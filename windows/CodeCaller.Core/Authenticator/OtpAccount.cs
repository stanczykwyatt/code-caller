namespace CodeCaller.Core.Authenticator;

public enum OtpAlgorithm { Sha1, Sha256, Sha512 }

/// <summary>One account in the authenticator: who it's for and the secret its codes come from.</summary>
public sealed class OtpAccount
{
    public const int MinDigits = 6;
    public const int MaxDigits = 8;
    /// <summary>Real secrets are at least 64 bits. Shorter ones are usually junk, like "NONE" in a CSV column.</summary>
    public const int MinSecretBytes = 8;

    OtpAccount(Guid id, string issuer, string name, byte[] secret, OtpAlgorithm algorithm, int digits, int period)
    {
        Id = id;
        Issuer = issuer;
        Name = name;
        Secret = secret;
        Algorithm = algorithm;
        Digits = digits;
        Period = period;
    }

    public Guid Id { get; }
    /// <summary>The service, such as "GitHub". Can be empty.</summary>
    public string Issuer { get; }
    /// <summary>The account at that service, such as an email address. Can be empty.</summary>
    public string Name { get; }
    public byte[] Secret { get; }
    public OtpAlgorithm Algorithm { get; }
    public int Digits { get; }
    /// <summary>How many seconds each code lasts.</summary>
    public int Period { get; }

    /// <summary>What to call the account in a list.</summary>
    public string Title => Issuer.Length > 0 ? Issuer : Name.Length > 0 ? Name : "Unnamed account";

    /// <summary>The second line under <see cref="Title"/>, if there's anything left to say.</summary>
    public string Subtitle => Issuer.Length > 0 ? Name : "";

    /// <summary>Makes an account, or returns null if the settings can't make valid codes.</summary>
    public static OtpAccount? Create(
        string? issuer,
        string? name,
        byte[]? secret,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1,
        int digits = 6,
        int period = 30,
        Guid? id = null)
    {
        if (secret is null || secret.Length < MinSecretBytes) return null;
        if (digits is < MinDigits or > MaxDigits || period <= 0) return null;
        return new OtpAccount(id ?? Guid.NewGuid(), Clean(issuer), Clean(name), secret, algorithm, digits, period);
    }

    /// <summary>Whether two accounts make the same codes for the same service, so one is a duplicate.</summary>
    public bool IsSameAs(OtpAccount other) =>
        Secret.AsSpan().SequenceEqual(other.Secret)
        && Algorithm == other.Algorithm
        && Digits == other.Digits
        && Period == other.Period
        && string.Equals(Issuer, other.Issuer, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the account's issuer or name contains <paramref name="query"/>.</summary>
    public bool Matches(string query)
    {
        query = query.Trim();
        return query.Length == 0
            || Issuer.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Adds the accounts that aren't already there, and says how many were new.</summary>
    public static int Merge(List<OtpAccount> into, IEnumerable<OtpAccount> accounts)
    {
        var added = 0;
        foreach (var account in accounts)
        {
            if (into.Exists(existing => existing.IsSameAs(account))) continue;
            into.Add(account);
            added++;
        }
        return added;
    }

    public static bool TryParseAlgorithm(string? text, out OtpAlgorithm algorithm)
    {
        switch (text?.Trim().Replace("-", "", StringComparison.Ordinal).ToUpperInvariant())
        {
            case null or "" or "SHA1":
                algorithm = OtpAlgorithm.Sha1;
                return true;
            case "SHA256":
                algorithm = OtpAlgorithm.Sha256;
                return true;
            case "SHA512":
                algorithm = OtpAlgorithm.Sha512;
                return true;
            default:
                algorithm = OtpAlgorithm.Sha1;
                return false;
        }
    }

    public static string AlgorithmName(OtpAlgorithm algorithm) => algorithm switch
    {
        OtpAlgorithm.Sha256 => "SHA256",
        OtpAlgorithm.Sha512 => "SHA512",
        _ => "SHA1",
    };

    static string Clean(string? text) => (text ?? "").Trim();
}
