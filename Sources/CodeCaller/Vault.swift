import AppKit
import CodeCallerCore
import LocalAuthentication
import Observation
import Security

enum VaultError: Error {
    case keychain(OSStatus)
    case locked
}

/// Authenticator accounts, kept in the login keychain and only loaded after Touch ID
/// or the Mac's password.
///
/// Secrets stay in memory only while the vault is unlocked. It locks again after the
/// time chosen in Settings, and right away when the screen locks or the Mac sleeps.
@MainActor
@Observable
final class Vault {
    private(set) var accounts: [OTPAccount] = []
    private(set) var isUnlocked = false
    @ObservationIgnored private var lockTask: Task<Void, Never>?
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

    /// Asks for Touch ID or the Mac's password, then loads the accounts.
    func unlock(reason: String) async -> Bool {
        if isUnlocked { return true }
        guard await Self.authenticate(reason: reason) else { return false }
        do {
            accounts = try Keychain.load()
            isUnlocked = true
            return true
        } catch {
            return false
        }
    }

    func lock() {
        lockTask?.cancel()
        accounts = []
        isUnlocked = false
    }

    /// Locks after `seconds`, or right away when it's zero.
    func lock(after seconds: Int) {
        lockTask?.cancel()
        guard seconds > 0 else { return lock() }
        lockTask = Task {
            try? await Task.sleep(for: .seconds(seconds))
            guard !Task.isCancelled else { return }
            lock()
        }
    }

    /// Adds new accounts and skips ones already in the vault. Returns how many were added.
    func add(_ newAccounts: [OTPAccount]) throws -> Int {
        guard isUnlocked else { throw VaultError.locked }
        var added = 0
        var updated = accounts
        for account in newAccounts where !updated.contains(where: { $0.isSameAccount(as: account) }) {
            updated.append(account)
            added += 1
        }
        try Keychain.save(updated)
        accounts = updated
        return added
    }

    func delete(_ account: OTPAccount) throws {
        guard isUnlocked else { throw VaultError.locked }
        let updated = accounts.filter { $0.id != account.id }
        try Keychain.save(updated)
        accounts = updated
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
