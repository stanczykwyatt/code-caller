import Foundation
import SQLite3
import Testing
@testable import Copy2FACore

/// Archives made with NSArchiver, the same format Messages uses for `attributedBody`.
enum Archives {
    static let short = "BAtzdHJlYW10eXBlZIHoA4QBQISEhBlOU011dGFibGVBdHRyaWJ1dGVkU3RyaW5nAISEEk5TQXR0cmlidXRlZFN0cmluZwCEhAhOU09iamVjdACFkoSEhA9OU011dGFibGVTdHJpbmcBhIQITlNTdHJpbmcBlYQBKxNZb3VyIGNvZGUgaXMgNDgyOTEzhoQCaUkBE5KEhIQMTlNEaWN0aW9uYXJ5AJWEAWkAhoY="
    static let long = "BAtzdHJlYW10eXBlZIHoA4QBQISEhBlOU011dGFibGVBdHRyaWJ1dGVkU3RyaW5nAISEEk5TQXR0cmlidXRlZFN0cmluZwCEhAhOU09iamVjdACFkoSEhA9OU011dGFibGVTdHJpbmcBhIQITlNTdHJpbmcBlYQBK4GiAFlvdXIgQ2hhc2Ugb25lLXRpbWUgY29kZSBpcyA0ODI5MTM3MC4gRG9uJ3Qgc2hhcmUgaXQuIFlvdXIgQ2hhc2Ugb25lLXRpbWUgY29kZSBpcyA0ODI5MTM3MC4gRG9uJ3Qgc2hhcmUgaXQuIFlvdXIgQ2hhc2Ugb25lLXRpbWUgY29kZSBpcyA0ODI5MTM3MC4gRG9uJ3Qgc2hhcmUgaXQuIIaEAmlJAYGiAJKEhIQMTlNEaWN0aW9uYXJ5AJWEAWkAhoY="
    static let unicode = "BAtzdHJlYW10eXBlZIHoA4QBQISEhBlOU011dGFibGVBdHRyaWJ1dGVkU3RyaW5nAISEEk5TQXR0cmlidXRlZFN0cmluZwCEhAhOU09iamVjdACFkoSEhA9OU011dGFibGVTdHJpbmcBhIQITlNTdHJpbmcBlYQBKx3oqo3oqLzjgrPjg7zjg4nvvJo0ODI5MTMg8J+UkIaEAmlJAQ+ShISEDE5TRGljdGlvbmFyeQCVhAFpAIaG"

    static func data(_ base64: String) -> Data { Data(base64Encoded: base64)! }
}

@Suite("Message text")
struct MessageText {
    @Test func shortText() {
        #expect(TypedStreamText.string(from: Archives.data(Archives.short)) == "Your code is 482913")
    }

    @Test func textLongerThan127Bytes() {
        let text = TypedStreamText.string(from: Archives.data(Archives.long))
        #expect(text?.hasPrefix("Your Chase one-time code is 48291370.") == true)
        #expect(text?.utf8.count == 162)
    }

    @Test func unicodeText() {
        #expect(TypedStreamText.string(from: Archives.data(Archives.unicode)) == "認証コード：482913 🔐")
    }

    @Test func rejectsDamagedData() {
        #expect(TypedStreamText.string(from: Data()) == nil)
        #expect(TypedStreamText.string(from: Data("not an archive".utf8)) == nil)
        // Cut off in the middle of the text.
        #expect(TypedStreamText.string(from: Archives.data(Archives.long).prefix(140)) == nil)
    }
}

@Suite("Reading Messages")
struct ReadingMessages {
    /// Builds a database with the same tables and columns Messages uses.
    static func makeDatabase(rows: [(date: Date, text: String?, body: Data?, sender: String, fromMe: Bool)]) throws -> String {
        let path = FileManager.default.temporaryDirectory.appendingPathComponent("chat-\(UUID().uuidString).db").path
        var database: OpaquePointer?
        try #require(sqlite3_open(path, &database) == SQLITE_OK)
        defer { sqlite3_close(database) }
        let schema = """
            CREATE TABLE handle (ROWID INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT NOT NULL, service TEXT);
            CREATE TABLE message (ROWID INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT, attributedBody BLOB,
                handle_id INTEGER DEFAULT 0, date INTEGER, is_from_me INTEGER DEFAULT 0);
            """
        try #require(sqlite3_exec(database, schema, nil, nil, nil) == SQLITE_OK)

        for row in rows {
            var statement: OpaquePointer?
            sqlite3_prepare_v2(database, "INSERT INTO handle (id) VALUES (?)", -1, &statement, nil)
            sqlite3_bind_text(statement, 1, row.sender, -1, transient)
            sqlite3_step(statement)
            sqlite3_finalize(statement)
            let handle = sqlite3_last_insert_rowid(database)

            sqlite3_prepare_v2(database, "INSERT INTO message (text, attributedBody, handle_id, date, is_from_me) VALUES (?, ?, ?, ?, ?)", -1, &statement, nil)
            if let text = row.text { sqlite3_bind_text(statement, 1, text, -1, transient) } else { sqlite3_bind_null(statement, 1) }
            if let body = row.body {
                _ = body.withUnsafeBytes { sqlite3_bind_blob(statement, 2, $0.baseAddress, Int32(body.count), transient) }
            } else {
                sqlite3_bind_null(statement, 2)
            }
            sqlite3_bind_int64(statement, 3, handle)
            sqlite3_bind_int64(statement, 4, MessagesStore.appleTimestamp(row.date))
            sqlite3_bind_int(statement, 5, row.fromMe ? 1 : 0)
            try #require(sqlite3_step(statement) == SQLITE_DONE)
            sqlite3_finalize(statement)
        }
        return path
    }

