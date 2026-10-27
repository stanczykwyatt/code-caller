using CodeCaller.Core;

namespace CodeCaller.Core.Tests;

// These mirror Tests/CodeCallerCoreTests/CodeExtractorTests.swift case for case, so
// both apps are held to the same behavior.

public class FindingCodes
{
    [Fact]
    public void CodeAfterItsLabel()
    {
        var code = CodeExtractor.Code(
            subject: "Your verification code",
            body: "Your verification code is 482913. It expires in 10 minutes.");
        Assert.Equal("482913", code);
    }

    [Fact]
    public void CodeAtTheStartOfTheSubject()
    {
        var code = CodeExtractor.Code(
            subject: "482913 is your Instagram code",
            body: "Someone tried to log in to your account. If this was you, enter the code above.");
        Assert.Equal("482913", code);
    }

    [Fact]
    public void CodeOnItsOwnLine()
    {
        var code = CodeExtractor.Code(
            subject: "[GitHub] Please verify your device",
            body: """
            Hey there!

            A sign in attempt requires further verification because we did not recognize your device.
            To complete the sign in, enter the verification code on the unrecognized device.

            Device: Chrome on macOS
            Verification code: 48291370

            If you did not attempt to sign in to your account, your password may be compromised.
            """);
        Assert.Equal("48291370", code);
    }

    [Fact]
    public void CodeOnItsOwnLineWithWindowsLineBreaks()
    {
        var code = CodeExtractor.Code(
            subject: "Your Steam account: Access from new computer",
            body: "Here is the Steam Guard code you need to login to account wyatt123:\r\n\r\nF4K9P\r\n\r\nThis email was generated because of a login attempt from a new computer.");
        Assert.Equal("F4K9P", code);
    }

    [Fact]
    public void LetterPrefixIsDropped()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "", body: "G-482913 is your Google verification code."));
    }

    [Fact]
    public void GroupedDigitsAreJoined()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "Sign in to Example", body: "Your sign-in code: 482 913"));
        Assert.Equal("48291370", CodeExtractor.Code(subject: "Sign in to Example", body: "Your sign-in code: 4829-1370"));
    }

    [Fact]
    public void FourDigitCode()
    {
        Assert.Equal("4829", CodeExtractor.Code(subject: "Your login code", body: "Use 4829 to log in."));
    }

    [Fact]
    public void FourDigitCodeThatLooksLikeAYear()
    {
        Assert.Equal("2041", CodeExtractor.Code(subject: "Your login code", body: "Your login code is 2041."));
    }

    [Fact]
    public void LettersAndDigits()
    {
        var code = CodeExtractor.Code(
            subject: "Your Steam account: Access from new computer",
            body: """
            Here is the Steam Guard code you need to login to account wyatt123:

            F4K9P

            This email was generated because of a login attempt from a new computer.
            """);
        Assert.Equal("F4K9P", code);
    }

    [Fact]
    public void HyphenatedLetters()
    {
        var code = CodeExtractor.Code(
            subject: "Slack confirmation code: XKF-7PQ",
            body: "Your confirmation code is below. Enter it in your open browser window.\n\nXKF-7PQ");
        Assert.Equal("XKF-7PQ", code);
    }

    [Fact]
    public void PrefersTheCodeOverOtherNumbers()
    {
        var code = CodeExtractor.Code(
            subject: "Verify your sign-in",
            body: "Request 7731 from Chicago.\nYour sign-in code is 482913.\nReference number 99887766.");
        Assert.Equal("482913", code);
    }

    [Fact]
    public void FullWidthDigits()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "認証コード", body: "認証コード：４８２９１３"));
    }
}

public class OtherLanguages
{
    [Fact]
    public void Spanish()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "Código de verificación", body: "Tu código de verificación es 482913."));
    }

    [Fact]
    public void French()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "Connexion", body: "Votre code de vérification est 482913."));
    }

    [Fact]
    public void German()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "Ihr Sicherheitscode", body: "Ihr Bestätigungscode lautet 482913."));
    }

    [Fact]
    public void SimplifiedChinese()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "验证码", body: "您的验证码是：482913，10分钟内有效。"));
    }

    [Fact]
    public void Korean()
    {
        Assert.Equal("482913", CodeExtractor.Code(subject: "[Web발신]", body: "인증번호 [482913]를 입력해주세요."));
    }
}

public class IgnoringNonCodes
{
    [Fact]
    public void OrderConfirmation()
    {
        var code = CodeExtractor.Code(
            subject: "Your order has been confirmed",
            body: "Thanks for shopping with us!\nOrder number: 4829173\nLog in to track your order.\nTotal: 1234");
        Assert.Null(code);
    }

