namespace CodeCaller.Core.Authenticator;

/// <summary>RFC 4648 Base32, the alphabet authenticator secrets are written in.</summary>
public static class Base32
{
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Decodes a secret, ignoring case, spaces, hyphens and padding, which apps add to
    /// make secrets easier to type. Returns null if anything else is in the way.
    /// </summary>
    public static byte[]? Decode(string text)
    {
        var output = new byte[text.Length * 5 / 8];
        var count = 0;
        var buffer = 0;
        var bits = 0;
        var sawPadding = false;
        foreach (var c in text)
        {
            if (c is ' ' or '-' or '\t' or '\r' or '\n') continue;
            if (c == '=')
            {
                sawPadding = true;
                continue;
            }
            if (sawPadding) return null;
            var value = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= 'a' and <= 'z' => c - 'a',
                >= '2' and <= '7' => c - '2' + 26,
                _ => -1,
            };
            if (value < 0) return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[count++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }
        if (count == output.Length) return output;
        var exact = output[..count];
        Array.Clear(output);
        return exact;
    }

    /// <summary>Encodes bytes without padding, the way otpauth links carry secrets.</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var chars = new char[(data.Length * 8 + 4) / 5];
        var index = 0;
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                chars[index++] = Alphabet[(buffer >> bits) & 31];
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) chars[index++] = Alphabet[(buffer << (5 - bits)) & 31];
        var result = new string(chars, 0, index);
        Array.Clear(chars);
        return result;
    }
}
