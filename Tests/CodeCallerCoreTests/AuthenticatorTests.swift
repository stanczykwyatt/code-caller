import CoreImage
import Foundation
import ImageIO
import Testing
import UniformTypeIdentifiers
@testable import CodeCallerCore

@Suite("Authenticator codes")
struct AuthenticatorCodes {
    static let sha1Secret = Data("12345678901234567890".utf8)
    static let sha256Secret = Data("12345678901234567890123456789012".utf8)
    static let sha512Secret = Data("1234567890123456789012345678901234567890123456789012345678901234".utf8)

    /// The test vectors from RFC 6238, appendix B.
    @Test(arguments: [
        (59.0, "94287082", "46119246", "90693936"),
        (1_111_111_109, "07081804", "68084774", "25091201"),
        (1_111_111_111, "14050471", "67062674", "99943326"),
        (1_234_567_890, "89005924", "91819424", "93441116"),
        (2_000_000_000, "69279037", "90698825", "38618901"),
        (20_000_000_000, "65353130", "77737706", "47863826"),
    ])
    func rfc6238(time: Double, sha1: String, sha256: String, sha512: String) {
        let date = Date(timeIntervalSince1970: time)
        #expect(OTPAccount(issuer: "", name: "", secret: Self.sha1Secret, algorithm: .sha1, digits: 8).code(at: date) == sha1)
        #expect(OTPAccount(issuer: "", name: "", secret: Self.sha256Secret, algorithm: .sha256, digits: 8).code(at: date) == sha256)
        #expect(OTPAccount(issuer: "", name: "", secret: Self.sha512Secret, algorithm: .sha512, digits: 8).code(at: date) == sha512)
    }

    @Test func sixDigitsKeepsLeadingZeros() {
        let account = OTPAccount(issuer: "", name: "", secret: Self.sha1Secret)
        #expect(account.code(at: Date(timeIntervalSince1970: 1_111_111_109)) == "081804")
    }

    @Test func secondsRemaining() {
        let account = OTPAccount(issuer: "", name: "", secret: Self.sha1Secret)
        #expect(account.secondsRemaining(at: Date(timeIntervalSince1970: 60)) == 30)
        #expect(account.secondsRemaining(at: Date(timeIntervalSince1970: 89)) == 1)
    }

    @Test func base32() {
        let hello = Data("Hello!".utf8) + Data([0xDE, 0xAD, 0xBE, 0xEF])
        #expect(Base32.decode("JBSWY3DPEHPK3PXP") == hello)
        #expect(Base32.decode("jbsw y3dp-ehpk 3pxp") == hello)
        #expect(Base32.encode(hello) == "JBSWY3DPEHPK3PXP")
        #expect(Base32.decode("not base32!") == nil)
        #expect(Base32.decode("") == nil)
    }

    @Test func displayNames() {
        #expect(OTPAccount(issuer: "GitHub", name: "wyatt", secret: Self.sha1Secret).displayName == "GitHub (wyatt)")
        #expect(OTPAccount(issuer: "GitHub", name: "", secret: Self.sha1Secret).displayName == "GitHub")
        #expect(OTPAccount(issuer: "", name: "wyatt", secret: Self.sha1Secret).displayName == "wyatt")
    }
}

@Suite("otpauth links")
struct OTPAuthLinks {
    @Test func fullLink() throws {
        let account = try #require(OTPAuthURL.account(from: "otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&algorithm=SHA256&digits=8&period=60"))
        #expect(account.issuer == "GitHub")
        #expect(account.name == "wyatt")
        #expect(account.algorithm == .sha256)
        #expect(account.digits == 8)
        #expect(account.period == 60)
    }

    @Test func issuerFromLabelAndEncodedCharacters() throws {
        let account = try #require(OTPAuthURL.account(from: "otpauth://totp/Big%20Bank%3Awyatt%40example.com?secret=JBSWY3DPEHPK3PXP"))
        #expect(account.issuer == "Big Bank")
        #expect(account.name == "wyatt@example.com")
        #expect(account.algorithm == .sha1)
        #expect(account.digits == 6)
        #expect(account.period == 30)
    }