    [Fact]
    public void PromoCode()
    {
        Assert.Null(CodeExtractor.Code(subject: "Big sale this weekend", body: "Use code SAVE20 at checkout for 20% off."));
    }

    [Fact]
    public void PhoneNumbers()
    {
        var code = CodeExtractor.Code(
            subject: "Verify your account",
            body: "Click the button below to verify your email. Questions? Call 1-800-555-1234 or (555) 123-4567.");
        Assert.Null(code);
    }

    [Fact]
    public void DatesAndYears()
    {
        var code = CodeExtractor.Code(
            subject: "New sign-in to your account",
            body: "We noticed a new sign-in on Oct 27, 2026 from Chrome on macOS.\n\n© 2026 Example Inc.");
        Assert.Null(code);
    }

    [Fact]
    public void LinksAndAddresses()
    {
        var code = CodeExtractor.Code(
            subject: "Verify your email",
            body: "Verify your email: https://example.com/verify?token=482913\nSent to user482913@example.com");
        Assert.Null(code);
    }

    [Fact]
    public void PricesAndPercentages()
    {
        var code = CodeExtractor.Code(
            subject: "Your verification receipt",
            body: "Verification fee: $1999 charged to your card ending in 4242. Save 1500% today.");
        Assert.Null(code);
    }

    [Fact]
    public void MessageWithoutVerificationWords()
    {
        Assert.Null(CodeExtractor.Code(subject: "Weekly update", body: "We shipped 482913 lines of code this year."));
    }
}

// Mirrors the email cases of the "Fixes from review" suite in the macOS tests.
public class ReviewFixes
{
    [Theory]
    [InlineData("Your login code", "Your login code is 482913. Shop now with the new app!")]
    [InlineData("Verify your email", "Your verification code is 482913 \u2014 use it at checkout to finish signing up.")]
    [InlineData("Sign in to Example", "Enter 482913 to sign in.\nDeals picked for you")]
    public void NumberCodesNearShoppingWordsInEmails(string subject, string body)
    {
        Assert.Equal("482913", CodeExtractor.Code(subject, body));
    }

    [Theory]
    [InlineData("Verify your address", "What's the zip code? It's 94110")]
    [InlineData("Your account is verified", "Use promo code 2041 for free shipping.")]
    [InlineData("Confirm your sign-in", "We noticed a sign-in near postal code 10115.")]
    public void CodePhrasesAreNotCodeWords(string subject, string body)
    {
        Assert.Null(CodeExtractor.Code(subject, body));
    }

    [Fact]
    public void RealCodeStillWinsNextToAPromoCode()
    {
        Assert.Equal("482913", CodeExtractor.Code(
            subject: "Verify your email",
            body: "Your verification code is 482913. Use promo code SAVE20 at checkout."));
    }
}

public class ExtractorDetails
{
    [Fact]
    public void LineRangeMatchesNSString()
    {
        const string text = "one\r\ntwo 42\nthree";
        var range = new CodeExtractor.Span(text.IndexOf("42", StringComparison.Ordinal), 2);
        var line = CodeExtractor.LineRange(range, text);
        Assert.Equal("two 42\n", text.Substring(line.Location, line.Length));

        var first = CodeExtractor.LineRange(new CodeExtractor.Span(0, 3), text);
        Assert.Equal("one\r\n", text.Substring(first.Location, first.Length));

        var last = CodeExtractor.LineRange(new CodeExtractor.Span(text.Length - 5, 5), text);
        Assert.Equal("three", text.Substring(last.Location, last.Length));
    }

    [Fact]
    public void PrefixCountsCharactersNotCodeUnits()
    {
        var flags = string.Concat(Enumerable.Repeat("🇺🇸", 3));
        Assert.Equal("🇺🇸🇺🇸", CodeExtractor.Prefix(flags, 2));
        Assert.Equal("abc", CodeExtractor.Prefix("abc", 20_000));
    }

    [Fact]
    public void NormalizeFoldsWidthAndRemovesLinks()
    {
        Assert.Equal("AB12 x", CodeExtractor.Normalize("ＡＢ１２\u00A0x"));
        Assert.Equal("see   now", CodeExtractor.Normalize("see https://example.com/482913 now"));
        Assert.DoesNotContain("482913", CodeExtractor.Normalize("mail me at ab482913@mail.example.com"));
    }

    [Fact]
    public void MessageTooLongIsCutOff()
    {
        var filler = new string('x', 20_100);
        Assert.Null(CodeExtractor.Code(subject: "Sign in", body: filler + "\nYour sign-in code is 482913."));
    }
}
