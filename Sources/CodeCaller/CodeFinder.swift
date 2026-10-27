import CodeCallerCore
import Foundation

struct FoundCode: Sendable {
    let code: String
    let sender: String
    let received: Date
}

enum Lookup: Sendable {
    case found(FoundCode)
    case nothingFound
    case needsMailAccess
    case mailMissing
    case failed
}

/// Finds the newest verification code in Mail.
enum CodeFinder {
    static func findLatest(lookbackMinutes: Int, checkForNewMail: Bool) async -> Lookup {
        do {
            try await MailReader.launchIfNeeded()
            if let found = try await newestCode(lookbackMinutes: lookbackMinutes) { return .found(found) }
            guard checkForNewMail else { return .nothingFound }

            // The code may still be on its way, so fetch new mail and look a few more times.
            try await MailReader.checkForNewMail()
            for delay in [1.5, 2.0, 3.0] {
                try await Task.sleep(for: .seconds(delay))
                if let found = try await newestCode(lookbackMinutes: lookbackMinutes) { return .found(found) }
            }
            return .nothingFound
        } catch MailError.notAllowed {
            return .needsMailAccess
        } catch MailError.notInstalled {
            return .mailMissing
        } catch {
            return .failed
        }
    }

    private static func newestCode(lookbackMinutes: Int) async throws -> FoundCode? {
        for message in try await MailReader.recentMessages(within: lookbackMinutes * 60) {
            if let code = CodeExtractor.code(subject: message.subject, body: message.body) {
                return FoundCode(code: code, sender: SenderName.display(message.sender), received: message.received)
            }
        }
        return nil
    }
}
