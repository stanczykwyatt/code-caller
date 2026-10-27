using System.Text;
using Copy2FA.Core.Authenticator;

namespace Copy2FA.Core.Tests;

public class ImporterTests
{
    static ImportResult Import(string text) => AccountImporter.Import(Encoding.UTF8.GetBytes(text));

    static void AssertNoPasswordsKept(ImportResult result, params string[] passwords)
    {
        foreach (var account in result.Accounts)
        {
            foreach (var password in passwords)
            {
                Assert.DoesNotContain(password, account.Issuer, StringComparison.Ordinal);
                Assert.DoesNotContain(password, account.Name, StringComparison.Ordinal);
            }
        }
    }

    // Lists of otpauth links

    [Fact]
    public void TextListOfLinks()
    {
        var result = Import("""
            # Accounts exported 2026-10-27
            otpauth://totp/GitHub:octocat?secret=JBSWY3DPEHPK3PXP&issuer=GitHub
            otpauth://totp/ACME%20Co:john.doe@email.com?secret=HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ&issuer=ACME%20Co&algorithm=SHA1&digits=6&period=30

            otpauth://hotp/Legacy?secret=JBSWY3DPEHPK3PXP&counter=1
            """);
        Assert.Equal(["GitHub", "ACME Co", ""], result.Accounts.Select(a => a.Issuer));
        Assert.Equal(["octocat", "john.doe@email.com", "Legacy"], result.Accounts.Select(a => a.Name));
        Assert.Equal((OtpKind.Hotp, 1UL), (result.Accounts[2].Kind, result.Accounts[2].Counter));
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void WindowsLineBreaksAndAByteOrderMark()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("otpauth://totp/A:a?secret=JBSWY3DPEHPK3PXP\r\notpauth://totp/B:b?secret=GEZDGNBVGY3TQOJQ\r\n"))
            .ToArray();
        var result = AccountImporter.Import(bytes);
        Assert.Equal(["A", "B"], result.Accounts.Select(a => a.Issuer));
    }

    [Fact]
    public void Utf16Text()
    {
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("otpauth://totp/Zoë:zoe?secret=JBSWY3DPEHPK3PXP"))
            .ToArray();
        Assert.Equal("Zoë", AccountImporter.Import(bytes).Accounts.Single().Issuer);
    }

    [Fact]
    public void PastedLink()
    {
        var result = AccountImporter.ImportLinks("  otpauth://totp/GitHub:octocat?secret=JBSWY3DPEHPK3PXP&issuer=GitHub \n");
        Assert.Equal("GitHub", result.Accounts.Single().Issuer);
    }

    [Fact]
    public void PastedLinkThatCantBeUsedSaysWhy()
    {
        var problem = Assert.Throws<ImportException>(() => AccountImporter.ImportLinks("otpauth://steam/Steam?secret=JBSWY3DPEHPK3PXP"));
        Assert.Contains("time-based or counter-based", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileWithoutLinks()
    {
        Assert.Throws<ImportException>(() => Import("just some notes\nnothing to see here\n"));
        Assert.Throws<ImportException>(() => Import(""));
    }

    // Google Authenticator

    const string GoogleExport =
        "otpauth-migration://offline?data=CjUKCkhlbGxvId6tvu8SGEV4YW1wbGU6YWxpY2VAZ29vZ2xlLmNvbRoHRXhhbXBsZSABKAEwAgotChQxMjM0NTY3ODkwMTIzNDU2Nzg5MBIHb2N0b2NhdBoGR2l0SHViIAIoAjACCicKCmFiY2RlZmdoaWoSCWhvdHAtdXNlchoGTGVnYWN5IAEoATABOAUQARgBIAAolZrvOg%3D%3D";

    [Fact]
    public void GoogleAuthenticatorExport()
    {
        var result = Import(GoogleExport);
        Assert.Equal(3, result.Accounts.Count);
        Assert.Equal(0, result.Skipped);

        var first = result.Accounts[0];
        Assert.Equal("Example", first.Issuer);
        Assert.Equal("alice@google.com", first.Name);
        Assert.Equal("JBSWY3DPEHPK3PXP", Base32.Encode(first.Secret));
        Assert.Equal(OtpAlgorithm.Sha1, first.Algorithm);
        Assert.Equal(6, first.Digits);
        Assert.Equal(30, first.Period);

        var second = result.Accounts[1];
        Assert.Equal("GitHub", second.Issuer);
        Assert.Equal("octocat", second.Name);
        Assert.Equal("12345678901234567890"u8.ToArray(), second.Secret);
        Assert.Equal(OtpAlgorithm.Sha256, second.Algorithm);
        Assert.Equal(8, second.Digits);

        // Type 1 is counter-based, and field 7 holds the counter for its next code.
        var third = result.Accounts[2];
        Assert.Equal(("Legacy", "hotp-user"), (third.Issuer, third.Name));
        Assert.Equal((OtpKind.Hotp, 5UL), (third.Kind, third.Counter));
        Assert.Equal(Hotp.Code("abcdefghij"u8, 5), third.TakeNextHotpCode());
    }

    [Fact]
    public void GoogleAuthenticatorExportWithAnUnescapedLink()
    {
        // Some QR readers hand back the data without percent-encoding, or turn "+" into a space.
        var link = Uri.UnescapeDataString(GoogleExport);
        Assert.Equal(3, Import(link).Accounts.Count);
    }

    [Fact]
    public void GoogleAuthenticatorExportSplitAcrossSeveralCodes()
    {
        var result = Import(GoogleExport + "\n" + GoogleExport);
        Assert.Equal(6, result.Accounts.Count);
        Assert.Equal(0, result.Skipped);
    }

    [Theory]
    [InlineData("otpauth-migration://offline?data=CjUKCkhlbGxv")]
    [InlineData("otpauth-migration://offline?data=%%%")]
    [InlineData("otpauth-migration://offline")]
    public void DamagedGoogleAuthenticatorExport(string link)
    {
        Assert.Throws<ImportException>(() => Import(link));
    }

    // Safari and Apple Passwords

    [Fact]
    public void ApplePasswordsCsv()
    {
        var result = Import(""""
            Title,URL,Username,Password,Notes,OTPAuth
            github.com (octocat),https://github.com/,octocat,"hunter2, with ""quotes""",,otpauth://totp/GitHub:octocat?secret=JBSWY3DPEHPK3PXP&issuer=GitHub
            example.com (alice),https://example.com/,alice@example.com,s3cret-password!,"Notes that run
            onto a second line",
            accounts.google.com (bob@gmail.com),https://accounts.google.com/,bob@gmail.com,correct-horse,,otpauth://totp/bob%40gmail.com?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&algorithm=SHA1&digits=6&period=30
            """");
        Assert.Equal(2, result.Accounts.Count);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("GitHub", result.Accounts[0].Issuer);
        Assert.Equal("octocat", result.Accounts[0].Name);
        // Without an issuer in the link, the entry's title stands in.
        Assert.Equal("accounts.google.com (bob@gmail.com)", result.Accounts[1].Issuer);
        Assert.Equal("bob@gmail.com", result.Accounts[1].Name);
        AssertNoPasswordsKept(result, "hunter2", "s3cret-password!", "correct-horse");
    }

    [Fact]
    public void SafariCsvWithWindowsLineBreaks()
    {
        var csv = "Title,URL,Username,Password,Notes,OTPAuth\r\n"
            + "Dropbox,https://www.dropbox.com/,alice@example.com,pa55word,,\"otpauth://totp/Dropbox:alice%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Dropbox\"\r\n";
        var result = Import(csv);
        Assert.Equal("Dropbox", result.Accounts.Single().Issuer);
        Assert.Equal("alice@example.com", result.Accounts.Single().Name);
    }

    [Fact]
    public void PasswordCsvWithoutCodes()
    {
        var problem = Assert.Throws<ImportException>(() => Import("Title,URL,Username,Password,Notes,OTPAuth\nexample.com,https://example.com/,alice,hunter2,,\n"));
        Assert.DoesNotContain("hunter2", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherPasswordCsvIsNotMistakenForCodes()
    {
        // A Chrome export has no OTPAuth column, and nothing in it is an otpauth link.
        Assert.Throws<ImportException>(() => Import("name,url,username,password,note\nexample.com,https://example.com/,alice,hunter2,\n"));
    }

    // Bitwarden

    [Fact]
    public void BitwardenJson()
    {
        var result = Import("""
            {
              "encrypted": false,
              "folders": [],
              "items": [
                {
                  "passwordHistory": null,
                  "revisionDate": "2026-09-01T12:00:00.000Z",
                  "creationDate": "2026-01-01T12:00:00.000Z",
                  "deletedDate": null,
                  "id": "6c2b4b1e-7a43-4f6e-9c61-b0e3a1d5f001",
                  "organizationId": null,
                  "folderId": null,
                  "type": 1,
                  "reprompt": 0,
                  "name": "GitHub (work)",
                  "notes": "Recovery codes are in the safe",
                  "favorite": false,
                  "login": {
                    "fido2Credentials": [],
                    "uris": [{ "match": null, "uri": "https://github.com/login" }],
                    "username": "octocat",
                    "password": "correct horse battery staple",
                    "totp": "otpauth://totp/GitHub:octocat?secret=JBSWY3DPEHPK3PXP&issuer=GitHub"
                  },
                  "collectionIds": null
                },
                {
                  "id": "6c2b4b1e-7a43-4f6e-9c61-b0e3a1d5f002",
                  "type": 1,
                  "name": "Dropbox",
                  "login": {
                    "uris": [{ "match": null, "uri": "https://www.dropbox.com/" }],
                    "username": "alice@example.com",
                    "password": "Tr0ub4dor&3",
                    "totp": "GEZD GNBV GY3T QOJQ GEZD GNBV GY3T QOJQ"
                  }
                },
                {
                  "id": "6c2b4b1e-7a43-4f6e-9c61-b0e3a1d5f003",
                  "type": 1,
                  "name": "Steam",
                  "login": { "username": "gamer", "password": "letmein", "totp": "steam://JBSWY3DPEHPK3PXP" }
                },
                {
                  "id": "6c2b4b1e-7a43-4f6e-9c61-b0e3a1d5f004",
                  "type": 1,
                  "name": "Bank",
                  "login": { "username": "alice", "password": "p@ssw0rd", "totp": null }
                },
                {
                  "id": "6c2b4b1e-7a43-4f6e-9c61-b0e3a1d5f005",
                  "type": 2,
                  "name": "Wi-Fi",
                  "notes": "The password is on the router",
                  "secureNote": { "type": 0 }
                }
              ]
            }
            """);
        Assert.Equal(2, result.Accounts.Count);
        Assert.Equal(1, result.Skipped);

        Assert.Equal("GitHub (work)", result.Accounts[0].Issuer);
        Assert.Equal("octocat", result.Accounts[0].Name);

        var dropbox = result.Accounts[1];
        Assert.Equal("Dropbox", dropbox.Issuer);
        Assert.Equal("alice@example.com", dropbox.Name);
        Assert.Equal("12345678901234567890"u8.ToArray(), dropbox.Secret);
        Assert.Equal((OtpAlgorithm.Sha1, 6, 30), (dropbox.Algorithm, dropbox.Digits, dropbox.Period));

        AssertNoPasswordsKept(result, "correct horse", "Tr0ub4dor", "letmein", "p@ssw0rd");
    }

    [Fact]
    public void EncryptedBitwardenJson()
    {
        var problem = Assert.Throws<ImportException>(() => Import("""
            { "encrypted": true, "passwordProtected": true, "salt": "c2FsdA==", "kdfType": 0, "kdfIterations": 600000, "encKeyValidation_DO_NOT_EDIT": "2.abc", "data": "2.xyz" }
            """));
        Assert.Contains("encrypted", problem.Message, StringComparison.Ordinal);
    }

    // Aegis

    [Fact]
    public void AegisJson()
    {
        var result = Import("""
            {
              "version": 1,
              "header": { "slots": null, "params": null },
              "db": {
                "version": 3,
                "entries": [
                  {
                    "type": "totp",
                    "uuid": "01234567-89ab-cdef-0123-456789abcdef",
                    "name": "alice@example.com",
                    "issuer": "Mastodon",
                    "note": "",
                    "favorite": false,
                    "icon": null,
                    "info": { "secret": "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "algo": "SHA256", "digits": 8, "period": 60 },
                    "groups": []
                  },
                  {
                    "type": "totp",
                    "uuid": "11234567-89ab-cdef-0123-456789abcdef",
                    "name": "octocat",
                    "issuer": "GitHub",
                    "note": "",
                    "favorite": true,
                    "icon": null,
                    "info": { "secret": "JBSWY3DPEHPK3PXP", "algo": "SHA1", "digits": 6, "period": 30 },
                    "groups": []
                  },
                  {
                    "type": "hotp",
                    "uuid": "21234567-89ab-cdef-0123-456789abcdef",
                    "name": "vpn",
                    "issuer": "Legacy",
                    "info": { "secret": "JBSWY3DPEHPK3PXP", "algo": "SHA1", "digits": 6, "counter": 3 }
                  },
                  {
                    "type": "steam",
                    "uuid": "31234567-89ab-cdef-0123-456789abcdef",
                    "name": "gamer",
                    "issuer": "Steam",
                    "info": { "secret": "JBSWY3DPEHPK3PXP", "algo": "SHA1", "digits": 5, "period": 30 }
                  }
                ],
                "groups": [],
                "icons_optimized": true
              }
            }
            """);
        Assert.Equal(["Mastodon", "GitHub", "Legacy"], result.Accounts.Select(a => a.Issuer));
        // Steam codes use their own alphabet, so they're still skipped.
        Assert.Equal(1, result.Skipped);
        Assert.Equal((OtpKind.Hotp, 3UL), (result.Accounts[2].Kind, result.Accounts[2].Counter));
        Assert.Equal(OtpKind.Totp, result.Accounts[0].Kind);
        var mastodon = result.Accounts[0];
        Assert.Equal((OtpAlgorithm.Sha256, 8, 60), (mastodon.Algorithm, mastodon.Digits, mastodon.Period));
        Assert.Equal("alice@example.com", mastodon.Name);
    }

    [Fact]
    public void EncryptedAegisJson()
    {
        Assert.Throws<ImportException>(() => Import("""
            {
              "version": 1,
              "header": {
                "slots": [{ "type": 1, "uuid": "a", "key": "b", "key_params": { "nonce": "c", "tag": "d" }, "n": 32768, "r": 8, "p": 1, "salt": "e" }],
                "params": { "nonce": "f", "tag": "g" }
              },
              "db": "dGhpcyBpcyBlbmNyeXB0ZWQ="
            }
            """));
    }

    // 2FAS

    [Fact]
    public void TwoFasJson()
    {
        var result = Import("""
            {
              "services": [
                {
                  "name": "GitHub",
                  "secret": "JBSWY3DPEHPK3PXP",
                  "updatedAt": 1761580800000,
                  "otp": {
                    "label": "GitHub:octocat",
                    "account": "octocat",
                    "issuer": "GitHub",
                    "digits": 6,
                    "period": 30,
                    "algorithm": "SHA1",
                    "tokenType": "TOTP",
                    "source": "Link"
                  },
                  "order": { "position": 0 },
                  "icon": { "selected": "Label", "label": { "text": "GI", "backgroundColor": "Turquoise" } }
                },
                {
                  "name": "Cloud Console",
                  "secret": "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
                  "updatedAt": 1761580800000,
                  "otp": {
                    "label": "ops@example.com",
                    "digits": 8,
                    "period": 30,
                    "algorithm": "SHA512",
                    "tokenType": "TOTP",
                    "source": "Manual"
                  },
                  "order": { "position": 1 }
                },
                {
                  "name": "Legacy VPN",
                  "secret": "JBSWY3DPEHPK3PXP",
                  "otp": { "account": "vpn", "digits": 6, "counter": 4, "algorithm": "SHA1", "tokenType": "HOTP" }
                },
                {
                  "name": "Steam",
                  "secret": "JBSWY3DPEHPK3PXP",
                  "otp": { "account": "gamer", "digits": 5, "period": 30, "algorithm": "SHA1", "tokenType": "STEAM" }
                }
              ],
              "groups": [],
              "updatedAt": 1761580800000,
              "schemaVersion": 4,
              "appVersionCode": 5000012,
              "appVersionName": "5.4.1",
              "appOrigin": "android"
            }
            """);
        Assert.Equal(["GitHub", "Cloud Console", "Legacy VPN"], result.Accounts.Select(a => a.Issuer));
        Assert.Equal(["octocat", "ops@example.com", "vpn"], result.Accounts.Select(a => a.Name));
        Assert.Equal(1, result.Skipped);
        Assert.Equal((OtpKind.Hotp, 4UL), (result.Accounts[2].Kind, result.Accounts[2].Counter));
        Assert.Equal((OtpAlgorithm.Sha512, 8), (result.Accounts[1].Algorithm, result.Accounts[1].Digits));
    }

    [Fact]
    public void EncryptedTwoFasJson()
    {
        Assert.Throws<ImportException>(() => Import("""
            { "services": [], "groups": [], "updatedAt": 1761580800000, "schemaVersion": 4, "servicesEncrypted": "YWJj:ZGVm:Z2hp", "reference": "cmVm" }
            """));
    }

    // andOTP

    [Fact]
    public void AndOtpJson()
    {
        var result = Import("""
            [
              {
                "secret": "JBSWY3DPEHPK3PXP",
                "issuer": "GitHub",
                "label": "octocat",
                "digits": 6,
                "type": "TOTP",
                "algorithm": "SHA1",
                "thumbnail": "Default",
                "last_used": 1761580800000,
                "used_frequency": 12,
                "period": 30,
                "tags": ["work"]
              },
              {
                "secret": "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
                "issuer": "Legacy",
                "label": "vpn",
                "digits": 8,
                "type": "HOTP",
                "algorithm": "SHA256",
                "thumbnail": "Default",
                "last_used": 1761580800000,
                "used_frequency": 3,
                "counter": 4,
                "tags": []
              },
              {
                "secret": "JBSWY3DPEHPK3PXP",
                "issuer": "Steam",
                "label": "gamer",
                "digits": 5,
                "type": "STEAM",
                "algorithm": "SHA1",
                "period": 30,
                "tags": []
              },
              {
                "secret": "GEZDGNBVGY3TQOJQ",
                "label": "Old entry without an issuer",
                "digits": 6,
                "type": "TOTP",
                "algorithm": "SHA1",
                "period": 60,
                "tags": []
              }
            ]
            """);
        Assert.Equal(["GitHub", "Legacy", ""], result.Accounts.Select(a => a.Issuer));
        Assert.Equal("Old entry without an issuer", result.Accounts[2].Title);
        Assert.Equal(60, result.Accounts[2].Period);
        Assert.Equal(1, result.Skipped);

        var legacy = result.Accounts[1];
        Assert.Equal((OtpKind.Hotp, 4UL, 8, OtpAlgorithm.Sha256), (legacy.Kind, legacy.Counter, legacy.Digits, legacy.Algorithm));
    }

    [Fact]
    public void JsonListThatIsntAndOtp()
    {
        Assert.Throws<ImportException>(() => Import("""[ { "name": "not an export" } ]"""));
        Assert.Throws<ImportException>(() => Import("[]"));
    }

    [Fact]
    public void UnknownJson()
    {
        Assert.Throws<ImportException>(() => Import("""{ "accounts": [] }"""));
        Assert.Throws<ImportException>(() => Import("""{ "broken": """));
    }

    // Merging

    [Fact]
    public void MergeSkipsDuplicates()
    {
        var existing = Import("otpauth://totp/GitHub:octocat?secret=JBSWY3DPEHPK3PXP").Accounts.ToList();
        var incoming = Import("""
            otpauth://totp/GitHub:octocat?secret=JBSWY3DPEHPK3PXP
            otpauth://totp/GitHub:hubot?secret=JBSWY3DPEHPK3PXP
            """).Accounts;
        Assert.Equal(1, OtpAccount.Merge(existing, incoming));
        Assert.Equal(["octocat", "hubot"], existing.Select(a => a.Name));
    }

    [Fact]
    public void SearchMatchesIssuerOrName()
    {
        var account = OtpAccount.Create("GitHub", "octocat", Base32.Decode("JBSWY3DPEHPK3PXP"))!;
        Assert.True(account.Matches(""));
        Assert.True(account.Matches("git"));
        Assert.True(account.Matches("OCTO"));
        Assert.False(account.Matches("gitlab"));
    }
}

public class VaultFormatTests
{
    [Fact]
    public void RoundTrips()
    {
        var accounts = new List<OtpAccount>
        {
            OtpAccount.Create("GitHub", "octocat", Base32.Decode("JBSWY3DPEHPK3PXP"))!,
            OtpAccount.Create("Zoë's \"Bank\"", "", "12345678901234567890"u8.ToArray(), OtpAlgorithm.Sha512, 8, 60)!,
        };
        var copy = VaultFormat.Deserialize(VaultFormat.Serialize(accounts));
        Assert.Equal(2, copy.Count);
        for (var i = 0; i < accounts.Count; i++)
        {
            Assert.Equal(accounts[i].Id, copy[i].Id);
            Assert.True(copy[i].IsSameAs(accounts[i]));
        }
    }

    [Fact]
    public void RoundTripsCounterBasedAccounts()
    {
        var account = OtpAccount.Create("Legacy", "vpn", "12345678901234567890"u8.ToArray(), kind: OtpKind.Hotp, counter: 7)!;
        account.TakeNextHotpCode();
        var copy = Assert.Single(VaultFormat.Deserialize(VaultFormat.Serialize([account])));
        Assert.Equal((OtpKind.Hotp, 8UL), (copy.Kind, copy.Counter));
    }

    [Fact]
    public void VaultsFromBeforeCountersAreTimeBased()
    {
        var old = VaultFormat.Deserialize("""
            {"version":1,"accounts":[{"id":"0b8f6a4e-2f0c-4e5b-9d3a-6f1e2c3b4a5d","issuer":"GitHub","name":"octocat","secret":"JBSWY3DPEHPK3PXP","algorithm":"SHA1","digits":6,"period":30}]}
            """u8.ToArray());
        var account = Assert.Single(old);
        Assert.Equal((OtpKind.Totp, 0UL), (account.Kind, account.Counter));
    }

    [Fact]
    public void RejectsUnknownKinds()
    {
        Assert.Throws<FormatException>(() => VaultFormat.Deserialize("""
            {"version":1,"accounts":[{"id":"0b8f6a4e-2f0c-4e5b-9d3a-6f1e2c3b4a5d","issuer":"x","name":"","secret":"JBSWY3DPEHPK3PXP","algorithm":"SHA1","digits":6,"period":30,"kind":"steam"}]}
            """u8.ToArray()));
    }

    [Fact]
    public void EmptyVault()
    {
        Assert.Empty(VaultFormat.Deserialize(VaultFormat.Serialize([])));
    }

    [Fact]
    public void RejectsDamagedData()
    {
        Assert.Throws<FormatException>(() => VaultFormat.Deserialize("not json"u8.ToArray()));
        Assert.Throws<FormatException>(() => VaultFormat.Deserialize("""{"version":2,"accounts":[]}"""u8.ToArray()));
        Assert.Throws<FormatException>(() => VaultFormat.Deserialize("""{"version":1,"accounts":[{"issuer":"x"}]}"""u8.ToArray()));
    }
}

// Mirrors the "Import fixes from review" suite in the macOS tests, apart from QR codes.
public class ImportReviewFixes
{
    [Theory]
    [InlineData("otpauth://totp/Macy's:alice?secret=JBSWY3DPEHPK3PXP&issuer=Macy's", "Macy's")]
    [InlineData("otpauth://totp/Acme,Inc:alice?secret=JBSWY3DPEHPK3PXP&issuer=Acme,Inc", "Acme,Inc")]
    [InlineData("otpauth://totp/Acme Inc:alice?secret=JBSWY3DPEHPK3PXP", "Acme Inc")]
    public void WholeLinksKeepTheirLabels(string link, string issuer)
    {
        foreach (var result in new[]
        {
            AccountImporter.ImportLinks(link),
            AccountImporter.Import(Encoding.UTF8.GetBytes("# My accounts\r\n  " + link + "\t\r\n")),
        })
        {
            var account = Assert.Single(result.Accounts);
            Assert.Equal(issuer, account.Issuer);
            Assert.Equal("alice", account.Name);
            Assert.Equal(0, result.Skipped);
        }
    }

    [Fact]
    public void LinkInsideOtherText()
    {
        var result = AccountImporter.ImportLinks("Scan this: <otpauth://totp/GitHub:wyatt?secret=JBSWY3DPEHPK3PXP>");
        var account = Assert.Single(result.Accounts);
        Assert.Equal(("GitHub", "wyatt"), (account.Issuer, account.Name));
    }

    [Fact]
    public void JunkSecretsAreSkipped()
    {
        var problem = Assert.Throws<ImportException>(() => AccountImporter.Import(Encoding.UTF8.GetBytes("""
            Title,URL,Username,Password,Notes,OTPAuth
            Site,https://site.example,wyatt,pw,,NONE
            """)));
        Assert.Contains("could be read", problem.Message, StringComparison.Ordinal);
        Assert.Null(OtpAccount.Create("", "", Base32.Decode("NONE")));
    }

    [Fact]
    public void JunkSecretsBesideRealOnes()
    {
        var csv = AccountImporter.Import(Encoding.UTF8.GetBytes("""
            Title,URL,Username,Password,Notes,OTPAuth
            Site,https://site.example,wyatt,pw,,NONE
            GitHub,https://github.com,octocat,pw,,JBSWY3DPEHPK3PXP
            """));
        Assert.Equal("GitHub", Assert.Single(csv.Accounts).Issuer);
        Assert.Equal(1, csv.Skipped);

        var bitwarden = AccountImporter.Import(Encoding.UTF8.GetBytes("""
            {
              "encrypted": false,
              "items": [
                { "type": 1, "name": "Site", "login": { "username": "wyatt", "password": "pw", "totp": "NONE" } },
                { "type": 1, "name": "GitHub", "login": { "username": "octocat", "password": "pw", "totp": "JBSWY3DPEHPK3PXP" } }
              ]
            }
            """));
        Assert.Equal("GitHub", Assert.Single(bitwarden.Accounts).Issuer);
        Assert.Equal(1, bitwarden.Skipped);
    }

    [Fact]
    public void LinkWithAShortSecret()
    {
        var problem = Assert.Throws<ImportException>(() => OtpAuthUri.Parse("otpauth://totp/Site:wyatt?secret=NONE"));
        Assert.Contains("too short", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralLinksOnOneLineOfText()
    {
        var result = AccountImporter.ImportLinks(
            "first \"otpauth://totp/A:a?secret=JBSWY3DPEHPK3PXP\" then otpauth://totp/B:b?secret=GEZDGNBVGY3TQOJQ");
        Assert.Equal(["A", "B"], result.Accounts.Select(account => account.Issuer));
    }
}