    static let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

    @Test func readsRecentIncomingTextsNewestFirst() throws {
        let now = Date.now
        let path = try Self.makeDatabase(rows: [
            (now.addingTimeInterval(-120), "Your Uber code: 1234", nil, "+14155550100", false),
            (now.addingTimeInterval(-30), nil, Archives.data(Archives.short), "32665", false),
            (now.addingTimeInterval(-20), "On my way!", nil, "+14155550199", true),
            (now.addingTimeInterval(-3_600), "Your old code is 999999", nil, "+14155550100", false),
        ])
        defer { try? FileManager.default.removeItem(atPath: path) }

        let messages = try MessagesStore.messages(at: path, since: now.addingTimeInterval(-600))
        #expect(messages.map(\.text) == ["Your code is 482913", "Your Uber code: 1234"])
        #expect(messages.map(\.sender) == ["32665", "+14155550100"])
        #expect(abs(messages[0].received.timeIntervalSince(now.addingTimeInterval(-30))) < 0.001)
    }

    @Test func missingDatabase() {
        #expect(throws: MessagesStoreError.missing) {
            try MessagesStore.messages(at: "/nonexistent/chat.db", since: .now)
        }
        #expect(MessagesStore.canRead("/nonexistent/chat.db") == false)
    }

    @Test func oldSecondBasedDates() {
        let date = MessagesStore.date(fromAppleTimestamp: 700_000_000)
        #expect(date == Date(timeIntervalSinceReferenceDate: 700_000_000))
        let modern = MessagesStore.date(fromAppleTimestamp: 700_000_000_000_000_000)
        #expect(modern == Date(timeIntervalSinceReferenceDate: 700_000_000))
    }
}

@Suite("Codes in texts")
struct CodesInTexts {
    @Test(arguments: [
        ("Your Uber code: 1234. Never share this code. Reply STOP ALL to +1 415-237-0403 to unsubscribe.", "1234"),
        ("<#> 482913 is your verification code. FA+9qCX9VSu", "482913"),
        ("Chase: Your one-time code is 48291370. Don't share it. We'll never call or text you for it.", "48291370"),
        ("Duo Passcode: 4829137", "4829137"),
        ("Your Apple Account code is: 482913. Don't share it with anyone.", "482913"),
        ("G-482913 is your Google verification code.", "482913"),
        ("Your PIN is 4829. It expires in 10 minutes.", "4829"),
        ("[Microsoft] Use 482913 as the security code for your account.", "482913"),
        ("Tu código de verificación de WhatsApp es 482-913", "482913"),
        ("您的验证码是482913，5分钟内有效。", "482913"),
    ])
    func findsCode(text: String, code: String) {
        #expect(CodeExtractor.code(inText: text) == code)
    }

    @Test(arguments: [
        "Use code SAVE20 at checkout for 20% off. Reply STOP to opt out.",
        "Hey, are we still meeting at 5:30? Call me at 555-1234",
        "Your package 4829137 will arrive today.",
        "Flash sale! Code WELCOME10 gets you 10% off your first order",
        "Your total is $1999. Thanks for shopping with us!",
    ])
    func ignoresText(text: String) {
        #expect(CodeExtractor.code(inText: text) == nil)
    }
}

@Suite("Fixes from review")
struct ReviewFixes {
    @Test(arguments: [
        "Your PayPal code is 482913. Use it to finish checkout.",
        "Your Target login code is 482913. Never share it. Shop deals at Target.",
        "Your Venmo code is 482913. Turn off your alerts in Settings.",
        "Your verification code is 482913 for checkout",
    ])
    func numberCodesNearShoppingWordsInTexts(text: String) {
        #expect(CodeExtractor.code(inText: text) == "482913")
    }

    @Test(arguments: [
        ("Your login code", "Your login code is 482913. Shop now with the new app!"),
        ("Verify your email", "Your verification code is 482913 — use it at checkout to finish signing up."),
        ("Sign in to Example", "Enter 482913 to sign in.\nDeals picked for you"),
    ])
    func numberCodesNearShoppingWordsInEmails(subject: String, body: String) {
        #expect(CodeExtractor.code(subject: subject, body: body) == "482913")
    }

    @Test(arguments: [
        "I'll pin the address. Meet at 1830 Market St",
        "What's the zip code? It's 94110",
        "Use promo code 2041 for free shipping",
    ])
    func everydayTextsAreNotCodes(text: String) {
        #expect(CodeExtractor.code(inText: text) == nil)
    }
}
