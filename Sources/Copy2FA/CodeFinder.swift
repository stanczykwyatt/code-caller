import Copy2FACore
import SwiftUI

/// Where a code came from, shown in the notice so it's clear which app it was read from.
enum CodeSource: Sendable {
    case mail, messages, authenticator

    var name: String {
        switch self {
        case .mail: "Mail"
        case .messages: "Messages"
        case .authenticator: "Authenticator"
        }
    }

    var symbol: String {
        switch self {
        case .mail: "envelope.fill"
        case .messages: "message.fill"
        case .authenticator: "lock.shield.fill"
        }
    }

    var tint: Color {
        switch self {
        case .mail: .blue
        case .messages: .green
        case .authenticator: .indigo
        }
    }
}

struct FoundCode: Sendable {
    let code: String
    let sender: String
    let source: CodeSource
    let received: Date
}

enum Lookup: Sendable {
    case found(FoundCode)
    case nothingFound
    case needsMailAccess
    case needsMessagesAccess
    case messagesFailed
    case mailMissing
    case noSources
    case failed
}

struct LookupOptions: Sendable {
    var lookbackMinutes: Int
    var readsMail: Bool
    var readsMessages: Bool
    var checksForNewMail: Bool
}

/// Finds the newest verification code across Mail and Messages.
enum CodeFinder {
    static func findLatest(_ options: LookupOptions) async -> Lookup {
        guard options.readsMail || options.readsMessages else { return .noSources }

        var attempt = await look(options)
        if attempt.code == nil, options.readsMail, options.checksForNewMail, attempt.mailProblem == nil {
            // The code may still be on its way, so fetch new mail and look a few more times.
            try? await MailReader.checkForNewMail()
            for delay in [1.5, 2.0, 3.0] where attempt.code == nil {
                try? await Task.sleep(for: .seconds(delay))
                attempt = await look(options)
            }
        }
        if let code = attempt.code { return .found(code) }
        return attempt.mailProblem ?? attempt.messagesProblem ?? .nothingFound
    }

    private struct Attempt {
        var code: FoundCode?
        var mailProblem: Lookup?
        var messagesProblem: Lookup?
    }

    /// Checks every turned-on source once and keeps the newest code.
    private static func look(_ options: LookupOptions) async -> Attempt {
        var attempt = Attempt()
        var codes: [FoundCode] = []
        let cutoff = Date.now.addingTimeInterval(-Double(options.lookbackMinutes * 60))

        if options.readsMessages {
            do {
                if let code = try await newestTextCode(since: cutoff) { codes.append(code) }
            } catch MessagesStoreError.noAccess {
                attempt.messagesProblem = .needsMessagesAccess
            } catch MessagesStoreError.missing {
                // Messages has never been set up, so there's nothing to find there.
            } catch {
                attempt.messagesProblem = .messagesFailed
            }
        }
        if options.readsMail {
            do {
                try await MailReader.launchIfNeeded()
                if let code = try await newestMailCode(lookbackMinutes: options.lookbackMinutes) { codes.append(code) }
            } catch MailError.notAllowed {
                attempt.mailProblem = .needsMailAccess
            } catch MailError.notInstalled {
                attempt.mailProblem = .mailMissing
            } catch {
                attempt.mailProblem = .failed
            }
        }
        attempt.code = codes.max { $0.received < $1.received }
        return attempt
    }

    private static func newestMailCode(lookbackMinutes: Int) async throws -> FoundCode? {
        for message in try await MailReader.recentMessages(within: lookbackMinutes * 60) {
            if let code = CodeExtractor.code(subject: message.subject, body: message.body) {
                return FoundCode(code: code, sender: SenderName.display(message.sender), source: .mail, received: message.received)
            }
        }
        return nil
    }

    private static func newestTextCode(since cutoff: Date) async throws -> FoundCode? {
        let messages = try await Task.detached(priority: .userInitiated) {
            try MessagesStore.messages(since: cutoff)
        }.value
        for message in messages {
            if let code = CodeExtractor.code(inText: message.text) {
                return FoundCode(code: code, sender: message.sender, source: .messages, received: message.received)
            }
        }
        return nil
    }
}
