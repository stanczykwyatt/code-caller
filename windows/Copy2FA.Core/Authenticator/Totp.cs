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
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        return Hotp.Code(secret, unchecked((ulong)Counter(time, period)), digits, algorithm);
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
}
