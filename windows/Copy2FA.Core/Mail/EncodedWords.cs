using System.Text;
using System.Text.RegularExpressions;

namespace Copy2FA.Core.Mail;

/// <summary>RFC 2047 encoded words, such as <c>=?UTF-8?B?w6l0w6k=?=</c> in a subject line.</summary>
internal static class EncodedWords
{
    static readonly Regex Word = new(
        @"=\?(?<charset>[^?\s*]+)(?:\*[^?\s]*)?\?(?<encoding>[BbQq])\?(?<text>[^?\s]*)\?=",
        RegexOptions.CultureInvariant);

    public static string Decode(string text)
    {
        if (!text.Contains("=?", StringComparison.Ordinal)) return text;

        var output = new StringBuilder(text.Length);
        var pending = new List<byte>();
        string? pendingCharset = null;
        var position = 0;

        void Flush()
        {
            if (pendingCharset is null) return;
            output.Append(Charsets.Decode(pending.ToArray(), pendingCharset));
            pending.Clear();
            pendingCharset = null;
        }

        foreach (Match match in Word.Matches(text))
        {
            var between = text[position..match.Index];
            // Whitespace between two encoded words is only there to fold the line, so it's dropped.
            if (pendingCharset is null || between.Trim().Length > 0)
            {
                Flush();
                output.Append(between);
            }
            var charset = match.Groups["charset"].Value;
            if (pendingCharset is not null && !pendingCharset.Equals(charset, StringComparison.OrdinalIgnoreCase)) Flush();
            pendingCharset = charset;
            var payload = match.Groups["text"].Value;
            if (match.Groups["encoding"].Value is "B" or "b") AppendBase64(pending, payload);
            else AppendQ(pending, payload);
            position = match.Index + match.Length;
        }
        Flush();
        output.Append(text[position..]);
        return output.ToString();
    }

    static void AppendBase64(List<byte> output, string text)
    {
        var bytes = Transfer.DecodeBase64(Encoding.ASCII.GetBytes(text));
        output.AddRange(bytes);
    }

    static void AppendQ(List<byte> output, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '_')
            {
                output.Add((byte)' ');
            }
            else if (c == '=' && i + 2 < text.Length && Transfer.TryHex(text[i + 1], text[i + 2], out var value))
            {
                output.Add(value);
                i += 2;
            }
            else
            {
                output.Add((byte)c);
            }
        }
    }
}