    @Test(arguments: [
        "otpauth://totp/GitHub?issuer=GitHub",
        "otpauth://totp/GitHub?secret=JBSWY3DPEHPK3PXP&algorithm=MD5",
        "otpauth://totp/GitHub?secret=JBSWY3DPEHPK3PXP&digits=12",
        "https://example.com/?secret=JBSWY3DPEHPK3PXP",
    ])
    func rejectsUnusableLinks(link: String) {
        #expect(OTPAuthURL.account(from: link) == nil)
    }
}

@Suite("Importing accounts")
struct ImportingAccounts {
    @Test func googleAuthenticatorTransferCode() throws {
        let link = "otpauth-migration://offline?data=CjEKCkhlbGxvId6tvu8SGEV4YW1wbGU6YWxpY2VAZ29vZ2xlLmNvbRoHRXhhbXBsZTAC"
        let result = try AccountImporter.accounts(fromText: link)
        #expect(result.skipped == 0)
        let account = try #require(result.accounts.first)
        #expect(account.issuer == "Example")
        #expect(account.name == "alice@google.com")
        #expect(account.secret == Base32.decode("JBSWY3DPEHPK3PXP"))
    }

    @Test func googleExportIncludesCounterAccounts() throws {
        func field(_ number: Int, _ bytes: [UInt8]) -> [UInt8] { [UInt8(number << 3 | 2), UInt8(bytes.count)] + bytes }
        func varint(_ number: Int, _ value: UInt8) -> [UInt8] { [UInt8(number << 3), value] }
        let secret = [UInt8](Base32.decode("JBSWY3DPEHPK3PXP")!)
        let totp = field(1, secret) + field(2, Array("wyatt".utf8)) + field(3, Array("GitHub".utf8)) + varint(4, 2) + varint(5, 2) + varint(6, 2)
        let hotp = field(1, secret) + field(2, Array("bank".utf8)) + varint(6, 1) + varint(7, 5)
        let payload = field(1, totp) + field(1, hotp) + varint(2, 1)
        let link = "otpauth-migration://offline?data=" + Data(payload).base64EncodedString()
            .addingPercentEncoding(withAllowedCharacters: .alphanumerics)!

        let result = try AccountImporter.accounts(fromText: link)
        #expect(result.accounts.count == 2)
        #expect(result.skipped == 0)
        #expect(result.accounts.last?.kind == .hotp)
        #expect(result.accounts.last?.counter == 5)
        #expect(result.accounts.first?.kind == .totp)
        #expect(result.accounts.first?.algorithm == .sha256)
        #expect(result.accounts.first?.digits == 8)
        #expect(result.accounts.first?.displayName == "GitHub (wyatt)")
    }

    @Test func applePasswordsCSV() throws {
        let csv = """
            Title,URL,Username,Password,Notes,OTPAuth
            GitHub,https://github.com/,wyatt,"hunter2, with a comma",,otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP&issuer=GitHub
            Bank,https://www.bank.example/,wyatt,secret,"Notes with ""quotes""
            and a line break",
            Old site,https://old.example/,wyatt,pw,,otpauth://hotp/Old?secret=JBSWY3DPEHPK3PXP
            """
        let result = try AccountImporter.accounts(fromText: csv)
        #expect(result.accounts.map(\.displayName) == ["GitHub (wyatt)", "Old site (Old)"])
        #expect(result.accounts.map(\.kind) == [.totp, .hotp])
        #expect(result.skipped == 0)
    }

    @Test func bitwardenCSVWithBareSecret() throws {
        let csv = """
            folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp
            ,,login,Dropbox,,,0,https://www.dropbox.com,wyatt@example.com,pw,JBSWY3DPEHPK3PXP
            """
        let result = try AccountImporter.accounts(fromText: csv)
        #expect(result.accounts.map(\.displayName) == ["Dropbox (wyatt@example.com)"])
    }

