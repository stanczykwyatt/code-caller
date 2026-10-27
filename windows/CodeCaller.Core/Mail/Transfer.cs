namespace CodeCaller.Core.Mail;

/// <summary>Content-Transfer-Encoding: undoes base64 and quoted-printable.</summary>
internal static class Transfer
{
    public static byte[] Decode(ReadOnlySpan<byte> body, string? encoding) =>
        encoding?.Trim().ToLowerInvariant() switch
        {
            "base64" => DecodeBase64(body),
            "quoted-printable" => DecodeQuotedPrintable(body),
            _ => body.ToArray(),
        };

    /// <summary>Decodes base64, skipping line breaks and anything else outside the alphabet, as mail readers do.</summary>
    public static byte[] DecodeBase64(ReadOnlySpan<byte> text)
    {
        var output = new byte[text.Length * 3 / 4 + 3];
        var count = 0;
        var buffer = 0;
        var bits = 0;
        foreach (var c in text)
        {
            int value = c switch
            {
                >= (byte)'A' and <= (byte)'Z' => c - 'A',
                >= (byte)'a' and <= (byte)'z' => c - 'a' + 26,
                >= (byte)'0' and <= (byte)'9' => c - '0' + 52,
                (byte)'+' or (byte)'-' => 62,
                (byte)'/' or (byte)'_' => 63,
                _ => -1,
            };
            if (c == (byte)'=')
            {
                // Padding ends a block. Some senders join several padded blocks, so keep going.
                buffer = 0;
                bits = 0;
                continue;
            }
            if (value < 0) continue;
            buffer = (buffer << 6) | value;
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                output[count++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }
        return output[..count];
    }

    public static byte[] DecodeQuotedPrintable(ReadOnlySpan<byte> text)
    {
        var output = new byte[text.Length];
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != (byte)'=')
            {
                output[count++] = c;
                continue;
            }
            // "=" at the end of a line is a soft line break, which joins the lines back up.
            var next = i + 1;
            while (next < text.Length && text[next] is (byte)' ' or (byte)'\t') next++;
            if (next < text.Length && text[next] == (byte)'\n')
            {
                i = next;
                continue;
            }
            if (next + 1 < text.Length && text[next] == (byte)'\r' && text[next + 1] == (byte)'\n')
            {
                i = next + 1;
                continue;
            }
            if (next >= text.Length)
            {
                break;
            }
            if (i + 2 < text.Length && TryHex((char)text[i + 1], (char)text[i + 2], out var value))
            {
                output[count++] = value;
                i += 2;
                continue;
            }
            output[count++] = c;
        }
        return output[..count];
    }

    public static bool TryHex(char high, char low, out byte value)
    {
        var h = HexValue(high);
        var l = HexValue(low);
        value = (byte)((h << 4) | l);
        return h >= 0 && l >= 0;
    }

    static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}
