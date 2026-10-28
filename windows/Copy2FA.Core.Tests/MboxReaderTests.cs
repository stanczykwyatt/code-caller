using System.Text;
using Copy2FA.Core.Mail;

namespace Copy2FA.Core.Tests;

public class MboxReaderTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 27, 16, 45, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Cutoff = Now.AddMinutes(-10);

    static MboxReaderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    const string GitHub = """
        From - Tue Oct 27 09:40:12 2026
        X-Mozilla-Status: 0001
        X-Mozilla-Status2: 00000000
        X-Mozilla-Keys:
        Return-Path: <noreply@github.com>
        Delivered-To: alice@example.com
        Received: by 2002:a05:6a10:8b0a:b0:5c1:2f3:abcd with SMTP id m10csp123456pxb;
                Tue, 27 Oct 2026 09:40:10 -0700 (PDT)
        Received: from out-21.smtp.github.com (out-21.smtp.github.com. [192.30.252.204])
                by mx.google.com with ESMTPS id x12si1234567qkj.300.2026.10.27.09.40.09
                for <alice@example.com>;
                Tue, 27 Oct 2026 09:40:09 -0700 (PDT)
        Date: Tue, 27 Oct 2026 09:40:08 -0700
        From: GitHub <noreply@github.com>
        To: alice@example.com
        Message-ID: <6720f1a8_1@github.com>
        Subject: [GitHub] Please verify your device
        Mime-Version: 1.0
        Content-Type: multipart/alternative;
         boundary="--==_mimepart_6720f1a8_1";
         charset=UTF-8
        Content-Transfer-Encoding: 7bit


        ----==_mimepart_6720f1a8_1
        Content-Type: text/plain;
         charset=UTF-8
        Content-Transfer-Encoding: quoted-printable

        Hey alice!

        A sign in attempt requires further verification because we did not recogni=
        ze your device. To complete the sign in, enter the verification code on the=
         unrecognized device.

        Device: Firefox on Windows
        Verification code: 48291370

        If you did not attempt to sign in to your account, your password may be com=
        promised.

        ----==_mimepart_6720f1a8_1
        Content-Type: text/html;
         charset=UTF-8
        Content-Transfer-Encoding: quoted-printable

        <!DOCTYPE html><html><body><p>Verification code: <strong>48291370</strong></p></body></html>
        ----==_mimepart_6720f1a8_1--

        """;

    const string OldMessage = """
        From - Tue Oct 27 09:20:00 2026
        X-Mozilla-Status: 0001
        Received: from mail.example.com by mx.example.net; Tue, 27 Oct 2026 16:20:00 +0000
        Date: Tue, 27 Oct 2026 16:19:58 +0000
        From: Example <security@example.com>
        Subject: Your sign-in code
        Content-Type: text/plain; charset=us-ascii

        Your sign-in code is 111111.

        """;

    // Base64 of: <html><head><style>p{color:red}</style></head><body><table><tr><td>Votre code de v&eacute;rification&nbsp;:</td></tr><tr><td style="font-size:32px">739 104</td></tr></table><p>Valable 10&nbsp;minutes.</p></body></html>
    const string HtmlOnly = """
        From - Tue Oct 27 09:42:30 2026
        X-Mozilla-Status: 0000
        Date: Tue, 27 Oct 2026 17:42:00 +0100
        From: =?iso-8859-1?Q?Soci=E9t=E9_G=E9n=E9rale?= <noreply@example.fr>
        Subject: =?UTF-8?B?Vm90cmUgY29kZSBkZSB2w6lyaWZp?=
         =?UTF-8?B?Y2F0aW9u?=
        MIME-Version: 1.0
        Content-Type: text/html; charset="utf-8"
        Content-Transfer-Encoding: base64

        PGh0bWw+PGhlYWQ+PHN0eWxlPnB7Y29sb3I6cmVkfTwvc3R5bGU+PC9oZWFkPjxib2R5Pjx0YWJs
        ZT48dHI+PHRkPlZvdHJlIGNvZGUgZGUgdiZlYWN1dGU7cmlmaWNhdGlvbiZuYnNwOzo8L3RkPjwv
        dHI+PHRyPjx0ZCBzdHlsZT0iZm9udC1zaXplOjMycHgiPjczOSAxMDQ8L3RkPjwvdHI+PC90YWJs
        ZT48cD5WYWxhYmxlIDEwJm5ic3A7bWludXRlcy48L3A+PC9ib2R5PjwvaHRtbD4=

        """;

    const string Deleted = """
        From - Tue Oct 27 09:44:00 2026
        X-Mozilla-Status: 0009
        Date: Tue, 27 Oct 2026 16:44:00 +0000
        From: Deleted <deleted@example.com>
        Subject: Your verification code
        Content-Type: text/plain

        Your verification code is 999999.

        """;

    static string Mbox(params string[] messages) => string.Concat(messages).ReplaceLineEndings("\r\n");

    [Fact]
    public void ReadsRecentMessagesNewestFirst()
    {
        var messages = MboxReader.Parse(Mbox(OldMessage, GitHub, HtmlOnly, Deleted), Cutoff);

        Assert.Equal(2, messages.Count);
        Assert.Equal("Société Générale <noreply@example.fr>", messages[0].Sender);
        Assert.Equal("Votre code de vérification", messages[0].Subject);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 16, 42, 0, TimeSpan.Zero), messages[0].Received);

        Assert.Equal("GitHub <noreply@github.com>", messages[1].Sender);
        Assert.Equal("[GitHub] Please verify your device", messages[1].Subject);
        // The newest Received header, not the Date header, says when it arrived.
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 16, 40, 10, TimeSpan.Zero), messages[1].Received);
    }

    [Fact]
    public void PrefersPlainTextAndJoinsSoftLineBreaks()
    {
        var github = MboxReader.Parse(Mbox(GitHub), Cutoff).Single();
        Assert.Contains("we did not recognize your device.", github.Body, StringComparison.Ordinal);
        Assert.Contains("Verification code: 48291370\r\n", github.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>", github.Body, StringComparison.Ordinal);
        Assert.Equal("48291370", CodeExtractor.Code(github.Subject, github.Body));
    }

    [Fact]
    public void StripsHtmlWhenThereIsNoPlainText()
    {
        var message = MboxReader.Parse(Mbox(HtmlOnly), Cutoff).Single();
        Assert.Equal("Votre code de vérification :\n739 104\nValable 10 minutes.", message.Body);
        Assert.Equal("739104", CodeExtractor.Code(message.Subject, message.Body));
    }

    [Fact]
    public void SkipsDeletedAndOldMessages()
    {
        Assert.Empty(MboxReader.Parse(Mbox(OldMessage, Deleted), Cutoff));
    }

    [Fact]
    public void LimitsHowManyMessagesAreRead()
    {
        var messages = MboxReader.Parse(Mbox(GitHub, HtmlOnly), Cutoff, maxMessages: 1);
        Assert.Equal("Votre code de vérification", messages.Single().Subject);
    }

    [Fact]
    public void ReadsLegacyCharsetsAnd8BitBodies()
    {
        var shiftJis = Encoding.GetEncoding("shift_jis");
        var header = """
            From - Tue Oct 27 09:43:00 2026
            X-Mozilla-Status: 0001
            Date: Wed, 28 Oct 2026 01:43:00 +0900
            From: =?ISO-2022-JP?B?GyRCJWEhPCVrGyhC?= <info@example.jp>
            Subject: =?shift_jis?B?lEaP2INSgVuDaA==?=
            Content-Type: text/plain; charset=Shift_JIS
            Content-Transfer-Encoding: 8bit


            """.ReplaceLineEndings("\r\n");
        var bytes = Encoding.ASCII.GetBytes(header)
            .Concat(shiftJis.GetBytes("認証コード：４８２９１３\r\n有効期限は10分です。\r\n"))
            .ToArray();

        var message = MboxReader.Parse(bytes, Cutoff).Single();
        Assert.Equal("メール <info@example.jp>", message.Sender);
        Assert.Equal("認証コード", message.Subject);
        Assert.Equal("482913", CodeExtractor.Code(message.Subject, message.Body));
    }

    [Fact]
    public void ReadsWindows1252QuotedPrintable()
    {
        var mbox = Mbox("""
            From - Tue Oct 27 09:41:00 2026
            Date: Tue, 27 Oct 2026 16:41:00 GMT
            From: "Caf=E9" <cafe@example.com>
            Subject: =?windows-1252?Q?Code_de_s=E9curit=E9?=
            Content-Type: text/plain; charset=windows-1252
            Content-Transfer-Encoding: quoted-printable

            Votre code de s=E9curit=E9 : 5521 8840
            >From the team at Caf=E9

            """);
        var message = MboxReader.Parse(mbox, Cutoff).Single();
        Assert.Equal("Code de sécurité", message.Subject);
        Assert.StartsWith("Votre code de sécurité : 5521 8840", message.Body, StringComparison.Ordinal);
        Assert.Equal("55218840", CodeExtractor.Code(message.Subject, message.Body));
    }

    [Fact]
    public void FallsBackToTheEnvelopeTime()
    {
        var mbox = Mbox("""
            From - Tue Oct 27 16:43:00 2026
            From: someone@example.com
            Subject: Login code

            Use 7788 to log in.

            """);
        var message = MboxReader.Parse(Encoding.UTF8.GetBytes(mbox), Cutoff, localZone: TimeZoneInfo.Utc).Single();
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 16, 43, 0, TimeSpan.Zero), message.Received);
    }

    [Fact]
    public void DoesNotSplitOnFromInsideABody()
    {
        var mbox = Mbox("""
            From - Tue Oct 27 09:41:00 2026
            Date: Tue, 27 Oct 2026 16:41:00 +0000
            From: Example <hello@example.com>
            Subject: Your login code
            Content-Type: text/plain

            Your login code is 2468.
            From what we can tell, this was you.

            """);
        var message = MboxReader.Parse(mbox, Cutoff).Single();
        Assert.Contains("From what we can tell", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsOnlyTheEndOfLargeFiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            var filler = string.Concat(Enumerable.Range(0, 200).Select(i => OldMessage.Replace("111111", i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));
            File.WriteAllText(path, Mbox(filler, GitHub));

            var tail = MboxReader.ReadTail(path, 8 * 1024);
            Assert.True(tail.Length <= 8 * 1024);
            Assert.StartsWith("From - ", Encoding.ASCII.GetString(tail, 0, 7), StringComparison.Ordinal);

            var messages = MboxReader.ReadRecent(path, Cutoff);
            Assert.Equal("[GitHub] Please verify your device", messages.Single().Subject);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptyAndMissingContent()
    {
        Assert.Empty(MboxReader.Parse("", Cutoff));
        Assert.Empty(MboxReader.Parse("not an mbox file\r\n", Cutoff));
    }
}

public class MimeTests
{
    [Theory]
    [InlineData("=?UTF-8?B?w6k=?= =?UTF-8?B?dMOp?=", "été")]
    [InlineData("=?UTF-8?Q?caf=C3=A9_au_lait?=", "café au lait")]
    [InlineData("Re: =?utf-8?q?H=C3=A9llo?= world", "Re: Héllo world")]
    [InlineData("=?UTF-8?B?8J+Ukg==?= Security", "🔒 Security")]
    [InlineData("=?iso-8859-1*fr?Q?R=E9sum=E9?=", "Résumé")]
    [InlineData("No encoding here", "No encoding here")]
    [InlineData("=?unknown-charset?Q?plain?=", "plain")]
    public void DecodesEncodedWords(string raw, string expected)
    {
        Assert.Equal(expected, EncodedWords.Decode(raw));
    }

    [Theory]
    [InlineData("Tue, 27 Oct 2026 09:40:10 -0700 (PDT)", "2026-10-27T16:40:10Z")]
    [InlineData("27 Oct 2026 16:40:10 +0000", "2026-10-27T16:40:10Z")]
    [InlineData("Tue, 27 Oct 26 9:40 EDT", "2026-10-27T13:40:00Z")]
    [InlineData("Tuesday, 27-Oct-2026 16:40:10 GMT", "2026-10-27T16:40:10Z")]
    [InlineData("Tue, 27 Oct 2026 22:10:10 +05:30", "2026-10-27T16:40:10Z")]
    [InlineData("Tue, 27 Oct 2026 16:40:10", "2026-10-27T16:40:10Z")]
    public void ReadsMailDates(string text, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), MailDate.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("31 Feb 2026 10:00:00 +0000")]
    public void RejectsBadDates(string text)
    {
        Assert.Null(MailDate.Parse(text));
    }

    [Fact]
    public void ReadsContentTypeParameters()
    {
        var type = ContentType.Parse("Multipart/Alternative; boundary=\"b1_=_\\\"x\\\"\"; charset=UTF-8; name*=utf-8''r%C3%A9sum%C3%A9.pdf");
        Assert.Equal("multipart/alternative", type.MediaType);
        Assert.Equal("b1_=_\"x\"", type.Parameter("boundary"));
        Assert.Equal("UTF-8", type.Parameter("CHARSET"));
        Assert.Equal("résumé.pdf", type.Parameter("name"));
        Assert.Equal("text/plain", ContentType.Parse(null).MediaType);
    }

    [Fact]
    public void SkipsAttachmentsAndFindsNestedText()
    {
        var message = """
            Content-Type: multipart/mixed; boundary=outer

            --outer
            Content-Type: multipart/alternative; boundary=inner

            --inner
            Content-Type: text/plain; charset=utf-8

            Your one-time passcode is 902118.
            --inner
            Content-Type: text/html; charset=utf-8

            <p>Your one-time passcode is <b>902118</b>.</p>
            --inner--
            --outer
            Content-Type: text/plain; name=notes.txt
            Content-Disposition: attachment; filename=notes.txt

            Not this one: 111111
            --outer--
            """.ReplaceLineEndings("\r\n");
        var entity = MimeEntity.Parse(Encoding.UTF8.GetBytes(message));
        Assert.Equal("Your one-time passcode is 902118.", entity.BodyText());
    }

    [Fact]
    public void HtmlToText()
    {
        const string html = """
            <html><head><title>Sign in</title><style>.x { color: red }</style></head>
            <body>
              <!-- preheader -->
              <div style="display:none">Your code inside&zwnj;&nbsp;&zwnj;&nbsp;</div>
              <table><tr><td>Hi Alice,</td></tr>
              <tr><td align="center" style="font-size:28px"><b>4 8 2</b>&#57;&#x31;3</td></tr></table>
              <p>Questions? Email <a href="mailto:help@example.com">help&#64;example.com</a> &amp; we&rsquo;ll help.<br>Thanks</p>
              <script>var code = 111111;</script>
            </body></html>
            """;
        Assert.Equal(
            "Your code inside\nHi Alice,\n4 8 2913\nQuestions? Email help@example.com & we’ll help.\nThanks",
            HtmlText.ToPlainText(html));
    }
}

public class ThunderbirdProfilesTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "cc-thunderbird-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    string Touch(params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void FindsInboxesAndJunkFoldersInEveryProfile()
    {
        var thunderbird = Path.Combine(root, "Thunderbird");
        var portable = Path.Combine(root, "Elsewhere", "portable");
        Directory.CreateDirectory(Path.Combine(thunderbird, "Profiles", "wxyz9876.default"));
        File.WriteAllText(Path.Combine(thunderbird, "profiles.ini"), $"""
            [Install308046B0AF4A39CB]
            Default=Profiles/abcd1234.default-release
            Locked=1

            [Profile1]
            Name=default
            IsRelative=1
            Path=Profiles/wxyz9876.default

            [Profile0]
            Name=default-release
            IsRelative=1
            Path=Profiles/abcd1234.default-release
            Default=1

            [Profile2]
            Name=portable
            IsRelative=0
            Path={portable}

            [Profile3]
            Name=gone
            IsRelative=1
            Path=Profiles/deleted.profile

            [General]
            StartWithLastProfile=1
            Version=2
            """);

        var gmailInbox = Touch("Thunderbird", "Profiles", "abcd1234.default-release", "ImapMail", "imap.gmail.com", "INBOX");
        Touch("Thunderbird", "Profiles", "abcd1234.default-release", "ImapMail", "imap.gmail.com", "INBOX.msf");
        Touch("Thunderbird", "Profiles", "abcd1234.default-release", "ImapMail", "imap.gmail.com", "Drafts");
        var gmailSpam = Touch("Thunderbird", "Profiles", "abcd1234.default-release", "ImapMail", "imap.gmail.com", "[Gmail].sbd", "Spam");
        Touch("Thunderbird", "Profiles", "abcd1234.default-release", "ImapMail", "imap.gmail.com", "[Gmail].sbd", "All Mail");
        Touch("Thunderbird", "Profiles", "abcd1234.default-release", "ImapMail", "imap.gmail.com", "INBOX.sbd", "Spam");
        var localInbox = Touch("Thunderbird", "Profiles", "abcd1234.default-release", "Mail", "Local Folders", "Inbox");
        var localJunk = Touch("Thunderbird", "Profiles", "abcd1234.default-release", "Mail", "Local Folders", "Junk");
        Touch("Thunderbird", "Profiles", "abcd1234.default-release", "Mail", "Local Folders", "Trash");
        // Maildir accounts keep each folder as a directory, which this doesn't read.
        Directory.CreateDirectory(Path.Combine(thunderbird, "Profiles", "abcd1234.default-release", "Mail", "maildir.example.com", "Inbox", "cur"));
        var outlookInbox = Touch("Elsewhere", "portable", "ImapMail", "outlook.office365.com", "INBOX");
        var outlookJunk = Touch("Elsewhere", "portable", "ImapMail", "outlook.office365.com", "Junk");

        var profiles = ThunderbirdProfiles.ProfileDirectories(thunderbird);
        Assert.Equal(3, profiles.Count);
        Assert.EndsWith("abcd1234.default-release", profiles[0], StringComparison.Ordinal);

        var mailboxes = ThunderbirdProfiles.FindMailboxes(thunderbird).Select(Path.GetFullPath).Order(StringComparer.Ordinal).ToList();
        var expected = new[] { gmailInbox, gmailSpam, localInbox, localJunk, outlookInbox, outlookJunk }
            .Select(Path.GetFullPath).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, mailboxes);
    }

    [Fact]
    public void NoThunderbird()
    {
        Assert.Empty(ThunderbirdProfiles.FindMailboxes(Path.Combine(root, "missing")));
    }
}
