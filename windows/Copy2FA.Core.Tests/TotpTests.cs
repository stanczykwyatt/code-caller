using System.Text;
using Copy2FA.Core.Authenticator;

namespace Copy2FA.Core.Tests;

public class TotpTests
{
    static readonly byte[] Sha1Seed = Encoding.ASCII.GetBytes("12345678901234567890");
    static readonly byte[] Sha256Seed = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    static readonly byte[] Sha512Seed = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    /// <summary>RFC 6238, Appendix B.</summary>
    [Theory]
    [InlineData(59L, "94287082", OtpAlgorithm.Sha1)]
    [InlineData(59L, "46119246", OtpAlgorithm.Sha256)]
    [InlineData(59L, "90693936", OtpAlgorithm.Sha512)]
    [InlineData(1111111109L, "07081804", OtpAlgorithm.Sha1)]
    [InlineData(1111111109L, "68084774", OtpAlgorithm.Sha256)]
    [InlineData(1111111109L, "25091201", OtpAlgorithm.Sha512)]
    [InlineData(1111111111L, "14050471", OtpAlgorithm.Sha1)]
    [InlineData(1111111111L, "67062674", OtpAlgorithm.Sha256)]
    [InlineData(1111111111L, "99943326", OtpAlgorithm.Sha512)]
    [InlineData(1234567890L, "89005924", OtpAlgorithm.Sha1)]
    [InlineData(1234567890L, "91819424", OtpAlgorithm.Sha256)]
    [InlineData(1234567890L, "93441116", OtpAlgorithm.Sha512)]
    [InlineData(2000000000L, "69279037", OtpAlgorithm.Sha1)]
    [InlineData(2000000000L, "90698825", OtpAlgorithm.Sha256)]
    [InlineData(2000000000L, "38618901", OtpAlgorithm.Sha512)]
    [InlineData(20000000000L, "65353130", OtpAlgorithm.Sha1)]
    [InlineData(20000000000L, "77737706", OtpAlgorithm.Sha256)]
    [InlineData(20000000000L, "47863826", OtpAlgorithm.Sha512)]
    public void Rfc6238TestVectors(long unixSeconds, string expected, OtpAlgorithm algorithm)
    {
        var seed = algorithm switch
        {
            OtpAlgorithm.Sha256 => Sha256Seed,
            OtpAlgorithm.Sha512 => Sha512Seed,
            _ => Sha1Seed,
        };
        var time = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        Assert.Equal(expected, Totp.Code(seed, time, digits: 8, period: 30, algorithm));
    }

    /// <summary>RFC 4226, Appendix D. A 30-second TOTP at time 30 × n is HOTP with counter n.</summary>
    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void Rfc4226TestVectors(int counter, string expected)
    {
        Assert.Equal(expected, Totp.Code(Sha1Seed, DateTimeOffset.FromUnixTimeSeconds(counter * 30L)));
    }

    [Fact]
    public void SevenDigitsAndOtherPeriods()
    {
        // Both land on counter 18518518: 1111111109 / 60 and 555555540 / 30.
        var sixty = Totp.Code(Sha1Seed, DateTimeOffset.FromUnixTimeSeconds(1111111109), digits: 8, period: 60);
        var thirty = Totp.Code(Sha1Seed, DateTimeOffset.FromUnixTimeSeconds(18518518L * 30), digits: 8, period: 30);
        Assert.Equal(thirty, sixty);
        Assert.Equal("7081804", Totp.Code(Sha1Seed, DateTimeOffset.FromUnixTimeSeconds(1111111109), digits: 7));
    }

    [Fact]
    public void SecondsRemaining()
    {
        Assert.Equal(30, Totp.SecondsRemaining(30, DateTimeOffset.FromUnixTimeSeconds(60)));
        Assert.Equal(1, Totp.SecondsRemaining(30, DateTimeOffset.FromUnixTimeSeconds(89)));
        Assert.Equal(45, Totp.SecondsRemaining(60, DateTimeOffset.FromUnixTimeSeconds(75)));
    }

    [Fact]
    public void RejectsDigitCountsOutsideSixToEight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Totp.Code(Sha1Seed, DateTimeOffset.UnixEpoch, digits: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Totp.Code(Sha1Seed, DateTimeOffset.UnixEpoch, digits: 9));
    }
}

