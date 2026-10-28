import Foundation

/// Reads the text out of a message's `attributedBody`, which Messages stores as an
/// NSAttributedString archived in the old "typedstream" format.
///
/// Only the string's bytes are read. Nothing is unarchived, so a crafted message
/// can't make the app create objects.
public enum TypedStreamText {
    public static func string(from data: Data) -> String? {
        let bytes = [UInt8](data)
        guard let className = bytes.firstRange(of: Array("NSString".utf8)) else { return nil }

        // The string's bytes follow a "+" marker a few bytes after the class name.
        let searchEnd = min(className.upperBound + 16, bytes.count)
        guard let marker = bytes[className.upperBound..<searchEnd].firstIndex(of: UInt8(ascii: "+")) else {
            return nil
        }
        var index = marker + 1
        guard index < bytes.count else { return nil }

        let length: Int
        switch bytes[index] {
        case 0x81:
            guard index + 2 < bytes.count else { return nil }
            length = Int(bytes[index + 1]) | Int(bytes[index + 2]) << 8
            index += 3
        case 0x82:
            guard index + 4 < bytes.count else { return nil }
            length = (0..<4).reduce(0) { $0 | Int(bytes[index + 1 + $1]) << (8 * $1) }
            index += 5
        default:
            length = Int(bytes[index])
            index += 1
        }
        guard length > 0, index + length <= bytes.count else { return nil }
        return String(decoding: bytes[index..<index + length], as: UTF8.self)
    }
}
