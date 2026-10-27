import AppKit
import CodeCallerCore
import LocalAuthentication
import Observation
import Security

enum VaultError: Error {
    case keychain(OSStatus)
    case locked
}

enum UnlockResult {
    case unlocked
    case cancelled
    /// The keychain refused, for example because its prompt was denied.
    case failed
}

/// Authenticator accounts, kept in the login keychain and only loaded after Touch ID
/// or the Mac's password.
///
/// Secrets stay in memory only while the vault is unlocked. It locks again after the
/// time chosen in Settings, and right away when the screen locks or the Mac sleeps.
/// Anything showing codes is told through `onLock`, so it can close at the same time.
@MainActor
@Observable
final class Vault {
    private(set) var accounts: [OTPAccount] = []
    private(set) var isUnlocked = false
    @ObservationIgnored var onLock: () -> Void = {}
    @ObservationIgnored private var lockTask: Task<Void, Never>?
    @ObservationIgnored private var pendingUnlock: Task<UnlockResult, Never>?
    @ObservationIgnored private var observers: [NSObjectProtocol] = []

    init() {
        let lock: @Sendable (Notification) -> Void = { [weak self] _ in
            MainActor.assumeIsolated { self?.lock() }
        }
        observers.append(DistributedNotificationCenter.default().addObserver(
            forName: Notification.Name("com.apple.screenIsLocked"), object: nil, queue: .main, using: lock
        ))
        for name in [NSWorkspace.screensDidSleepNotification, NSWorkspace.willSleepNotification, NSWorkspace.sessionDidResignActiveNotification] {
            observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: name, object: nil, queue: .main, using: lock))
        }
    }

    /// Asks for Touch ID or the Mac's password, then loads the accounts. With `fresh`, it
    /// asks even when the vault is already unlocked. Only one prompt is shown at a time.
    func unlock(reason: String, fresh: Bool = false) async -> UnlockResult {
        if let pendingUnlock { return await pendingUnlock.value }
        if isUnlocked && !fresh { return .unlocked }
        let attempt = Task { () -> UnlockResult in
            guard await Self.authenticate(reason: reason) else { return .cancelled }
            do {
                // Off the main thread, since the keychain can stop to ask for permission.
                let loaded = try await Task.detached { try Keychain.load() }.value
                accounts = loaded
                isUnlocked = true
                return .unlocked
            } catch {
                return .failed
            }
        }
        pendingUnlock = attempt
        let result = await attempt.value
        pendingUnlock = nil
        return result
    }

    func lock() {
        lockTask?.cancel()
        lockTask = nil
        let wasUnlocked = isUnlocked
        accounts = []
        isUnlocked = false
        if wasUnlocked { onLock() }
    }

    /// Locks after `seconds`, or right away when it's zero. Replaces any earlier timer.
    func lock(after seconds: Int) {
        guard isUnlocked else { return }
        lockTask?.cancel()
        guard seconds > 0 else { return lock() }
        lockTask = Task {
            try? await Task.sleep(for: .seconds(seconds))
            guard !Task.isCancelled else { return }
            lock()
        }
    }

    /// Adds new accounts and skips ones already in the vault. Returns how many were added.
    func add(_ newAccounts: [OTPAccount]) async throws -> Int {
        guard isUnlocked else { throw VaultError.locked }
        var added = 0
        var updated = accounts
        for account in newAccounts where !updated.contains(where: { $0.isSameAccount(as: account) }) {
            updated.append(account)
            added += 1
        }
        try await save(updated)
        return added
    }

    /// The code to copy for an account. A counter-based account moves to its next counter, and
    /// that's saved before the code is handed out, so the same code is never given twice.
    func code(for account: OTPAccount) async throws -> String {
        guard account.kind == .hotp else { return account.code() }
        guard isUnlocked, let index = accounts.firstIndex(where: { $0.id == account.id }) else { throw VaultError.locked }
        let next = accounts[index].usingNextCode()
        var updated = accounts
        updated[index] = next.account
        try await save(updated)
        return next.code
    }

    func delete(_ account: OTPAccount) async throws {
        guard isUnlocked else { throw VaultError.locked }
        try await save(accounts.filter { $0.id != account.id })
    }

    private func save(_ updated: [OTPAccount]) async throws {
        try await Task.detached { try Keychain.save(updated) }.value
        // The vault may have locked while saving, and a locked vault keeps nothing in memory.
        if isUnlocked { accounts = updated }
    }

    private static func authenticate(reason: String) async -> Bool {
        let context = LAContext()
        var error: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &error) else {
            // Without a login password there's nothing to check against.
            return error?.code == LAError.passcodeNotSet.rawValue
        }
        return await withCheckedContinuation { continuation in
            context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason) { success, _ in
                continuation.resume(returning: success)
            }
        }
    }
}

/// Stores every account as one item in the login keychain, readable only by this app.
private enum Keychain {
    static let service = "com.stanczykwyatt.CodeCaller.authenticator"
    static let account = "accounts"

    static func load() throws -> [OTPAccount] {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return [] }
        guard status == errSecSuccess, let data = result as? Data else { throw VaultError.keychain(status) }
        return try JSONDecoder().decode([OTPAccount].self, from: data)
    }

    static func save(_ accounts: [OTPAccount]) throws {
        let data = try JSONEncoder().encode(accounts)
        let status = SecItemUpdate(baseQuery as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if status == errSecItemNotFound {
            var item = baseQuery
            item[kSecValueData as String] = data
            item[kSecAttrLabel as String] = "Code Caller authenticator"
            item[kSecAttrDescription as String] = "One-time password secrets"
            let added = SecItemAdd(item as CFDictionary, nil)
            guard added == errSecSuccess else { throw VaultError.keychain(added) }
        } else if status != errSecSuccess {
            throw VaultError.keychain(status)
        }
    }

    private static var baseQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecAttrSynchronizable as String: false,
        ]
    }
}