public class Base32Tests
{
    /// <summary>RFC 4648, section 10.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foob", "MZXW6YQ=")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Rfc4648TestVectors(string text, string encoded)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        Assert.Equal(encoded.TrimEnd('='), Base32.Encode(bytes));
        Assert.Equal(bytes, Base32.Decode(encoded));
        Assert.Equal(bytes, Base32.Decode(encoded.TrimEnd('=')));
    }

    [Fact]
    public void IgnoresCaseSpacesAndHyphens()
    {
        var expected = Base32.Decode("JBSWY3DPEHPK3PXP");
        Assert.Equal(expected, Base32.Decode("jbsw y3dp-ehpk 3pxp"));
        Assert.Equal("Hello!"u8.ToArray(), expected![..6]);
    }

    [Theory]
    [InlineData("JBSWY3DPEHPK3PX1")]
    [InlineData("JBSWY3DP=EHPK3PXP")]
    [InlineData("JBSW Y3DP É")]
    public void RejectsCharactersOutsideTheAlphabet(string text)
    {
        Assert.Null(Base32.Decode(text));
    }
}

public class OtpAuthUriTests
{
    [Fact]
    public void ReadsTheExampleFromTheKeyUriFormat()
    {
        var account = OtpAuthUri.Parse(
            "otpauth://totp/ACME%20Co:john.doe@email.com?secret=HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ&issuer=ACME%20Co&algorithm=SHA1&digits=6&period=30");
        Assert.Equal("ACME Co", account.Issuer);
        Assert.Equal("john.doe@email.com", account.Name);
        Assert.Equal(OtpAlgorithm.Sha1, account.Algorithm);
        Assert.Equal(6, account.Digits);
        Assert.Equal(30, account.Period);
        Assert.Equal("HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ", Base32.Encode(account.Secret));
    }

    [Fact]
    public void ReadsOtherSettings()
    {
        var account = OtpAuthUri.Parse("OTPAUTH://TOTP/Example:alice%40example.com?secret=jbswy3dpehpk3pxp&algorithm=sha512&digits=8&period=60");
        Assert.Equal("Example", account.Issuer);
        Assert.Equal("alice@example.com", account.Name);
        Assert.Equal(OtpAlgorithm.Sha512, account.Algorithm);
        Assert.Equal(8, account.Digits);
        Assert.Equal(60, account.Period);
    }

    [Fact]
    public void IssuerParameterWinsOverTheLabel()
    {
        var account = OtpAuthUri.Parse("otpauth://totp/Old%20Name:bob?secret=JBSWY3DPEHPK3PXP&issuer=New+Name");
        Assert.Equal("New Name", account.Issuer);
        Assert.Equal("bob", account.Name);
    }

    [Fact]
    public void LabelWithoutAnIssuer()
    {
        var account = OtpAuthUri.Parse("otpauth://totp/alice+work@example.com?secret=JBSWY3DPEHPK3PXP");
        Assert.Equal("", account.Issuer);
        Assert.Equal("alice+work@example.com", account.Name);
        Assert.Equal("alice+work@example.com", account.Title);
    }

    [Theory]
    [InlineData("https://example.com/?secret=JBSWY3DPEHPK3PXP")]
    [InlineData("otpauth://hotp/Example?secret=JBSWY3DPEHPK3PXP&counter=0")]
    [InlineData("otpauth://totp/Example")]
    [InlineData("otpauth://totp/Example?secret=not-base32!")]
    [InlineData("otpauth://totp/Example?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")]
    [InlineData("otpauth://totp/Example?secret=JBSWY3DPEHPK3PXP&digits=5")]
    [InlineData("otpauth://totp/Example?secret=JBSWY3DPEHPK3PXP&digits=10")]
    [InlineData("otpauth://totp/Example?secret=JBSWY3DPEHPK3PXP&period=0")]
    [InlineData("otpauth://totp/Example?secret=JBSWY3DPEHPK3PXP&period=-30")]
    public void RejectsLinksThatCantMakeCodes(string link)
    {
        Assert.Throws<ImportException>(() => OtpAuthUri.Parse(link));
    }

    [Fact]
    public void BuildsLinksThatReadBack()
    {
        var account = OtpAccount.Create("ACME Co", "john@example.com", Base32.Decode("JBSWY3DPEHPK3PXP"), OtpAlgorithm.Sha256, 8, 45)!;
        var copy = OtpAuthUri.Parse(OtpAuthUri.Build(account));
        Assert.True(copy.IsSameAs(account));
    }
}

public class CodeFormatTests
{
    [Theory]
    [InlineData("482913", "482 913")]
    [InlineData("4829135", "482 9135")]
    [InlineData("48291370", "4829 1370")]
    [InlineData("4829", "4829")]
    public void GroupsCodesLikeTheMacPicker(string code, string expected)
    {
        Assert.Equal(expected, CodeFormat.Grouped(code));
    }
}
