using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Copy2FA.Core.Authenticator;

/// <summary>
/// Reads accounts from the files other authenticators and password managers export:
/// lists of otpauth:// links, Google Authenticator transfer links, Safari or Apple
/// Passwords CSV files, and unencrypted Bitwarden, Aegis and 2FAS JSON files.
/// </summary>
/// <remarks>
/// Password manager exports hold every password you have. The file is read in place,
/// and only the fields an authenticator needs are ever turned into strings. Passwords
/// and notes are skipped over without being read. The caller should still zero the
/// file's bytes when it's done with them.
/// </remarks>
public static class AccountImporter
{
    static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>Reads an export file of any supported kind.</summary>
    /// <exception cref="ImportException">The file can't be read, is encrypted, or holds no accounts.</exception>
    public static ImportResult Import(byte[] file)
    {
        if (file.Length >= 2 && (file[0], file[1]) is (0xFF, 0xFE) or (0xFE, 0xFF))
        {
            // UTF-16, as some Windows tools save text. Convert it, then wipe the copies.
            var encoding = file[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode;
            var chars = encoding.GetChars(file, 2, file.Length - 2);
            var utf8 = Encoding.UTF8.GetBytes(chars);
            Array.Clear(chars);
            try
            {
                return ImportUtf8(utf8);
            }
            finally
            {
                Array.Clear(utf8);
            }
        }
        return ImportUtf8(file);
    }

    /// <summary>Reads text that holds otpauth:// or otpauth-migration:// links, such as a pasted link.</summary>
    /// <exception cref="ImportException">There are no links, or none of them could be read.</exception>
    public static ImportResult ImportLinks(string text) => ImportLinks(Encoding.UTF8.GetBytes(text));

    static ImportResult ImportUtf8(byte[] bytes)
    {
        ReadOnlyMemory<byte> data = bytes;
        if (data.Span.StartsWith(Utf8Bom)) data = data[Utf8Bom.Length..];
        var first = data.Span.TrimStart(" \t\r\n"u8);
        if (!first.IsEmpty && first[0] is (byte)'{' or (byte)'[') return ImportJson(data);
        if (CsvExport.TryRead(data.Span, out var csv)) return csv;
        return ImportLinks(data.Span);
    }

    // Lists of links

    static ImportResult ImportLinks(ReadOnlySpan<byte> data)
    {
        var results = new List<ImportResult>();
        string? firstProblem = null;
        var linksFound = 0;

        var rest = data;
        while (!rest.IsEmpty)
        {
            var end = rest.IndexOfAny((byte)'\r', (byte)'\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? [] : rest[(end + 1)..];
            // Lines without a link are never turned into text, so nothing else in the file is read.
            if (IndexOfIgnoringCase(line, "otpauth"u8) < 0) continue;

            foreach (var link in LinksIn(Encoding.UTF8.GetString(line)))
            {
                linksFound++;
                try
                {
                    results.Add(GoogleAuthenticatorExport.IsMigrationLink(link)
                        ? GoogleAuthenticatorExport.Parse(link)
                        : new ImportResult([OtpAuthUri.Parse(link)], 0));
                }
                catch (ImportException problem)
                {
                    firstProblem ??= problem.Message;
                    results.Add(new ImportResult([], 1));
                }
            }
        }

        if (linksFound == 0) throw new ImportException("No accounts were found. The file needs otpauth:// links, or to be an export from a supported app.");
        var combined = ImportResult.Combine(results);
        if (combined.Accounts.Count == 0 && firstProblem is not null) throw new ImportException(firstProblem);
        return combined;
    }

    static readonly Regex EmbeddedLink = new(@"otpauth(?:-migration)?://[^\s""<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The links in one line of text. A line that's only a link is used whole, since labels
    /// like "Macy's", "Acme,Inc" or "Acme Inc" hold characters that would otherwise end it.
    /// </summary>
    internal static IEnumerable<string> LinksIn(string line)
    {
        line = line.Trim();
        if (OtpAuthUri.IsOtpAuth(line) || GoogleAuthenticatorExport.IsMigrationLink(line)) return [line];
        return EmbeddedLink.Matches(line).Select(match => match.Value);
    }

    static int IndexOfIgnoringCase(ReadOnlySpan<byte> text, ReadOnlySpan<byte> lowercaseWord)
    {
        for (var i = 0; i + lowercaseWord.Length <= text.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < lowercaseWord.Length && matches; j++)
            {
                var c = text[i + j];
                if (c is >= (byte)'A' and <= (byte)'Z') c = (byte)(c + 32);
                matches = c == lowercaseWord[j];
            }
            if (matches) return i;
        }
        return -1;
    }

    /// <summary>
    /// Password managers store either a full otpauth link or just the Base32 secret.
    /// A bare secret gets the standard settings: SHA1, 6 digits, 30 seconds.
    /// </summary>
    internal static OtpAccount? FromLinkOrSecret(string value, string? issuer, string? name, bool preferGivenIssuer)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        if (OtpAuthUri.IsOtpAuth(value))
        {
            OtpAccount parsed;
            try
            {
                parsed = OtpAuthUri.Parse(value);
            }
            catch (ImportException)
            {
                return null;
            }
            var useGiven = !string.IsNullOrWhiteSpace(issuer) && (preferGivenIssuer || parsed.Issuer.Length == 0);
            return OtpAccount.Create(
                useGiven ? issuer : parsed.Issuer,
                parsed.Name.Length > 0 ? parsed.Name : name,
                parsed.Secret,
                parsed.Algorithm,
                parsed.Digits,
                parsed.Period);
        }
        // Steam links and other schemes make codes that aren't standard TOTP.
        if (value.Contains("://", StringComparison.Ordinal)) return null;
        return OtpAccount.Create(issuer, name, Base32.Decode(value));
    }

    // JSON exports

    static ImportResult ImportJson(ReadOnlyMemory<byte> data)
    {
        JsonDocument document;
        try
        {
            // Parsing from memory reads the file in place, without copying it.
            document = JsonDocument.Parse(data, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException)
        {
            throw new ImportException("That file isn't valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("items", out _) || root.TryGetProperty("encrypted", out _)) return Bitwarden(root);
                if (root.TryGetProperty("db", out _)) return Aegis(root);
                if (root.TryGetProperty("services", out _) || root.TryGetProperty("servicesEncrypted", out _)) return TwoFas(root);
            }
            throw new ImportException("That JSON file isn't an export from Bitwarden, Aegis or 2FAS.");
        }
    }

    /// <summary>Bitwarden's unencrypted .json export. Only each login's name, username and TOTP field are read.</summary>
    static ImportResult Bitwarden(JsonElement root)
    {
        if (root.TryGetProperty("encrypted", out var encrypted) && encrypted.ValueKind == JsonValueKind.True)
        {
            throw new ImportException("This Bitwarden export is encrypted. Export your vault again in the plain .json format.");
        }
        var accounts = new List<OtpAccount>();
        var skipped = 0;
        foreach (var item in Items(root, "items"))
        {
            var login = Child(item, "login");
            var totp = Text(login, "totp");
            if (string.IsNullOrWhiteSpace(totp)) continue;
            var account = FromLinkOrSecret(totp, Text(item, "name"), Text(login, "username"), preferGivenIssuer: true);
            if (account is null) skipped++;
            else accounts.Add(account);
        }
        return Finish(accounts, skipped, "Bitwarden");
    }

    /// <summary>Aegis's unencrypted vault export.</summary>
    static ImportResult Aegis(JsonElement root)
    {
        var db = Child(root, "db");
        if (db.ValueKind == JsonValueKind.String)
        {
            throw new ImportException("This Aegis export is encrypted. Export your vault again without encryption.");
        }
        var accounts = new List<OtpAccount>();
        var skipped = 0;
        foreach (var entry in Items(db, "entries"))
        {
            var info = Child(entry, "info");
            var type = Text(entry, "type") ?? "totp";
            if (!type.Equals("totp", StringComparison.OrdinalIgnoreCase)
                || !OtpAccount.TryParseAlgorithm(Text(info, "algo"), out var algorithm))
            {
                skipped++;
                continue;
            }
            var account = OtpAccount.Create(
                Text(entry, "issuer"),
                Text(entry, "name"),
                Base32.Decode(Text(info, "secret") ?? ""),
                algorithm,
                Number(info, "digits", 6),
                Number(info, "period", 30));
            if (account is null) skipped++;
            else accounts.Add(account);
        }
        return Finish(accounts, skipped, "Aegis");
    }

    /// <summary>A 2FAS backup saved without a password.</summary>
    static ImportResult TwoFas(JsonElement root)
    {
        var encrypted = Text(root, "servicesEncrypted");
        if (!string.IsNullOrEmpty(encrypted))
        {
            throw new ImportException("This 2FAS backup is protected with a password. Export it again without one.");
        }
        var accounts = new List<OtpAccount>();
        var skipped = 0;
        foreach (var service in Items(root, "services"))
        {
            var otp = Child(service, "otp");
            var type = Text(otp, "tokenType") ?? "TOTP";
            if (!type.Equals("TOTP", StringComparison.OrdinalIgnoreCase)
                || !OtpAccount.TryParseAlgorithm(Text(otp, "algorithm"), out var algorithm))
            {
                skipped++;
                continue;
            }
            var issuer = Text(service, "name");
            if (string.IsNullOrWhiteSpace(issuer)) issuer = Text(otp, "issuer");
            var name = Text(otp, "account");
            if (string.IsNullOrWhiteSpace(name)) name = Text(otp, "label");
            var account = OtpAccount.Create(
                issuer,
                name,
                Base32.Decode(Text(service, "secret") ?? ""),
                algorithm,
                Number(otp, "digits", 6),
                Number(otp, "period", 30));
            if (account is null) skipped++;
            else accounts.Add(account);
        }
        return Finish(accounts, skipped, "2FAS");
    }

    static ImportResult Finish(List<OtpAccount> accounts, int skipped, string app)
    {
        if (accounts.Count == 0 && skipped == 0) throw new ImportException($"That {app} export has no two-factor accounts in it.");
        if (accounts.Count == 0) throw new ImportException($"None of the accounts in that {app} export use time-based codes.");
        return new ImportResult(accounts, skipped);
    }

    static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

    static IEnumerable<JsonElement> Items(JsonElement element, string name)
    {
        var array = Child(element, name);
        return array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];
    }

    static string? Text(JsonElement element, string name)
    {
        var value = Child(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    static int Number(JsonElement element, string name, int fallback)
    {
        var value = Child(element, name);
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var number) ? number : -1,
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1,
            _ => fallback,
        };
    }
}
