namespace Copy2FA.Core.Authenticator;

/// <summary>
/// Reads the <c>otpauth-migration://offline?data=...</c> links that Google Authenticator's
/// "Transfer accounts" QR codes hold.
/// </summary>
/// <remarks>
/// The data is a base64 protocol buffer, decoded here by hand so no library is needed:
/// <code>
/// message MigrationPayload {
///   repeated OtpParameters otp_parameters = 1;
///   int32 version = 2; int32 batch_size = 3; int32 batch_index = 4; int32 batch_id = 5;
/// }
/// message OtpParameters {
///   bytes secret = 1; string name = 2; string issuer = 3;
///   Algorithm algorithm = 4;   // 0 unspecified, 1 SHA1, 2 SHA256, 3 SHA512, 4 MD5
///   DigitCount digits = 5;     // 0 unspecified, 1 six, 2 eight
///   OtpType type = 6;          // 0 unspecified, 1 HOTP, 2 TOTP
///   int64 counter = 7;         // HOTP only: the counter for the next code
/// }
/// </code>
/// </remarks>
public static class GoogleAuthenticatorExport
{
    const string Scheme = "otpauth-migration://";

    public static bool IsMigrationLink(string text) =>
        text.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads every time-based account in a link. Others, like HOTP counters, are counted as skipped.</summary>
    public static ImportResult Parse(string link)
    {
        link = link.Trim();
        if (!IsMigrationLink(link)) throw new ImportException("That isn't a Google Authenticator export link.");
        var question = link.IndexOf('?');
        var parameters = OtpAuthUri.ParseQuery(question < 0 ? "" : link[(question + 1)..]);
        if (!parameters.TryGetValue("data", out var data) || data.Length == 0)
        {
            throw new ImportException("That Google Authenticator export link has no data.");
        }

        var payload = DecodeBase64(data) ?? throw Damaged();
        try
        {
            return ReadPayload(payload);
        }
        catch (FormatException)
        {
            throw Damaged();
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    static ImportException Damaged() => new("That Google Authenticator export link is incomplete or damaged.");

    static byte[]? DecodeBase64(string text)
    {
        // Undo the ways a link gets mangled on its way here: "+" read as a space, or URL-safe base64.
        var normalized = text.Trim().Replace(' ', '+').Replace('-', '+').Replace('_', '/');
        normalized = normalized.TrimEnd('=');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        var buffer = new byte[normalized.Length * 3 / 4];
        if (!Convert.TryFromBase64String(normalized, buffer, out var written)) return null;
        return buffer[..written];
    }

    static ImportResult ReadPayload(byte[] payload)
    {
        var accounts = new List<OtpAccount>();
        var skipped = 0;
        var reader = new ProtoReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 1 && wireType == ProtoReader.LengthDelimited)
            {
                var account = ReadParameters(reader.ReadBytes());
                if (account is null) skipped++;
                else accounts.Add(account);
            }
            else
            {
                reader.Skip(wireType);
            }
        }
        return new ImportResult(accounts, skipped);
    }

    static OtpAccount? ReadParameters(ReadOnlySpan<byte> message)
    {
        byte[]? secret = null;
        var name = "";
        var issuer = "";
        ulong algorithm = 0, digits = 0, type = 0, counter = 0;

        var reader = new ProtoReader(message);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (1, ProtoReader.LengthDelimited): secret = reader.ReadBytes().ToArray(); break;
                case (2, ProtoReader.LengthDelimited): name = reader.ReadString(); break;
                case (3, ProtoReader.LengthDelimited): issuer = reader.ReadString(); break;
                case (4, ProtoReader.Varint): algorithm = reader.ReadVarint(); break;
                case (5, ProtoReader.Varint): digits = reader.ReadVarint(); break;
                case (6, ProtoReader.Varint): type = reader.ReadVarint(); break;
                case (7, ProtoReader.Varint): counter = reader.ReadVarint(); break;
                default: reader.Skip(wireType); break;
            }
        }

        // MD5 isn't a standard algorithm for these codes, so those accounts are skipped.
        OtpKind? kind = type switch
        {
            0 or 2 => OtpKind.Totp,
            1 => OtpKind.Hotp,
            _ => null,
        };
        OtpAlgorithm? otpAlgorithm = algorithm switch
        {
            0 or 1 => OtpAlgorithm.Sha1,
            2 => OtpAlgorithm.Sha256,
            3 => OtpAlgorithm.Sha512,
            _ => null,
        };
        int? digitCount = digits switch
        {
            0 or 1 => 6,
            2 => 8,
            _ => null,
        };
        if (kind is null || otpAlgorithm is null || digitCount is null) return null;

        // Names often carry the issuer too, as in "GitHub:octocat".
        var colon = name.IndexOf(':');
        if (colon >= 0)
        {
            if (issuer.Trim().Length == 0) issuer = name[..colon];
            name = name[(colon + 1)..];
        }
        // For counter-based accounts, the export's counter is the one the next code comes from.
        return OtpAccount.Create(issuer, name, secret, otpAlgorithm.Value, digitCount.Value, 30, kind: kind.Value, counter: counter);
    }
}
