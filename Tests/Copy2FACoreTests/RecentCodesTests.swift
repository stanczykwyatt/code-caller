import Foundation
import Testing
@testable import Copy2FACore

private struct Sample: Equatable {
    let code: String
    let minutesAgo: Double
    let sender: String

    init(_ code: String, _ minutesAgo: Double, _ sender: String = "") {
        self.code = code
        self.minutesAgo = minutesAgo
        self.sender = sender
    }
}

private let now = Date(timeIntervalSinceReferenceDate: 800_000_000)

private func pick(_ samples: [Sample], limit: Int = RecentCodes.limit) -> [Sample] {
    RecentCodes.pick(samples, limit: limit, code: \.code, received: { now.addingTimeInterval(-$0.minutesAgo * 60) })
}

@Suite("Recent codes")
struct RecentCodesTests {
    @Test func putsTheNewestFirst() {
        let picked = pick([Sample("111111", 4), Sample("222222", 1), Sample("333333", 9)])
        #expect(picked.map(\.code) == ["222222", "111111", "333333"])
    }

    @Test func showsAtMostFive() {
        let samples = (1...8).map { Sample(String(repeating: "\($0)", count: 6), Double($0)) }
        let picked = pick(samples)
        #expect(picked.count == 5)
        #expect(picked.map(\.code) == ["111111", "222222", "333333", "444444", "555555"])
    }

    @Test func keepsTheNewestCopyOfACodeSentTwice() {
        // Some services send the same code by email and by text.
        let picked = pick([Sample("482913", 3, "Mail"), Sample("771204", 2), Sample("482913", 1, "Messages")])
        #expect(picked == [Sample("482913", 1, "Messages"), Sample("771204", 2)])
    }

    @Test func duplicatesDontUseUpTheLimit() {
        let samples = [Sample("111111", 1), Sample("111111", 2), Sample("222222", 3), Sample("333333", 4)]
        #expect(pick(samples, limit: 3).map(\.code) == ["111111", "222222", "333333"])
    }

    @Test func keepsTheOriginalOrderForCodesThatArrivedTogether() {
        let picked = pick([Sample("111111", 2), Sample("222222", 2), Sample("333333", 2)])
        #expect(picked.map(\.code) == ["111111", "222222", "333333"])
    }

    @Test func oneIsTheNewestCode() {
        #expect(pick([Sample("111111", 4), Sample("222222", 1)], limit: 1).map(\.code) == ["222222"])
    }

    @Test func handlesNothingAndZero() {
        #expect(pick([]).isEmpty)
        #expect(pick([Sample("111111", 1)], limit: 0).isEmpty)
        #expect(pick([Sample("111111", 1)], limit: -3).isEmpty)
    }

    @Test func handlesThousandsOfCodesQuickly() {
        let samples = (0..<5_000).map { Sample(String(format: "%06d", $0 % 2_500), Double($0 % 97)) }
        let picked = pick(samples)
        #expect(picked.count == 5)
        #expect(picked.allSatisfy { $0.minutesAgo == 0 })
        #expect(Set(picked.map(\.code)).count == 5)
    }
}

@Suite("Recent codes from texts")
struct RecentCodesFromTexts {
    /// Reads a Messages database and keeps the codes the way Copy 2FA's recent codes list does.
    @Test func findsEachRecentCodeNewestFirst() throws {
        let now = Date.now
        let path = try ReadingMessages.makeDatabase(rows: [
            (now.addingTimeInterval(-60), "Your Uber code: 1234", nil, "+14155550100", false),
            (now.addingTimeInterval(-50), "See you at 6!", nil, "+14155550199", false),
            (now.addingTimeInterval(-40), "G-482913 is your Google verification code.", nil, "22000", false),
            (now.addingTimeInterval(-30), "Chase: Your one-time code is 48291370. Don't share it.", nil, "24273", false),
            (now.addingTimeInterval(-25), "Your Apple Account code is: 771204. Don't share it with anyone.", nil, "+14155550123", true),
            (now.addingTimeInterval(-20), "G-482913 is your Google verification code.", nil, "22000", false),
            (now.addingTimeInterval(-3_600), "Your old code is 999999", nil, "+14155550100", false),
        ])
        defer { try? FileManager.default.removeItem(atPath: path) }

        let messages = try MessagesStore.messages(at: path, since: now.addingTimeInterval(-600))
        let codes = messages.compactMap { message in
            CodeExtractor.code(inText: message.text).map { (code: $0, sender: message.sender, received: message.received) }
        }
        let picked = RecentCodes.pick(codes, code: \.code, received: \.received)
        #expect(picked.map(\.code) == ["482913", "48291370", "1234"])
        #expect(picked.map(\.sender) == ["22000", "24273", "+14155550100"])
    }
}
