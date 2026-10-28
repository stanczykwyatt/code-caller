import CoreImage
import Foundation
import ImageIO

public struct ImportResult: Sendable, Equatable {
    public var accounts: [OTPAccount]
    /// Entries that were found but can't be used, like Steam accounts or ones without a valid secret.
    public var skipped: Int

    public init(accounts: [OTPAccount] = [], skipped: Int = 0) {
        self.accounts = accounts
        self.skipped = skipped
    }

    mutating func add(_ other: ImportResult) {
        accounts += other.accounts
        skipped += other.skipped
    }
}

public enum ImportError: Error, Equatable {
    /// The export is password-protected. Export it again without encryption.
    case encrypted
    /// The image has no QR code in it.
    case noQRCode
    /// Not a format Copy 2FA knows.
    case unrecognized
}

/// Turns exports from other apps into authenticator accounts.
///
/// Reads QR code images, `otpauth://` links, Google Authenticator transfer codes,
/// CSV exports from Apple Passwords, Safari, 1Password and Bitwarden, JSON exports
/// from Bitwarden, Aegis, 2FAS and andOTP, and the `~/.2fa` file of the 2fa
/// command-line tool. Only one-time password secrets are kept; passwords and
/// anything else in an export are never stored.
public enum AccountImporter {
    public static func accounts(from data: Data) throws -> ImportResult {
        if let codes = QRCodeReader.strings(in: data) {
            guard !codes.isEmpty else { throw ImportError.noQRCode }
            // A screenshot can include unrelated QR codes, like an App Store link. Those are ignored.
            var result = ImportResult()
            for code in codes {
                if let found = try? accounts(fromText: code) { result.add(found) }
            }
            guard !result.accounts.isEmpty || result.skipped > 0 else { throw ImportError.unrecognized }
            return result
        }
        guard let text = String(data: data, encoding: .utf8) else { throw ImportError.unrecognized }
        return try accounts(fromText: text)
    }

    public static func accounts(fromText text: String) throws -> ImportResult {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines.union(CharacterSet(charactersIn: "\u{FEFF}")))
        if trimmed.hasPrefix("{") || trimmed.hasPrefix("[") {
            return try JSONExports.accounts(from: Data(trimmed.utf8))
        }
        if let result = CSVExport.accounts(from: trimmed) {
            return result
        }
        if let result = TwoFAFile.accounts(from: trimmed) {
            return result
        }

        var result = ImportResult()
        for line in trimmed.components(separatedBy: .newlines) {
            for link in links(in: line) {
                if link.lowercased().hasPrefix("otpauth-migration://") {
                    if let export = GoogleAuthenticatorExport.accounts(from: link) {
                        result.add(ImportResult(accounts: export.accounts, skipped: export.skipped))
                    } else {
                        result.skipped += 1
                    }
                } else if let account = OTPAuthURL.account(from: link) {
                    result.accounts.append(account)
                } else {
                    result.skipped += 1
                }
            }
        }
        guard !result.accounts.isEmpty || result.skipped > 0 else { throw ImportError.unrecognized }
        return result
    }

    /// The links in one line of text. A line that's only a link is used whole, since labels
    /// like "Macy's" or "Acme, Inc" contain characters that would otherwise end the link.
    private static func links(in line: String) -> [String] {
        let line = line.trimmingCharacters(in: .whitespaces)
        let lowercased = line.lowercased()
        if lowercased.hasPrefix("otpauth://") || lowercased.hasPrefix("otpauth-migration://") {
            return [line]
        }
        let range = NSRange(line.startIndex..., in: line)
        return embeddedLink.matches(in: line, range: range).compactMap { Range($0.range, in: line).map { String(line[$0]) } }
    }

    private static let embeddedLink = try! NSRegularExpression(pattern: "otpauth(?:-migration)?://[^\\s\"<>]+", options: [.caseInsensitive])
}

public enum QRCodeReader {
    /// The text in every QR code in an image, or nil if the data isn't an image.
    public static func strings(in imageData: Data) -> [String]? {
        guard let source = CGImageSourceCreateWithData(imageData as CFData, nil),
              CGImageSourceGetCount(source) > 0,
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return nil }
        let detector = CIDetector(ofType: CIDetectorTypeQRCode, context: nil, options: [CIDetectorAccuracy: CIDetectorAccuracyHigh])
        let features = detector?.features(in: CIImage(cgImage: image)) ?? []
        return features.compactMap { ($0 as? CIQRCodeFeature)?.messageString }
    }
}

// MARK: - 2fa

/// Reads the `~/.2fa` file kept by rsc/2fa, the command-line authenticator.
///
/// Each line is `name digits key` for a time-based key, followed by a 20-digit
/// counter for a counter-based one. The counter is the last one 2fa used.
enum TwoFAFile {
    private static let line = try! NSRegularExpression(pattern: "^(\\S+) ([678]) ([A-Za-z2-7]+=*)(?: ([0-9]{20}))?$")

