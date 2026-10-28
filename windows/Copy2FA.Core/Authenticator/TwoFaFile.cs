using System.Globalization;
using System.Text;

namespace Copy2FA.Core.Authenticator;

/// <summary>
/// Reads the <c>~/.2fa</c> file that the rsc/2fa command-line tool keeps its keys in.
/// </summary>
/// <remarks>
/// Each line is <c>name size key</c> for a time-based key, or <c>name size key counter</c>
/// for a counter-based one, separated by single spaces. The size is 6, 7 or 8 digits, the
/// key is uppercase Base32 padded with "=", and the counter is 20 zero-padded digits
/// holding the last counter used, so the next code comes from the one after it.
/// A file only counts as this format when every non-empty line matches, and lines are
/// checked as bytes first, so nothing in any other kind of file is turned into text.
/// </remarks>
internal static class TwoFaFile
{
    const int CounterDigits = 20;

    public static bool TryRead(ReadOnlySpan<byte> data, out ImportResult result)
    {
        result = new ImportResult([], 0);
        var sawKey = false;
        foreach (var range in Lines(data))
        {
            var line = data[range];
            if (line.IsEmpty) continue;
            if (!Matches(line)) return false;
            sawKey = true;
        }
        if (!sawKey) return false;

        var accounts = new List<OtpAccount>();
        var skipped = 0;
        foreach (var range in Lines(data))
        {
            var line = data[range];
            if (line.IsEmpty) continue;
            var account = Read(line);
            if (account is null) skipped++;
            else accounts.Add(account);
        }
        if (accounts.Count == 0) throw new ImportException("None of the keys in that 2fa file could be read.");
        result = new ImportResult(accounts, skipped);
        return true;
    }

    /// <summary>Whether a line is <c>name size key</c> or <c>name size key counter</c>, exactly as rsc/2fa writes it.</summary>
    internal static bool Matches(ReadOnlySpan<byte> line)
    {
        var space = line.IndexOf((byte)' ');
        if (space <= 0) return false;
        var rest = line[(space + 1)..];

        if (rest.Length < 3 || rest[0] is < (byte)'6' or > (byte)'8' || rest[1] != (byte)' ') return false;
        rest = rest[2..];

        var length = 0;
        while (length < rest.Length && rest[length] is >= (byte)'A' and <= (byte)'Z' or >= (byte)'2' and <= (byte)'7') length++;
        if (length == 0) return false;
        while (length < rest.Length && rest[length] == (byte)'=') length++;
        if (length == rest.Length) return true;

        if (rest[length] != (byte)' ') return false;
        var counter = rest[(length + 1)..];
        return counter.Length == CounterDigits && counter.IndexOfAnyExceptInRange((byte)'0', (byte)'9') < 0;
    }

    static OtpAccount? Read(ReadOnlySpan<byte> line)
    {
        var parts = Encoding.UTF8.GetString(line).Split(' ');
        var name = parts[0];
        var digits = parts[1][0] - '0';
        var secret = Base32.Decode(parts[2]);
        if (parts.Length == 3) return OtpAccount.Create(name, "", secret, digits: digits);

        // The file holds the last counter used; the next code comes from the one after it.
        if (!ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var last) || last == ulong.MaxValue) return null;
        return OtpAccount.Create(name, "", secret, digits: digits, kind: OtpKind.Hotp, counter: last + 1);
    }

    /// <summary>Each line's range, without its line break. rsc/2fa writes "\n", but "\r\n" works too.</summary>
    static List<Range> Lines(ReadOnlySpan<byte> data)
    {
        var lines = new List<Range>();
        var start = 0;
        while (start <= data.Length)
        {
            var newline = data[start..].IndexOf((byte)'\n');
            var end = newline < 0 ? data.Length : start + newline;
            var trimmedEnd = end > start && data[end - 1] == (byte)'\r' ? end - 1 : end;
            lines.Add(start..trimmedEnd);
            if (newline < 0) break;
            start = end + 1;
        }
        return lines;
    }
}
