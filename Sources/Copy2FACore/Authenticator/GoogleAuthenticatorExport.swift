import Foundation

/// Reads Google Authenticator's "Transfer accounts" QR codes, which hold an
/// `otpauth-migration://offline?data=...` link with the accounts encoded as protobuf.
public enum GoogleAuthenticatorExport {
    /// The accounts in one link, plus how many were skipped because they aren't time-based.
    public static func accounts(from link: String) -> (accounts: [OTPAccount], skipped: Int)? {
        guard let components = URLComponents(string: link.trimmingCharacters(in: .whitespacesAndNewlines)),
              components.scheme?.lowercased() == "otpauth-migration",
              let encoded = components.queryItems?.first(where: { $0.name == "data" })?.value,
              let payload = base64Data(encoded) else { return nil }

        var reader = ProtobufReader(payload)
        var accounts: [OTPAccount] = []
        var skipped = 0
        while let field = reader.nextField() {
            if field.number == 1, field.wireType == 2, let bytes = reader.lengthDelimited() {
                if let account = account(from: bytes) {
                    accounts.append(account)
                } else {
                    skipped += 1
                }
            } else if !reader.skip(wireType: field.wireType) {
                return nil
            }
        }
        return (accounts, skipped)
    }

    private static func account(from bytes: [UInt8]) -> OTPAccount? {
        var reader = ProtobufReader(bytes)
        var secret = Data()
        var name = ""
        var issuer = ""
        var algorithm = OTPAlgorithm.sha1
        var digits = 6
        var kind = OTPKind.totp
        var counter: UInt64 = 0

        while let field = reader.nextField() {
            switch (field.number, field.wireType) {
            case (1, 2): secret = Data(reader.lengthDelimited() ?? [])
            case (2, 2): name = String(decoding: reader.lengthDelimited() ?? [], as: UTF8.self)
            case (3, 2): issuer = String(decoding: reader.lengthDelimited() ?? [], as: UTF8.self)
            case (4, 0):
                switch reader.varint() {
                case 0, 1: algorithm = .sha1
                case 2: algorithm = .sha256
                case 3: algorithm = .sha512
                default: return nil  // MD5 isn't supported by anything else either.
                }
            case (5, 0): digits = reader.varint() == 2 ? 8 : 6
            case (6, 0): kind = reader.varint() == 1 ? .hotp : .totp
            case (7, 0): counter = reader.varint() ?? 0
            default:
                guard reader.skip(wireType: field.wireType) else { return nil }
            }
        }

        // Names often repeat the issuer, as in "GitHub:wyatt".
        if let colon = name.firstIndex(of: ":") {
            let prefix = String(name[..<colon])
            if issuer.isEmpty || prefix == issuer {
                issuer = prefix
                name = String(name[name.index(after: colon)...])
            }
        }
        let account = OTPAccount(issuer: issuer, name: name, secret: secret, algorithm: algorithm, digits: digits, kind: kind, counter: counter)
        return account.isValid ? account : nil
    }

    private static func base64Data(_ text: String) -> Data? {
        var base64 = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        base64 = base64.replacingOccurrences(of: " ", with: "+")
        while base64.count % 4 != 0 { base64.append("=") }
        return Data(base64Encoded: base64)
    }
}

/// Just enough of the protobuf wire format to read Google Authenticator exports.
struct ProtobufReader {
    private let bytes: [UInt8]
    private var index = 0

    init(_ bytes: [UInt8]) { self.bytes = bytes }
    init(_ data: Data) { self.bytes = [UInt8](data) }

    mutating func nextField() -> (number: Int, wireType: Int)? {
        guard index < bytes.count, let key = varint() else { return nil }
        return (Int(key >> 3), Int(key & 0x07))
    }

    mutating func varint() -> UInt64? {
        var result: UInt64 = 0
        var shift: UInt64 = 0
        while index < bytes.count, shift < 64 {
            let byte = bytes[index]
            index += 1
            result |= UInt64(byte & 0x7F) << shift
            if byte & 0x80 == 0 { return result }
            shift += 7
        }
        return nil
    }

    mutating func lengthDelimited() -> [UInt8]? {
        guard let length = varint(), length <= UInt64(bytes.count - index) else { return nil }
        let end = index + Int(length)
        defer { index = end }
        return Array(bytes[index..<end])
    }

    /// Skips a field this reader doesn't use. Returns false on damaged data.
    mutating func skip(wireType: Int) -> Bool {
        switch wireType {
        case 0: return varint() != nil
        case 1: return advance(8)
        case 2: return lengthDelimited() != nil
        case 5: return advance(4)
        default: return false
        }
    }

    private mutating func advance(_ count: Int) -> Bool {
        guard index + count <= bytes.count else { return false }
        index += count
        return true
    }
}
