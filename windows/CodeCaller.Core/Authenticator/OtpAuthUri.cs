using System.Globalization;

namespace CodeCaller.Core.Authenticator;

/// <summary>
/// Reads <c>otpauth://totp/Issuer:account?secret=...&amp;issuer=...</c> links, the format
/// behind authenticator QR codes.
/// </summary>
public static class OtpAuthUri
{
    const string Scheme = "otpauth://";

    public static bool IsOtpAuth(string text) => text.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a link, or throws <see cref="ImportException"/> saying what's wrong with it.</summary>
    public static OtpAccount Parse(string text)
    {
        text = text.Trim();
        if (!IsOtpAuth(text)) throw new ImportException("That isn't an otpauth:// link.");

        var rest = text[Scheme.Length..];
        var fragment = rest.IndexOf('#');
        if (fragment >= 0) rest = rest[..fragment];

        var slash = rest.IndexOf('/');
        var type = slash < 0 ? rest : rest[..slash];
        if (type.Equals("hotp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImportException("Counter-based (HOTP) codes aren't supported, only time-based ones.");
        }
        if (!type.Equals("totp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImportException("That otpauth link isn't for a time-based code.");
        }

        var afterType = slash < 0 ? "" : rest[(slash + 1)..];
        var question = afterType.IndexOf('?');
        var label = Unescape(question < 0 ? afterType : afterType[..question]);
        var parameters = ParseQuery(question < 0 ? "" : afterType[(question + 1)..]);

        if (!parameters.TryGetValue("secret", out var secretText) || secretText.Trim().Length == 0)
        {
            throw new ImportException("That otpauth link has no secret.");
        }
        var secret = Base32.Decode(secretText) ?? throw new ImportException("That otpauth link's secret isn't valid Base32.");

        var labelIssuer = "";
        var account = label;
        var colon = label.IndexOf(':');
        if (colon >= 0)
        {
            labelIssuer = label[..colon];
            account = label[(colon + 1)..];
        }
        parameters.TryGetValue("issuer", out var issuer);
        if (string.IsNullOrWhiteSpace(issuer)) issuer = labelIssuer;

        parameters.TryGetValue("algorithm", out var algorithmText);
        if (!OtpAccount.TryParseAlgorithm(algorithmText, out var algorithm))
        {
            throw new ImportException($"The {algorithmText} algorithm isn't supported. Use SHA1, SHA256 or SHA512.");
        }

        var digits = ReadNumber(parameters, "digits", 6);
        if (digits is < OtpAccount.MinDigits or > OtpAccount.MaxDigits)
        {
            throw new ImportException("Codes need to be 6, 7 or 8 digits long.");
        }
        var period = ReadNumber(parameters, "period", 30);
        if (period <= 0) throw new ImportException("That otpauth link's period isn't valid.");

        return OtpAccount.Create(issuer, account, secret, algorithm, digits, period)
            ?? throw new ImportException("That otpauth link isn't valid.");
    }

    /// <summary>Builds a link for an account, the inverse of <see cref="Parse"/>.</summary>
    public static string Build(OtpAccount account)
    {
        var label = account.Issuer.Length > 0
            ? Uri.EscapeDataString(account.Issuer) + ":" + Uri.EscapeDataString(account.Name)
            : Uri.EscapeDataString(account.Name);
        var link = $"{Scheme}totp/{label}?secret={Base32.Encode(account.Secret)}";
        if (account.Issuer.Length > 0) link += "&issuer=" + Uri.EscapeDataString(account.Issuer);
        if (account.Algorithm != OtpAlgorithm.Sha1) link += "&algorithm=" + OtpAccount.AlgorithmName(account.Algorithm);
        if (account.Digits != 6) link += "&digits=" + account.Digits.ToString(CultureInfo.InvariantCulture);
        if (account.Period != 30) link += "&period=" + account.Period.ToString(CultureInfo.InvariantCulture);
        return link;
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = Unescape(equals < 0 ? pair : pair[..equals]);
            var value = equals < 0 ? "" : pair[(equals + 1)..];
            // Some apps write spaces in the issuer as "+", as web forms do.
            if (key.Equals("issuer", StringComparison.OrdinalIgnoreCase)) value = value.Replace('+', ' ');
            parameters.TryAdd(key, Unescape(value));
        }
        return parameters;
    }

    internal static string Unescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }

    static int ReadNumber(Dictionary<string, string> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var text) || text.Trim().Length == 0) return fallback;
        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;
    }
}
