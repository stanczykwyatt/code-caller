using System.Text;

namespace CodeCaller.Core.Authenticator;

/// <summary>
/// Reads the CSV files Safari and Apple Passwords export, with the columns
/// Title, URL, Username, Password, Notes and OTPAuth.
/// </summary>
/// <remarks>
/// Only the Title, Username and OTPAuth columns are ever decoded. Every other field,
/// passwords included, is stepped over byte by byte and never becomes a string.
/// </remarks>
internal static class CsvExport
{
    /// <summary>Returns false if the file doesn't have an OTPAuth column, so it isn't this kind of export.</summary>
    public static bool TryRead(ReadOnlySpan<byte> data, out ImportResult result)
    {
        result = new ImportResult([], 0);
        // A header row is short. A long first line means this isn't a CSV export at all.
        var firstLine = data.IndexOfAny((byte)'\r', (byte)'\n');
        if ((firstLine < 0 ? data.Length : firstLine) > 4096) return false;
        var reader = new CsvReader(data);

        // The header row names the columns. It holds no secrets, so it's fine to decode.
        int otp = -1, title = -1, username = -1;
        var column = 0;
        while (reader.TryReadField(out var raw, out var quoted, out var endOfRow))
        {
            var name = CsvReader.Decode(raw, quoted).Trim().ToLowerInvariant();
            switch (name)
            {
                case "otpauth": otp = column; break;
                case "title" or "name": title = column; break;
                case "username" or "login" or "login_username": username = column; break;
            }
            column++;
            if (endOfRow) break;
        }
        if (otp < 0) return false;

        var accounts = new List<OtpAccount>();
        var skipped = 0;
        while (!reader.AtEnd)
        {
            string? otpValue = null, titleValue = null, usernameValue = null;
            column = 0;
            while (reader.TryReadField(out var raw, out var quoted, out var endOfRow))
            {
                if (column == otp) otpValue = CsvReader.Decode(raw, quoted);
                else if (column == title) titleValue = CsvReader.Decode(raw, quoted);
                else if (column == username) usernameValue = CsvReader.Decode(raw, quoted);
                column++;
                if (endOfRow) break;
            }
            if (string.IsNullOrWhiteSpace(otpValue)) continue;
            var account = AccountImporter.FromLinkOrSecret(otpValue, titleValue, usernameValue, preferGivenIssuer: false);
            if (account is null) skipped++;
            else accounts.Add(account);
        }

        if (accounts.Count == 0)
        {
            throw new ImportException(skipped == 0
                ? "None of the passwords in that file have verification codes set up."
                : "None of the verification codes in that file could be read.");
        }
        result = new ImportResult(accounts, skipped);
        return true;
    }
}

/// <summary>An RFC 4180 CSV reader that hands out fields as slices of the original bytes.</summary>
internal ref struct CsvReader
{
    readonly ReadOnlySpan<byte> data;
    int position;

    public CsvReader(ReadOnlySpan<byte> data)
    {
        this.data = data;
        position = 0;
    }

    public readonly bool AtEnd => position >= data.Length;

    /// <summary>
    /// Reads the next field. <paramref name="raw"/> still has doubled quotes in it when
    /// <paramref name="quoted"/> is true; <see cref="Decode"/> takes care of them.
    /// </summary>
    public bool TryReadField(out ReadOnlySpan<byte> raw, out bool quoted, out bool endOfRow)
    {
        raw = default;
        quoted = false;
        endOfRow = true;
        if (AtEnd) return false;

        if (data[position] == (byte)'"')
        {
            quoted = true;
            var start = ++position;
            while (position < data.Length)
            {
                if (data[position] == (byte)'"')
                {
                    if (position + 1 < data.Length && data[position + 1] == (byte)'"')
                    {
                        position += 2;
                        continue;
                    }
                    break;
                }
                position++;
            }
            raw = data[start..Math.Min(position, data.Length)];
            if (position < data.Length) position++;
            // Anything between the closing quote and the next comma isn't valid CSV; step over it.
            while (position < data.Length && data[position] is not ((byte)',' or (byte)'\r' or (byte)'\n')) position++;
        }
        else
        {
            var start = position;
            while (position < data.Length && data[position] is not ((byte)',' or (byte)'\r' or (byte)'\n')) position++;
            raw = data[start..position];
        }

        if (position >= data.Length) return true;
        switch (data[position])
        {
            case (byte)',':
                position++;
                endOfRow = false;
                break;
            case (byte)'\r':
                position++;
                if (position < data.Length && data[position] == (byte)'\n') position++;
                break;
            default:
                position++;
                break;
        }
        return true;
    }

    public static string Decode(ReadOnlySpan<byte> raw, bool quoted)
    {
        if (!quoted || raw.IndexOf("\"\""u8) < 0) return Encoding.UTF8.GetString(raw);
        var buffer = new byte[raw.Length];
        var length = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            buffer[length++] = raw[i];
            if (raw[i] == (byte)'"' && i + 1 < raw.Length && raw[i + 1] == (byte)'"') i++;
        }
        var text = Encoding.UTF8.GetString(buffer, 0, length);
        Array.Clear(buffer);
        return text;
    }
}