    /// The accounts in the file, or nil if the text isn't in this format.
    static func accounts(from text: String) -> ImportResult? {
        let lines = text.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
        guard !lines.isEmpty else { return nil }

        var result = ImportResult()
        for line in lines {
            let range = NSRange(line.startIndex..., in: line)
            guard let match = Self.line.firstMatch(in: line, range: range) else { return nil }
            func group(_ index: Int) -> String? {
                Range(match.range(at: index), in: line).map { String(line[$0]) }
            }
            guard let name = group(1), let digits = group(2).flatMap({ Int($0) }),
                  let secret = group(3).flatMap(Base32.decode) else {
                result.skipped += 1
                continue
            }
            let lastCounter = group(4).flatMap { UInt64($0) }
            let account = OTPAccount(
                issuer: name, name: "", secret: secret, digits: digits,
                kind: lastCounter == nil ? .totp : .hotp,
                counter: lastCounter.map { $0 &+ 1 } ?? 0
            )
            if account.isValid { result.accounts.append(account) } else { result.skipped += 1 }
        }
        return result
    }
}

// MARK: - CSV

enum CSVExport {
    /// Column names other apps use for one-time password secrets.
    private static let otpColumns: Set<String> = ["otpauth", "otp", "totp", "login_totp", "one-time password", "otp auth", "2fa", "authenticator key"]

    static func accounts(from text: String) -> ImportResult? {
        let rows = parse(text)
        guard let header = rows.first?.map({ $0.trimmingCharacters(in: .whitespaces).lowercased() }),
              let otpColumn = header.firstIndex(where: otpColumns.contains) else { return nil }
        let titleColumn = header.firstIndex { ["title", "name", "issuer"].contains($0) }
        let urlColumn = header.firstIndex { ["url", "login_uri", "website", "urls"].contains($0) }
        let userColumn = header.firstIndex { ["username", "login_username", "user", "email", "account"].contains($0) }

        var result = ImportResult()
        for row in rows.dropFirst() {
            func value(_ column: Int?) -> String {
                guard let column, column < row.count else { return "" }
                return row[column].trimmingCharacters(in: .whitespaces)
            }
            let secret = value(otpColumn)
            guard !secret.isEmpty else { continue }
            let issuer = value(titleColumn).isEmpty ? host(of: value(urlColumn)) : value(titleColumn)

            if secret.lowercased().hasPrefix("otpauth://") {
                guard var account = OTPAuthURL.account(from: secret) else {
                    result.skipped += 1
                    continue
                }
                if account.issuer.isEmpty { account.issuer = issuer }
                if account.name.isEmpty { account.name = value(userColumn) }
                result.accounts.append(account)
            } else if let key = Base32.decode(secret), OTPAccount(issuer: issuer, name: "", secret: key).isValid {
                result.accounts.append(OTPAccount(issuer: issuer, name: value(userColumn), secret: key))
            } else {
                result.skipped += 1
            }
        }
        return result
    }

    private static func host(of url: String) -> String {
        let host = URLComponents(string: url)?.host ?? URLComponents(string: "https://\(url)")?.host ?? ""
        return host.hasPrefix("www.") ? String(host.dropFirst(4)) : host
    }

    /// RFC 4180 rows: quoted fields may contain commas, quotes and line breaks.
    static func parse(_ text: String) -> [[String]] {
        var rows: [[String]] = []
        var row: [String] = []
        var field = ""
        var inQuotes = false
        var iterator = text.makeIterator()
        while let character = iterator.next() {
            if inQuotes {
                if character == "\"" {
                    if let next = iterator.next() {
                        if next == "\"" {
                            field.append("\"")
                        } else {
                            inQuotes = false
                            if next == "," {
                                row.append(field)
                                field = ""
                            } else if next == "\n" || next == "\r\n" {
                                row.append(field)
                                rows.append(row)
                                row = []
                                field = ""
                            } else {
                                field.append(next)
                            }
                        }
                    } else {
                        inQuotes = false
                    }
                } else {
                    field.append(character)
                }
            } else if character == "\"" && field.isEmpty {
                inQuotes = true
            } else if character == "," {
                row.append(field)
                field = ""
            } else if character == "\n" || character == "\r\n" {
                row.append(field)
                rows.append(row)
                row = []
                field = ""
            } else {
                field.append(character)
            }
        }
        if !field.isEmpty || !row.isEmpty {
            row.append(field)
            rows.append(row)
        }
        return rows
    }
}

// MARK: - JSON

