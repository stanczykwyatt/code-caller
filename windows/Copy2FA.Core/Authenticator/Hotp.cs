using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Copy2FA.Core.Authenticator;

/// <summary>Counter-based one-time passwords, as defined in RFC 4226. TOTP is HOTP with a counter taken from the clock.</summary>
public static class Hotp
{
    public static string Code(
        ReadOnlySpan<byte> secret,
        ulong counter,
        int digits = 6,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, OtpAccount.MinDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, OtpAccount.MaxDigits);

        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(message, counter);

        Span<byte> hash = stackalloc byte[64];
        var length = algorithm switch
        {
            OtpAlgorithm.Sha256 => HMACSHA256.HashData(secret, message, hash),
            OtpAlgorithm.Sha512 => HMACSHA512.HashData(secret, message, hash),
            _ => HMACSHA1.HashData(secret, message, hash),
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

    static int Pow10(int digits)
    {
        var value = 1;
        for (var i = 0; i < digits; i++) value *= 10;
        return value;
    }
}
