import Foundation
import Testing
@testable import Copy2FACore

/// Emails, texts and import files can come from anyone, so no input may crash a parser.
@Suite("Damaged input")
struct DamagedInput {
    /// A small seeded generator, so a failure can be reproduced exactly.
    struct Generator: RandomNumberGenerator {
        var state: UInt64
        mutating func next() -> UInt64 {
            state &+= 0x9E37_79B9_7F4A_7C15
            var value = state
            value = (value ^ (value >> 30)) &* 0xBF58_476D_1CE4_E5B9
            value = (value ^ (value >> 27)) &* 0x94D0_49BB_1331_11EB
            return value ^ (value >> 31)
        }
    }

    static let pieces = [
        "code", "verification", "otpauth://totp/", "?secret=", "JBSWY3DP", "&digits=", "99999999999999999999",
        "-", ",", "\"", "\n", "\r\n", "%", "$", "#", ":", "482913", "G-", "NSString", "+", "\u{FF14}", "验证码",
        "otpauth-migration://offline?data=", "Cg", "==", "{", "}", "[", "]", "\"items\":", "\"db\":", "OTPAuth",
    ]

    static func randomText(_ generator: inout Generator) -> String {
        (0..<Int.random(in: 0...40, using: &generator)).map { _ in
            Bool.random(using: &generator)
                ? pieces.randomElement(using: &generator)!
                : String(UnicodeScalar(UInt8.random(in: 0...127, using: &generator)))
        }.joined()
    }

    static func randomBytes(_ generator: inout Generator) -> Data {
        Data((0..<Int.random(in: 0...300, using: &generator)).map { _ in UInt8.random(in: 0...255, using: &generator) })
    }

    @Test func codeDetection() {
        var generator = Generator(state: 1)
        for _ in 0..<3_000 {
            _ = CodeExtractor.code(subject: Self.randomText(&generator), body: Self.randomText(&generator))
            _ = CodeExtractor.code(inText: Self.randomText(&generator))
        }
    }

    @Test func messageBodies() {
        var generator = Generator(state: 2)
        let real = Archives.data(Archives.long)
        for _ in 0..<3_000 {
            _ = TypedStreamText.string(from: Self.randomBytes(&generator))
            // Real archives with random bytes changed, including the length bytes.
            var damaged = real
            for _ in 0..<Int.random(in: 1...6, using: &generator) {
                damaged[Int.random(in: 0..<damaged.count, using: &generator)] = UInt8.random(in: 0...255, using: &generator)
            }
            _ = TypedStreamText.string(from: damaged)
            _ = TypedStreamText.string(from: real.prefix(Int.random(in: 0..<real.count, using: &generator)))
        }
    }

    @Test func imports() {
        var generator = Generator(state: 3)
        for _ in 0..<3_000 {
            _ = try? AccountImporter.accounts(fromText: Self.randomText(&generator))
            _ = try? AccountImporter.accounts(from: Self.randomBytes(&generator))
            let payload = Self.randomBytes(&generator).base64EncodedString()
            _ = GoogleAuthenticatorExport.accounts(from: "otpauth-migration://offline?data=\(payload)")
        }
    }

    @Test func extremeAccountSettings() {
        let secret = Data([1, 2, 3])
        for digits in [0, 1, 6, 8, 9, 10, 18, -1] {
            for period in [1, 30, 300] {
                _ = OTPAccount(issuer: "", name: "", secret: secret, digits: digits, period: period).code(at: .distantFuture)
            }
        }
        #expect(OTPAccount(issuer: "", name: "", secret: Data(), digits: 6).isValid == false)
    }
}