enum JSONExports {
    static func accounts(from data: Data) throws -> ImportResult {
        guard let root = try? JSONSerialization.jsonObject(with: data) else { throw ImportError.unrecognized }

        if let list = root as? [[String: Any]] {
            return andOTP(list)
        }
        guard let object = root as? [String: Any] else { throw ImportError.unrecognized }
        if let items = object["items"] as? [[String: Any]] {
            return bitwarden(items)
        }
        if object["encrypted"] as? Bool == true {
            throw ImportError.encrypted
        }
        if let database = object["db"] {
            guard let entries = (database as? [String: Any])?["entries"] as? [[String: Any]] else {
                throw ImportError.encrypted  // Encrypted Aegis vaults store `db` as a string.
            }
            return aegis(entries)
        }
        if let services = object["services"] as? [[String: Any]] {
            if services.isEmpty, object["servicesEncrypted"] != nil { throw ImportError.encrypted }
            return twoFAS(services)
        }
        throw ImportError.unrecognized
    }

    private static func bitwarden(_ items: [[String: Any]]) -> ImportResult {
        var result = ImportResult()
        for item in items {
            guard let login = item["login"] as? [String: Any], let totp = login["totp"] as? String, !totp.isEmpty else {
                continue
            }
            let issuer = item["name"] as? String ?? ""
            let name = login["username"] as? String ?? ""
            if totp.lowercased().hasPrefix("otpauth://") {
                guard var account = OTPAuthURL.account(from: totp) else {
                    result.skipped += 1
                    continue
                }
                if account.issuer.isEmpty { account.issuer = issuer }
                if account.name.isEmpty { account.name = name }
                result.accounts.append(account)
            } else if let secret = Base32.decode(totp), OTPAccount(issuer: issuer, name: name, secret: secret).isValid {
                result.accounts.append(OTPAccount(issuer: issuer, name: name, secret: secret))
            } else {
                result.skipped += 1  // For example "steam://", which needs its own code format.
            }
        }
        return result
    }

    private static func aegis(_ entries: [[String: Any]]) -> ImportResult {
        var result = ImportResult()
        for entry in entries {
            let info = entry["info"] as? [String: Any] ?? [:]
            let account = makeAccount(
                type: entry["type"], issuer: entry["issuer"], name: entry["name"], secret: info["secret"],
                algorithm: info["algo"], digits: info["digits"], period: info["period"], counter: info["counter"]
            )
            if let account { result.accounts.append(account) } else { result.skipped += 1 }
        }
        return result
    }

    private static func twoFAS(_ services: [[String: Any]]) -> ImportResult {
        var result = ImportResult()
        for service in services {
            let otp = service["otp"] as? [String: Any] ?? [:]
            let account = makeAccount(
                type: otp["tokenType"], issuer: otp["issuer"] ?? service["name"], name: otp["account"], secret: service["secret"],
                algorithm: otp["algorithm"], digits: otp["digits"], period: otp["period"], counter: otp["counter"]
            )
            if let account { result.accounts.append(account) } else { result.skipped += 1 }
        }
        return result
    }

    private static func andOTP(_ entries: [[String: Any]]) -> ImportResult {
        var result = ImportResult()
        for entry in entries {
            var issuer = entry["issuer"] as? String ?? ""
            var name = entry["label"] as? String ?? ""
            if issuer.isEmpty, let colon = name.firstIndex(of: ":") {
                issuer = String(name[..<colon])
                name = String(name[name.index(after: colon)...])
            }
            let account = makeAccount(
                type: entry["type"], issuer: issuer, name: name, secret: entry["secret"],
                algorithm: entry["algorithm"], digits: entry["digits"], period: entry["period"], counter: entry["counter"]
            )
            if let account { result.accounts.append(account) } else { result.skipped += 1 }
        }
        return result
    }

    /// Builds an account from the fields apps name differently. Steam and other kinds return nil.
    private static func makeAccount(
        type: Any?, issuer: Any?, name: Any?, secret: Any?, algorithm: Any?, digits: Any?, period: Any?, counter: Any?
    ) -> OTPAccount? {
        let kind: OTPKind
        switch (type as? String)?.lowercased() ?? "totp" {
        case "totp": kind = .totp
        case "hotp": kind = .hotp
        default: return nil
        }
        guard let secret = (secret as? String).flatMap(Base32.decode) else { return nil }
        let algorithmName = algorithm as? String ?? "SHA1"
        guard let algorithm = OTPAlgorithm(name: algorithmName) else { return nil }
        let account = OTPAccount(
            issuer: issuer as? String ?? "",
            name: name as? String ?? "",
            secret: secret,
            algorithm: algorithm,
            digits: number(digits) ?? 6,
            period: number(period) ?? 30,
            kind: kind,
            counter: number(counter).map { UInt64(max(0, $0)) } ?? 0
        )
        return account.isValid ? account : nil
    }

    private static func number(_ value: Any?) -> Int? {
        if let value = value as? Int { return value }
        if let value = value as? String { return Int(value) }
        return nil
    }
}
