import Testing
@testable import CodeCallerCore

@Suite("Finding codes")
struct FindingCodes {
    @Test func codeAfterItsLabel() {
        let code = CodeExtractor.code(
            subject: "Your verification code",
            body: "Your verification code is 482913. It expires in 10 minutes."
        )
        #expect(code == "482913")
    }

    @Test func codeAtTheStartOfTheSubject() {
        let code = CodeExtractor.code(
            subject: "482913 is your Instagram code",
            body: "Someone tried to log in to your account. If this was you, enter the code above."
        )
        #expect(code == "482913")
    }

    @Test func codeOnItsOwnLine() {
        let code = CodeExtractor.code(
            subject: "[GitHub] Please verify your device",
            body: """
            Hey there!

            A sign in attempt requires further verification because we did not recognize your device.
            To complete the sign in, enter the verification code on the unrecognized device.

            Device: Chrome on macOS
            Verification code: 48291370

            If you did not attempt to sign in to your account, your password may be compromised.
            """
        )
        #expect(code == "48291370")
    }

    @Test func letterPrefixIsDropped() {
        #expect(CodeExtractor.code(subject: "", body: "G-482913 is your Google verification code.") == "482913")
    }

    @Test func groupedDigitsAreJoined() {
        #expect(CodeExtractor.code(subject: "Sign in to Example", body: "Your sign-in code: 482 913") == "482913")
        #expect(CodeExtractor.code(subject: "Sign in to Example", body: "Your sign-in code: 4829-1370") == "48291370")
    }

    @Test func fourDigitCode() {
        #expect(CodeExtractor.code(subject: "Your login code", body: "Use 4829 to log in.") == "4829")
    }

    @Test func fourDigitCodeThatLooksLikeAYear() {
        #expect(CodeExtractor.code(subject: "Your login code", body: "Your login code is 2041.") == "2041")
    }

    @Test func lettersAndDigits() {
        let code = CodeExtractor.code(
            subject: "Your Steam account: Access from new computer",
            body: """
            Here is the Steam Guard code you need to login to account wyatt123:

            F4K9P

            This email was generated because of a login attempt from a new computer.
            """
        )
        #expect(code == "F4K9P")
    }

    @Test func hyphenatedLetters() {
        let code = CodeExtractor.code(
            subject: "Slack confirmation code: XKF-7PQ",
            body: "Your confirmation code is below. Enter it in your open browser window.\n\nXKF-7PQ"
        )
        #expect(code == "XKF-7PQ")
    }

    @Test func prefersTheCodeOverOtherNumbers() {
        let code = CodeExtractor.code(
            subject: "Verify your sign-in",
            body: "Request 7731 from Chicago.\nYour sign-in code is 482913.\nReference number 99887766."
        )
        #expect(code == "482913")
    }

    @Test func fullWidthDigits() {
        #expect(CodeExtractor.code(subject: "認証コード", body: "認証コード：４８２９１３") == "482913")
    }
}

@Suite("Other languages")
struct OtherLanguages {
    @Test func spanish() {
        #expect(CodeExtractor.code(subject: "Código de verificación", body: "Tu código de verificación es 482913.") == "482913")
    }

    @Test func french() {
        #expect(CodeExtractor.code(subject: "Connexion", body: "Votre code de vérification est 482913.") == "482913")
    }

    @Test func german() {
        #expect(CodeExtractor.code(subject: "Ihr Sicherheitscode", body: "Ihr Bestätigungscode lautet 482913.") == "482913")
    }

    @Test func simplifiedChinese() {
        #expect(CodeExtractor.code(subject: "验证码", body: "您的验证码是：482913，10分钟内有效。") == "482913")
    }

    @Test func korean() {
        #expect(CodeExtractor.code(subject: "[Web발신]", body: "인증번호 [482913]를 입력해주세요.") == "482913")
    }
}

@Suite("Ignoring things that aren't codes")
struct IgnoringNonCodes {
    @Test func orderConfirmation() {
        let code = CodeExtractor.code(
            subject: "Your order has been confirmed",
            body: "Thanks for shopping with us!\nOrder number: 4829173\nLog in to track your order.\nTotal: 1234"
        )
        #expect(code == nil)
    }

    @Test func promoCode() {
        #expect(CodeExtractor.code(subject: "Big sale this weekend", body: "Use code SAVE20 at checkout for 20% off.") == nil)
    }

    @Test func phoneNumbers() {
        let code = CodeExtractor.code(
            subject: "Verify your account",
            body: "Click the button below to verify your email. Questions? Call 1-800-555-1234 or (555) 123-4567."
        )
        #expect(code == nil)
    }

    @Test func datesAndYears() {
        let code = CodeExtractor.code(
            subject: "New sign-in to your account",
            body: "We noticed a new sign-in on Oct 27, 2026 from Chrome on macOS.\n\n© 2026 Example Inc."
        )
        #expect(code == nil)
    }

    @Test func linksAndAddresses() {
        let code = CodeExtractor.code(
            subject: "Verify your email",
            body: "Verify your email: https://example.com/verify?token=482913\nSent to user482913@example.com"
        )
        #expect(code == nil)
    }

    @Test func pricesAndPercentages() {
        let code = CodeExtractor.code(
            subject: "Your verification receipt",
            body: "Verification fee: $1999 charged to your card ending in 4242. Save 1500% today."
        )
        #expect(code == nil)
    }

    @Test func messageWithoutVerificationWords() {
        #expect(CodeExtractor.code(subject: "Weekly update", body: "We shipped 482913 lines of code this year.") == nil)
    }
}
