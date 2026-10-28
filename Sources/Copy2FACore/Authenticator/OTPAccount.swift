import CryptoKit
import Foundation

public enum OTPAlgorithm: String, Codable, Sendable, CaseIterable {
    case sha1 = "SHA1"
    case sha256 = "SHA256"
    case sha512 = "SHA512"

    init?(name: String) {
        self.init(rawValue: name.uppercased().replacingOccurrences(of: "-", with: ""))
    }
}

public enum OTPKind: String, Codable, Sendable {
    /// A new code every `period` seconds, the kind almost every website uses.
    case totp
    /// A new code each time one is used, tracked by a counter.
    case hotp
}

/// A one-time password account, the kind authenticator apps hold.
public struct OTPAccount: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var issuer: String
    public var name: String
    public var secret: Data
    public var algorithm: OTPAlgorithm
    public var digits: Int
    public var period: Int
    public var kind: OTPKind
    /// For counter-based accounts, the counter the next code uses.
    public var counter: UInt64

    public init(
        id: UUID = UUID(), issuer: String, name: String, secret: Data,
        algorithm: OTPAlgorithm = .sha1, digits: Int = 6, period: Int = 30,
        kind: OTPKind = .totp, counter: UInt64 = 0
    ) {
        self.id = id
        self.issuer = issuer.trimmingCharacters(in: .whitespaces)
        self.name = name.trimmingCharacters(in: .whitespaces)
        self.secret = secret
        self.algorithm = algorithm
        self.digits = digits
        self.period = period
        self.kind = kind
        self.counter = counter
    }

    /// Accounts saved before counter-based codes were supported have no kind or counter.
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(UUID.self, forKey: .id)
        issuer = try container.decode(String.self, forKey: .issuer)
        name = try container.decode(String.self, forKey: .name)
        secret = try container.decode(Data.self, forKey: .secret)
        algorithm = try container.decode(OTPAlgorithm.self, forKey: .algorithm)
        digits = try container.decode(Int.self, forKey: .digits)
        period = try container.decode(Int.self, forKey: .period)
        kind = try container.decodeIfPresent(OTPKind.self, forKey: .kind) ?? .totp
        counter = try container.decodeIfPresent(UInt64.self, forKey: .counter) ?? 0
    }

    /// Real secrets are at least 64 bits. Shorter ones are usually junk, like "NONE" in a CSV column.
    public static let minimumSecretLength = 8

    /// Whether the settings are ones every authenticator supports.
    public var isValid: Bool {
        secret.count >= Self.minimumSecretLength && (6...8).contains(digits) && (1...300).contains(period)
    }

    /// "GitHub (wyatt)", or whichever part is known.
    public var displayName: String {
        switch (issuer.isEmpty, name.isEmpty) {
        case (false, false): "\(issuer) (\(name))"
        case (false, true): issuer
        case (true, false): name
        case (true, true): "Unnamed account"
        }
    }

    /// The time-based code that's valid at `date`, per RFC 6238.
    public func code(at date: Date = .now) -> String {
        let period = max(self.period, 1)
        return code(forCounter: UInt64(max(0, date.timeIntervalSince1970) / Double(period)))
    }

    /// The code a counter-based account gives next, and the account with its counter moved on.
    public func usingNextCode() -> (code: String, account: OTPAccount) {
        var account = self
        account.counter &+= 1
        return (code(forCounter: counter), account)
    }

    /// The code for one counter value, per RFC 4226. Time-based codes use the time as the counter.
    public func code(forCounter counter: UInt64) -> String {
        // Imports only create valid accounts, but stored settings are clamped so damaged data can't crash.
        let digits = min(max(self.digits, 1), 9)
        var bigEndian = counter.bigEndian
        let message = Data(bytes: &bigEndian, count: 8)
        let key = SymmetricKey(data: secret)
        let mac: [UInt8] = switch algorithm {
        case .sha1: Array(HMAC<Insecure.SHA1>.authenticationCode(for: message, using: key))
        case .sha256: Array(HMAC<SHA256>.authenticationCode(for: message, using: key))
        case .sha512: Array(HMAC<SHA512>.authenticationCode(for: message, using: key))
        }
        let offset = Int(mac[mac.count - 1] & 0x0F)
        let truncated = (UInt32(mac[offset] & 0x7F) << 24)
            | (UInt32(mac[offset + 1]) << 16)
            | (UInt32(mac[offset + 2]) << 8)
            | UInt32(mac[offset + 3])
        let modulus = (0..<digits).reduce(UInt32(1)) { result, _ in result * 10 }
        let text = String(truncated % modulus)
        return String(repeating: "0", count: max(0, digits - text.count)) + text
    }

    /// Seconds until the current code changes.
    public func secondsRemaining(at date: Date = .now) -> Int {
        let period = max(self.period, 1)
        return period - Int(max(0, date.timeIntervalSince1970)) % period
    }

    /// Whether this is the same account as `other`, so imports don't add duplicates.
    public func isSameAccount(as other: OTPAccount) -> Bool {
        secret == other.secret && issuer.caseInsensitiveCompare(other.issuer) == .orderedSame
            && name.caseInsensitiveCompare(other.name) == .orderedSame
    }
}

public enum Base32 {
    private static let alphabet = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".utf8)

    /// Decodes a secret like "JBSW Y3DP-EHPK 3PXP", ignoring case, spaces, hyphens and padding.
    public static func decode(_ text: String) -> Data? {
        var bits = 0
        var buffer = 0
        var output = Data()
        for character in text.uppercased().utf8 {
            if character == UInt8(ascii: " ") || character == UInt8(ascii: "-") || character == UInt8(ascii: "=") {
                continue
            }
            guard let value = alphabet.firstIndex(of: character) else { return nil }
            buffer = (buffer << 5) | value
            bits += 5
            if bits >= 8 {
                bits -= 8
                output.append(UInt8((buffer >> bits) & 0xFF))
            }
        }
        return output.isEmpty ? nil : output
    }

    public static func encode(_ data: Data) -> String {
        var bits = 0
        var buffer = 0
        var output = ""
        for byte in data {
            buffer = (buffer << 8) | Int(byte)
            bits += 8
            while bits >= 5 {
                bits -= 5
                output.append(Character(UnicodeScalar(alphabet[(buffer >> bits) & 0x1F])))
            }
        }
        if bits > 0 {
            output.append(Character(UnicodeScalar(alphabet[(buffer << (5 - bits)) & 0x1F])))
        }
        return output
    }
}
