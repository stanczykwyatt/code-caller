using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Copy2FA.Core.Authenticator;

/// <summary>Time-based one-time passwords, as defined in RFC 6238.</summary>
public static class Totp
{
    public static string Code(OtpAccount account, DateTimeOffset time) =>
        Code(account.Secret, time, account.Digits, account.Period, account.Algorithm);

    public static string Code(
        ReadOnlySpan<byte> secret,
        DateTimeOffset time,
        int digits = 6,
        int period = 30,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, OtpAccount.MinDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, OtpAccount.MaxDigits);
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, Counter(time, period));

        Span<byte> hash = stackalloc byte[64];
        var length = algorithm switch
        {
            OtpAlgorithm.Sha256 => HMACSHA256.HashData(secret, counter, hash),
            OtpAlgorithm.Sha512 => HMACSHA512.HashData(secret, counter, hash),
            _ => HMACSHA1.HashData(secret, counter, hash),
        };

        // Dynamic truncation, RFC 4226 section 5.3.
        var offset = hash[length - 1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        CryptographicOperations.ZeroMemory(hash);

        var code = binary % Pow10(digits);
        return code.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    /// <summary>Seconds until the code shown at <paramref name="time"/> changes.</summary>
    public static int SecondsRemaining(int period, DateTimeOffset time)
    {
        var seconds = time.ToUnixTimeSeconds();
        return period - (int)(((seconds % period) + period) % period);
    }

    static long Counter(DateTimeOffset time, int period)
    {
        var seconds = time.ToUnixTimeSeconds();
        return seconds >= 0 ? seconds / period : (seconds - period + 1) / period;
    }

    static int Pow10(int digits)
    {
        var value = 1;
        for (var i = 0; i < digits; i++) value *= 10;
        return value;
    }
}