    @Test func bitwardenJSON() throws {
        let json = """
            {"encrypted": false, "items": [
              {"name": "GitHub", "login": {"username": "wyatt", "totp": "otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP"}},
              {"name": "Dropbox", "login": {"username": "wyatt@example.com", "totp": "JBSW Y3DP EHPK 3PXP"}},
              {"name": "Steam", "login": {"username": "wyatt", "totp": "steam://JBSWY3DPEHPK3PXP"}},
              {"name": "No 2FA", "login": {"username": "wyatt", "totp": null}}
            ]}
            """
        let result = try AccountImporter.accounts(fromText: json)
        #expect(result.accounts.map(\.displayName) == ["GitHub (wyatt)", "Dropbox (wyatt@example.com)"])
        #expect(result.skipped == 1)
    }

    @Test func aegisJSON() throws {
        let json = """
            {"version": 1, "header": {"slots": null, "params": null}, "db": {"version": 2, "entries": [
              {"type": "totp", "name": "wyatt", "issuer": "GitHub", "info": {"secret": "JBSWY3DPEHPK3PXP", "algo": "SHA512", "digits": 7, "period": 30}},
              {"type": "hotp", "name": "bank", "issuer": "Bank", "info": {"secret": "JBSWY3DPEHPK3PXP", "algo": "SHA1", "digits": 6, "counter": 3}}
            ]}}
            """
        let result = try AccountImporter.accounts(fromText: json)
        #expect(result.accounts.count == 2)
        #expect(result.accounts.first?.algorithm == .sha512)
        #expect(result.accounts.first?.digits == 7)
        #expect(result.accounts.last?.kind == .hotp)
        #expect(result.accounts.last?.counter == 3)
        #expect(result.skipped == 0)
    }

    @Test func twoFASJSON() throws {
        let json = """
            {"schemaVersion": 4, "services": [
              {"name": "GitHub", "secret": "JBSWY3DPEHPK3PXP", "otp": {"account": "wyatt", "digits": 6, "period": 30, "algorithm": "SHA1", "tokenType": "TOTP"}},
              {"name": "Steam", "secret": "JBSWY3DPEHPK3PXP", "otp": {"tokenType": "STEAM"}}
            ]}
            """
        let result = try AccountImporter.accounts(fromText: json)
        #expect(result.accounts.map(\.displayName) == ["GitHub (wyatt)"])
        #expect(result.skipped == 1)
    }

    @Test func andOTPJSON() throws {
        let json = """
            [{"secret": "JBSWY3DPEHPK3PXP", "issuer": "", "label": "GitHub:wyatt", "digits": 6, "type": "TOTP", "algorithm": "SHA1", "period": 30}]
            """
        let result = try AccountImporter.accounts(fromText: json)
        #expect(result.accounts.map(\.displayName) == ["GitHub (wyatt)"])
    }

    @Test(arguments: [
        #"{"encrypted": true, "encKeyValidation_DO_NOT_EDIT": "abc", "data": "xyz"}"#,
        #"{"version": 1, "header": {"slots": []}, "db": "c2VjcmV0"}"#,
        #"{"schemaVersion": 4, "services": [], "servicesEncrypted": "abc"}"#,
    ])
    func encryptedExports(json: String) {
        #expect(throws: ImportError.encrypted) { try AccountImporter.accounts(fromText: json) }
    }

    @Test func listOfLinks() throws {
        let text = """
            otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP&issuer=GitHub
            otpauth://totp/Dropbox:wyatt?secret=JBSWY3DPEHPK3PXQ&issuer=Dropbox
            """
        #expect(try AccountImporter.accounts(fromText: text).accounts.count == 2)
    }

