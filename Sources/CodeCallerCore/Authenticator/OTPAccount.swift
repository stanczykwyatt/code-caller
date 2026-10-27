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

/// A time-based one-time password account, the kind authenticator apps hold.
public struct OTPAccount: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var issuer: String
    public var name: String
    public var secret: Data
    public var algorithm: OTPAlgorithm
    public var digits: Int
    public var period: Int

    public init(
        id: UUID = UUID(), issuer: String, name: String, secret: Data,
        algorithm: OTPAlgorithm = .sha1, digits: Int = 6, period: Int = 30
    ) {
        self.id = id
        self.issuer = issuer.trimmingCharacters(in: .whitespaces)
        self.name = name.trimmingCharacters(in: .whitespaces)
        self.secret = secret
        self.algorithm = algorithm
        self.digits = digits
        self.period = period
    }

    /// Whether the settings are ones every authenticator supports.
    public var isValid: Bool {
        !secret.isEmpty && (6...8).contains(digits) && (1...300).contains(period)
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

    /// The code that's valid at `date`, per RFC 6238.
    public func code(at date: Date = .now) -> String {
        let counter = UInt64(max(0, date.timeIntervalSince1970) / Double(period))
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
        let modulus = (0..<digits).reduce(UInt32(1)) { result, _ in result &* 10 }
        let value = digits >= 10 ? truncated : truncated % modulus
        let text = String(value)
        return String(repeating: "0", count: max(0, digits - text.count)) + text
    }

    /// Seconds until the current code changes.
    public func secondsRemaining(at date: Date = .now) -> Int {
        period - Int(max(0, date.timeIntervalSince1970)) % period
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
