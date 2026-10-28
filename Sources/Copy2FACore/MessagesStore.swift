import Foundation
import SQLite3

public struct TextMessage: Sendable, Equatable {
    public let received: Date
    public let sender: String
    public let text: String

    public init(received: Date, sender: String, text: String) {
        self.received = received
        self.sender = sender
        self.text = text
    }
}

public enum MessagesStoreError: Error, Equatable {
    /// macOS blocked the read. Messages needs Full Disk Access.
    case noAccess
    /// There's no Messages database, because Messages has never been set up.
    case missing
    /// The database couldn't be read, for example because a macOS update changed it.
    case unreadable(String)
}

public enum UserHome {
    /// The user's real home folder. Inside the App Sandbox, `NSHomeDirectory()` is the app's container instead.
    public static var path: String {
        getpwuid(getuid()).flatMap { String(validatingCString: $0.pointee.pw_dir) } ?? NSHomeDirectory()
    }
}

/// Reads recent incoming texts from the Messages database, read-only.
public enum MessagesStore {
    /// `~/Library/Messages/chat.db` in the real home folder, not the app's sandbox container.
    public static var defaultPath: String {
        (UserHome.path as NSString).appendingPathComponent("Library/Messages/chat.db")
    }

    /// Whether macOS currently lets this app open the database.
    public static func canRead(_ path: String = defaultPath) -> Bool {
        let file = open(path, O_RDONLY)
        guard file >= 0 else { return false }
        close(file)
        return true
    }

    /// Incoming messages received since `cutoff`, newest first.
    public static func messages(at path: String = defaultPath, since cutoff: Date, limit: Int = 25) throws -> [TextMessage] {
        let file = open(path, O_RDONLY)
        guard file >= 0 else {
            throw errno == ENOENT ? MessagesStoreError.missing : MessagesStoreError.noAccess
        }
        close(file)

        var database: OpaquePointer?
        guard sqlite3_open_v2(path, &database, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, nil) == SQLITE_OK else {
            defer { sqlite3_close(database) }
            throw MessagesStoreError.unreadable(String(cString: sqlite3_errmsg(database)))
        }
        defer { sqlite3_close(database) }

        let query = """
            SELECT message.date, message.text, message.attributedBody, handle.id
            FROM message LEFT JOIN handle ON message.handle_id = handle.ROWID
            WHERE message.is_from_me = 0 AND message.date >= ?1
            ORDER BY message.date DESC
            LIMIT ?2
            """
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(database, query, -1, &statement, nil) == SQLITE_OK else {
            throw MessagesStoreError.unreadable(String(cString: sqlite3_errmsg(database)))
        }
        defer { sqlite3_finalize(statement) }
        sqlite3_bind_int64(statement, 1, appleTimestamp(cutoff))
        sqlite3_bind_int(statement, 2, Int32(limit))

        var messages: [TextMessage] = []
        var step = sqlite3_step(statement)
        while step == SQLITE_ROW {
            if let text = text(of: statement), !text.isEmpty {
                let sender = sqlite3_column_text(statement, 3).map { String(cString: $0) } ?? ""
                messages.append(TextMessage(received: date(fromAppleTimestamp: sqlite3_column_int64(statement, 0)), sender: sender, text: text))
            }
            step = sqlite3_step(statement)
        }
        guard step == SQLITE_DONE else {
            throw MessagesStoreError.unreadable(String(cString: sqlite3_errmsg(database)))
        }
        return messages
    }

    /// Newer messages keep their text only in `attributedBody`.
    private static func text(of statement: OpaquePointer?) -> String? {
        if let text = sqlite3_column_text(statement, 1).map({ String(cString: $0) }), !text.isEmpty {
            return text
        }
        guard let blob = sqlite3_column_blob(statement, 2) else { return nil }
        let data = Data(bytes: blob, count: Int(sqlite3_column_bytes(statement, 2)))
        return TypedStreamText.string(from: data)
    }

    /// Messages stores dates as nanoseconds since January 1, 2001.
    static func appleTimestamp(_ date: Date) -> Int64 {
        Int64(date.timeIntervalSinceReferenceDate * 1_000_000_000)
    }

    static func date(fromAppleTimestamp value: Int64) -> Date {
        // Databases from before macOS 10.13 stored seconds instead.
        let seconds = value > 10_000_000_000 ? Double(value) / 1_000_000_000 : Double(value)
        return Date(timeIntervalSinceReferenceDate: seconds)
    }
}
