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

struct FoundCode: Sendable, Equatable {
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

/// What looking for recent codes found.
enum RecentLookup: Sendable {
    /// Newest first, each code once.
    case found([FoundCode])
    case problem(Lookup)
}

/// Finds verification codes across Mail and Messages.
enum CodeFinder {
    static func findLatest(_ options: LookupOptions) async -> Lookup {
        let result = await find(options, limit: 1)
        if let code = result.codes.first { return .found(code) }
        return result.problem
    }

    /// The last few codes from the lookback window, for when the newest isn't the one you need.
    static func findRecent(_ options: LookupOptions) async -> RecentLookup {
        let result = await find(options, limit: RecentCodes.limit)
        if !result.codes.isEmpty { return .found(result.codes) }
        return .problem(result.problem)
    }

    /// Up to `limit` codes, newest first. When there are none, `problem` says why.
    private static func find(_ options: LookupOptions, limit: Int) async -> (codes: [FoundCode], problem: Lookup) {
        guard options.readsMail || options.readsMessages else { return ([], .noSources) }

        var attempt = await look(options, limit: limit)
        if attempt.codes.isEmpty, options.readsMail, options.checksForNewMail, attempt.mailProblem == nil {
            // The code may still be on its way, so fetch new mail and look a few more times.
            try? await MailReader.checkForNewMail()
            for delay in [1.5, 2.0, 3.0] where attempt.codes.isEmpty {
                try? await Task.sleep(for: .seconds(delay))
                attempt = await look(options, limit: limit)
            }
        }
        return (attempt.codes, attempt.mailProblem ?? attempt.messagesProblem ?? .nothingFound)
    }

    private struct Attempt {
        var codes: [FoundCode] = []
        var mailProblem: Lookup?
        var messagesProblem: Lookup?
    }

    /// Checks every turned-on source once and keeps the newest codes.
    private static func look(_ options: LookupOptions, limit: Int) async -> Attempt {
        var attempt = Attempt()
        var codes: [FoundCode] = []
        let cutoff = Date.now.addingTimeInterval(-Double(options.lookbackMinutes * 60))

        if options.readsMessages {
            do {
                codes += try await textCodes(since: cutoff, limit: limit)
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
                codes += try await mailCodes(lookbackMinutes: options.lookbackMinutes, limit: limit)
            } catch MailError.notAllowed {
                attempt.mailProblem = .needsMailAccess
            } catch MailError.notInstalled {
                attempt.mailProblem = .mailMissing
            } catch {
                attempt.mailProblem = .failed
            }
        }
        attempt.codes = RecentCodes.pick(codes, limit: limit, code: \.code, received: \.received)
        return attempt
    }

    /// Stops reading as soon as it has `limit` codes, so copying the newest one stays quick.
    private static func mailCodes(lookbackMinutes: Int, limit: Int) async throws -> [FoundCode] {
        var codes: [FoundCode] = []
        for message in try await MailReader.recentMessages(within: lookbackMinutes * 60) where codes.count < limit {
            if let code = CodeExtractor.code(subject: message.subject, body: message.body), !codes.contains(where: { $0.code == code }) {
                codes.append(FoundCode(code: code, sender: SenderName.display(message.sender), source: .mail, received: message.received))
            }
        }
        return codes
    }

    private static func textCodes(since cutoff: Date, limit: Int) async throws -> [FoundCode] {
        let messages = try await Task.detached(priority: .userInitiated) {
            try MessagesStore.messages(since: cutoff)
        }.value
        var codes: [FoundCode] = []
        for message in messages where codes.count < limit {
            if let code = CodeExtractor.code(inText: message.text), !codes.contains(where: { $0.code == code }) {
                codes.append(FoundCode(code: code, sender: message.sender, source: .messages, received: message.received))
            }
        }
        return codes
    }
}