    @Test func unrecognizedText() {
        #expect(throws: ImportError.unrecognized) { try AccountImporter.accounts(fromText: "just some notes") }
    }

    @Test func qrCodeImage() throws {
        let link = "otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP&issuer=GitHub"
        let result = try AccountImporter.accounts(from: try Self.qrCodePNG(link))
        #expect(result.accounts.map(\.displayName) == ["GitHub (wyatt)"])
    }

    @Test func imageWithoutQRCode() throws {
        #expect(throws: ImportError.noQRCode) { try AccountImporter.accounts(from: try Self.blankPNG()) }
    }

    @Test func duplicatesAreDetected() throws {
        let one = try #require(OTPAuthURL.account(from: "otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP"))
        let two = try #require(OTPAuthURL.account(from: "otpauth://totp/github:WYATT?secret=JBSWY3DPEHPK3PXP"))
        #expect(one.isSameAccount(as: two))
        #expect(one.id != two.id)
    }

    static func qrCodePNG(_ text: String) throws -> Data {
        let filter = try #require(CIFilter(name: "CIQRCodeGenerator", parameters: ["inputMessage": Data(text.utf8)]))
        let image = try #require(filter.outputImage).transformed(by: CGAffineTransform(scaleX: 8, y: 8))
        return try png(image)
    }

    static func blankPNG() throws -> Data {
        try png(CIImage(color: .white).cropped(to: CGRect(x: 0, y: 0, width: 64, height: 64)))
    }

    private static func png(_ image: CIImage) throws -> Data {
        let context = CIContext()
        let cgImage = try #require(context.createCGImage(image, from: image.extent))
        let data = NSMutableData()
        let destination = try #require(CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil))
        CGImageDestinationAddImage(destination, cgImage, nil)
        try #require(CGImageDestinationFinalize(destination))
        return data as Data
    }
}

@Suite("Import fixes from review")
struct ImportReviewFixes {
    @Test(arguments: [
        ("otpauth://totp/Macy's:alice?secret=JBSWY3DPEHPK3PXP&issuer=Macy's", "Macy's (alice)"),
        ("otpauth://totp/Acme,Inc:alice?secret=JBSWY3DPEHPK3PXP&issuer=Acme,Inc", "Acme,Inc (alice)"),
        ("otpauth://totp/Acme Inc:alice?secret=JBSWY3DPEHPK3PXP", "Acme Inc (alice)"),
    ])
    func wholeLinksKeepTheirLabels(link: String, name: String) throws {
        let result = try AccountImporter.accounts(fromText: link)
        #expect(result.accounts.map(\.displayName) == [name])
        #expect(result.skipped == 0)
    }

    @Test func linkInsideOtherText() throws {
        let result = try AccountImporter.accounts(fromText: "Scan this: <otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP>")
        #expect(result.accounts.map(\.displayName) == ["GitHub (wyatt)"])
    }

    @Test func unrelatedQRCodeIsIgnored() throws {
        let image = try ImportingAccounts.twoQRCodesPNG(
            "https://apps.apple.com/app/id123",
            "otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP"
        )
        let result = try AccountImporter.accounts(from: image)
        #expect(result.accounts.map(\.displayName) == ["GitHub (wyatt)"])
    }

    @Test func junkSecretsAreSkipped() throws {
        let csv = """
            Title,URL,Username,Password,Notes,OTPAuth
            Site,https://site.example,wyatt,pw,,NONE
            """
        let result = try AccountImporter.accounts(fromText: csv)
        #expect(result.accounts.isEmpty)
        #expect(result.skipped == 1)
        #expect(OTPAccount(issuer: "", name: "", secret: Base32.decode("NONE")!).isValid == false)
    }
}

