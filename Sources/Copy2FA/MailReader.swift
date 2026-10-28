import AppKit

struct MailMessage: Sendable {
    let received: Date
    let sender: String
    let subject: String
    let body: String
}

enum MailAccess: Sendable {
    case allowed, denied, unknown
}

enum MailError: Error {
    case notAllowed
    case notInstalled
    case failed(code: Int)
}

/// Reads recent messages from Apple Mail through Apple Events.
///
/// The app's entitlements allow events to Mail and nothing else, and every
/// script here only reads. Messages stay in memory just long enough to find a
/// code and are never written anywhere.
enum MailReader {
    static let bundleID = "com.apple.mail"

    /// NSAppleScript can't run two scripts at once, so every script runs on this queue.
    private static let queue = DispatchQueue(label: "com.stanczykwyatt.CodeCaller.mail", qos: .userInitiated)

    static var isRunning: Bool {
        NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).contains { $0.isFinishedLaunching }
    }

    /// Opens Mail in the background if it isn't running, since it has to be open to answer.
    @MainActor
    static func launchIfNeeded() async throws {
        guard !isRunning else { return }
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) else {
            throw MailError.notInstalled
        }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = false
        configuration.hides = true
        configuration.addsToRecentItems = false
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            NSWorkspace.shared.openApplication(at: url, configuration: configuration) { _, error in
                if let error { continuation.resume(throwing: error) } else { continuation.resume() }
            }
        }
        var attempts = 0
        while !isRunning, attempts < 20 {
            try await Task.sleep(for: .milliseconds(250))
            attempts += 1
        }
    }

    /// Inbox messages received in the last `seconds`, newest first.
    static func recentMessages(within seconds: Int) async throws -> [MailMessage] {
        let messages = try await run(recentMessagesScript(seconds: seconds)) { list in
            (0..<list.numberOfItems).compactMap { index -> MailMessage? in
                guard let item = list.atIndex(index + 1), item.numberOfItems == 4,
                      let received = item.atIndex(1)?.dateValue else { return nil }
                return MailMessage(
                    received: received,
                    sender: item.atIndex(2)?.stringValue ?? "",
                    subject: item.atIndex(3)?.stringValue ?? "",
                    body: item.atIndex(4)?.stringValue ?? ""
                )
            }
        }
        return messages.sorted { $0.received > $1.received }
    }

    /// Asks Mail to fetch new messages for every account.
    static func checkForNewMail() async throws {
        let script = """
        with timeout of 10 seconds
            tell application id "com.apple.mail" to check for new mail
        end timeout
        """
        try await run(script) { _ in () }
    }

    /// Asks Mail for its own name, which reads nothing from your email. The first time, this is
    /// what makes macOS ask whether Copy 2FA may control Mail.
    static func confirmAccess() async -> MailAccess {
        let script = """
        with timeout of 120 seconds
            tell application id "com.apple.mail" to get name
        end timeout
        """
        do {
            try await run(script) { _ in () }
            return .allowed
        } catch MailError.notAllowed {
            return .denied
        } catch {
            return .unknown
        }
    }

    /// Whether access to Mail is turned off in System Settings. Mail has to be running to tell.
    ///
    /// This check can report access as granted before macOS has ever asked, so only a
    /// denial is trusted. `confirmAccess()` is the real test.
    static func isDenied() async -> Bool {
        await withCheckedContinuation { continuation in
            queue.async {
                let target = NSAppleEventDescriptor(bundleIdentifier: bundleID)
                let status = AEDeterminePermissionToAutomateTarget(target.aeDesc, AEEventClass(kAECoreSuite), AEEventID(kAEGetData), false)
                continuation.resume(returning: Int(status) == Int(errAEEventNotPermitted))
            }
        }
    }

    // MARK: - Scripts

    private static func run<Result: Sendable>(
        _ source: String,
        parse: @escaping @Sendable (NSAppleEventDescriptor) -> Result
    ) async throws -> Result {
        try await withCheckedThrowingContinuation { continuation in
            queue.async {
                var errorInfo: NSDictionary?
                guard let output = NSAppleScript(source: source)?.executeAndReturnError(&errorInfo), errorInfo == nil else {
                    let code = errorInfo?[NSAppleScript.errorNumber] as? Int ?? 0
                    continuation.resume(throwing: code == Int(errAEEventNotPermitted) ? MailError.notAllowed : MailError.failed(code: code))
                    return
                }
                continuation.resume(returning: parse(output))
            }
        }
    }

    /// Only an integer is put into the script, so message content can never change what it does.
    private static func recentMessagesScript(seconds: Int) -> String {
        """
        set cutoff to (current date) - \(seconds)
        set found to {}
        with timeout of 20 seconds
            tell application id "com.apple.mail"
                try
                    set recent to (every message of inbox whose date received ≥ cutoff)
                on error
                    -- Without a combined inbox, look in each account's inbox instead.
                    set recent to {}
                    repeat with anAccount in accounts
                        try
                            set recent to recent & (every message of mailbox "INBOX" of anAccount whose date received ≥ cutoff)
                        end try
                    end repeat
                end try
                -- Codes sometimes get marked as junk.
                try
                    set recent to recent & (every message of junk mailbox whose date received ≥ cutoff)
                end try
                repeat with aMessage in recent
                    if (count of found) ≥ 25 then exit repeat
                    try
                        set end of found to {date received of aMessage, sender of aMessage, subject of aMessage, content of aMessage}
                    end try
                end repeat
            end tell
        end timeout
        return found
        """
    }
}