extension ImportingAccounts {
    static func twoQRCodesPNG(_ first: String, _ second: String) throws -> Data {
        func code(_ text: String) throws -> CIImage {
            let filter = try #require(CIFilter(name: "CIQRCodeGenerator", parameters: ["inputMessage": Data(text.utf8)]))
            return try #require(filter.outputImage).transformed(by: CGAffineTransform(scaleX: 8, y: 8))
        }
        let left = try code(first)
        let right = try code(second).transformed(by: CGAffineTransform(translationX: left.extent.width + 80, y: 0))
        let size = CGRect(x: -40, y: -40, width: left.extent.width + right.extent.width + 160, height: max(left.extent.height, right.extent.height) + 80)
        let image = right.composited(over: left).composited(over: CIImage(color: .white).cropped(to: size))
        return try pngData(image)
    }

    static func pngData(_ image: CIImage) throws -> Data {
        let context = CIContext()
        let cgImage = try #require(context.createCGImage(image, from: image.extent))
        let data = NSMutableData()
        let destination = try #require(CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil))
        CGImageDestinationAddImage(destination, cgImage, nil)
        try #require(CGImageDestinationFinalize(destination))
        return data as Data
    }
}

@Suite("Counter-based codes")
struct CounterBasedCodes {
    /// The test vectors from RFC 4226, appendix D.
    @Test func rfc4226() {
        let account = OTPAccount(issuer: "", name: "", secret: Data("12345678901234567890".utf8), kind: .hotp)
        let expected = ["755224", "287082", "359152", "969429", "338314", "254676", "287922", "162583", "399871", "520489"]
        #expect((0..<10).map { account.code(forCounter: UInt64($0)) } == expected)
    }

    @Test func eachCodeMovesTheCounterOn() {
        let account = OTPAccount(issuer: "", name: "", secret: Data("12345678901234567890".utf8), kind: .hotp, counter: 3)
        let first = account.usingNextCode()
        let second = first.account.usingNextCode()
        #expect(first.code == "969429")
        #expect(second.code == "338314")
        #expect(second.account.counter == 5)
    }

    @Test func link() throws {
        let account = try #require(OTPAuthURL.account(from: "otpauth://hotp/Bank:wyatt?secret=JBSWY3DPEHPK3PXP&counter=7&digits=8"))
        #expect(account.kind == .hotp)
        #expect(account.counter == 7)
        #expect(account.digits == 8)
        #expect(account.displayName == "Bank (wyatt)")
    }

    @Test func twoFASAndAndOTP() throws {
        let twoFAS = """
            {"schemaVersion": 4, "services": [{"name": "Bank", "secret": "JBSWY3DPEHPK3PXP", "otp": {"tokenType": "HOTP", "counter": 2}}]}
            """
        #expect(try AccountImporter.accounts(fromText: twoFAS).accounts.first?.counter == 2)
        let andOTP = """
            [{"secret": "JBSWY3DPEHPK3PXP", "label": "Bank:wyatt", "digits": 6, "type": "HOTP", "algorithm": "SHA1", "counter": 9}]
            """
        let account = try #require(try AccountImporter.accounts(fromText: andOTP).accounts.first)
        #expect(account.kind == .hotp)
        #expect(account.counter == 9)
    }

    /// Version 0.2.0 saved accounts without a kind or counter.
    @Test func accountsSavedBeforeCountersStillLoad() throws {
        let saved = """
            [{"id": "BFE4A033-A813-46B7-821B-71893961B7B0", "issuer": "GitHub", "name": "wyatt", "secret": "SGVsbG8h3q2+7w==",
              "algorithm": "SHA1", "digits": 6, "period": 30}]
            """
        let accounts = try JSONDecoder().decode([OTPAccount].self, from: Data(saved.utf8))
        #expect(accounts.first?.kind == .totp)
        #expect(accounts.first?.counter == 0)
        #expect(accounts.first?.secret == Base32.decode("JBSWY3DPEHPK3PXP"))
        // And they save and load again unchanged.
        let again = try JSONDecoder().decode([OTPAccount].self, from: try JSONEncoder().encode(accounts))
        #expect(again == accounts)
    }
}
