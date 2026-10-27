using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeCaller.Core.Mail;

/// <summary>
/// Turns an HTML email into plain text, keeping the line structure a mail app shows,
/// so a code in its own table cell or paragraph still ends up on its own line.
/// </summary>
public static class HtmlText
{
    const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline;

    static readonly Regex Hidden = new(
        @"<!--.*?-->|<(script|style|head|title|template|noscript)\b[^>]*>.*?</\1\s*>", Options);
    static readonly Regex LineBreak = new(
        @"<br\b[^>]*>|</?(?:p|div|tr|li|ul|ol|dl|dt|dd|h[1-6]|table|tbody|thead|tfoot|blockquote|pre|section|article|header|footer|center|hr|address)\b[^>]*>",
        Options);
    static readonly Regex Cell = new(@"</?(?:td|th)\b[^>]*>", Options);
    static readonly Regex Tag = new(@"<[^>]*>", Options);
    static readonly Regex Entity = new(@"&(?:#(?<decimal>[0-9]{1,7})|#[xX](?<hex>[0-9a-fA-F]{1,6})|(?<name>[a-zA-Z][a-zA-Z0-9]{1,31}));?", RegexOptions.CultureInvariant);
    static readonly Regex Spaces = new(@"[ \t\f\v]+", RegexOptions.CultureInvariant);
    static readonly Regex BlankLines = new(@"\n{2,}", RegexOptions.CultureInvariant);

    public static string ToPlainText(string html)
    {
        var text = Hidden.Replace(html, " ");
        text = text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
        text = LineBreak.Replace(text, "\n");
        text = Cell.Replace(text, " ");
        text = Tag.Replace(text, "");
        text = DecodeEntities(text);
        text = RemoveInvisible(text);

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++) lines[i] = Spaces.Replace(lines[i], " ").Trim();
        // Block boundaries pile up, as in "</td></tr><tr><td>"; one line break stands for all of them.
        return BlankLines.Replace(string.Join('\n', lines), "\n").Trim();
    }

    public static string DecodeEntities(string text)
    {
        if (!text.Contains('&', StringComparison.Ordinal)) return text;
        return Entity.Replace(text, match =>
        {
            int codePoint;
            if (match.Groups["decimal"].Success)
            {
                codePoint = int.Parse(match.Groups["decimal"].Value, CultureInfo.InvariantCulture);
            }
            else if (match.Groups["hex"].Success)
            {
                codePoint = int.Parse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            else if (!Named.TryGetValue(match.Groups["name"].Value, out codePoint))
            {
                return match.Value;
            }
            if (codePoint is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) return "\uFFFD";
            return char.ConvertFromUtf32(codePoint);
        });
    }

    /// <summary>Drops the zero-width characters email preheaders are padded with.</summary>
    static string RemoveInvisible(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is (char)0x200B or (char)0x200C or (char)0x200D or (char)0x034F or (char)0x00AD or (char)0xFEFF or (char)0x2060) continue;
            builder.Append(c == (char)0x00A0 ? ' ' : c);
        }
        return builder.ToString();
    }

    static readonly Dictionary<string, int> Named = new(StringComparer.Ordinal)
    {
        ["amp"] = '&', ["AMP"] = '&', ["lt"] = '<', ["LT"] = '<', ["gt"] = '>', ["GT"] = '>',
        ["quot"] = '"', ["QUOT"] = '"', ["apos"] = '\'', ["nbsp"] = 0xA0, ["ensp"] = 0x2002, ["emsp"] = 0x2003,
        ["thinsp"] = 0x2009, ["zwnj"] = 0x200C, ["zwj"] = 0x200D, ["shy"] = 0xAD, ["ndash"] = 0x2013,
        ["mdash"] = 0x2014, ["lsquo"] = 0x2018, ["rsquo"] = 0x2019, ["sbquo"] = 0x201A, ["ldquo"] = 0x201C,
        ["rdquo"] = 0x201D, ["bdquo"] = 0x201E, ["laquo"] = 0xAB, ["raquo"] = 0xBB, ["hellip"] = 0x2026,
        ["bull"] = 0x2022, ["middot"] = 0xB7, ["copy"] = 0xA9, ["reg"] = 0xAE, ["trade"] = 0x2122,
        ["euro"] = 0x20AC, ["pound"] = 0xA3, ["yen"] = 0xA5, ["cent"] = 0xA2, ["sect"] = 0xA7, ["deg"] = 0xB0,
        ["times"] = 0xD7, ["divide"] = 0xF7, ["iexcl"] = 0xA1, ["iquest"] = 0xBF, ["ordf"] = 0xAA, ["ordm"] = 0xBA,
        ["Agrave"] = 0xC0, ["Aacute"] = 0xC1, ["Acirc"] = 0xC2, ["Atilde"] = 0xC3, ["Auml"] = 0xC4, ["Aring"] = 0xC5,
        ["AElig"] = 0xC6, ["Ccedil"] = 0xC7, ["Egrave"] = 0xC8, ["Eacute"] = 0xC9, ["Ecirc"] = 0xCA, ["Euml"] = 0xCB,
        ["Igrave"] = 0xCC, ["Iacute"] = 0xCD, ["Icirc"] = 0xCE, ["Iuml"] = 0xCF, ["Ntilde"] = 0xD1, ["Ograve"] = 0xD2,
        ["Oacute"] = 0xD3, ["Ocirc"] = 0xD4, ["Otilde"] = 0xD5, ["Ouml"] = 0xD6, ["Oslash"] = 0xD8, ["Ugrave"] = 0xD9,
        ["Uacute"] = 0xDA, ["Ucirc"] = 0xDB, ["Uuml"] = 0xDC, ["Yacute"] = 0xDD, ["szlig"] = 0xDF, ["agrave"] = 0xE0,
        ["aacute"] = 0xE1, ["acirc"] = 0xE2, ["atilde"] = 0xE3, ["auml"] = 0xE4, ["aring"] = 0xE5, ["aelig"] = 0xE6,
        ["ccedil"] = 0xE7, ["egrave"] = 0xE8, ["eacute"] = 0xE9, ["ecirc"] = 0xEA, ["euml"] = 0xEB, ["igrave"] = 0xEC,
        ["iacute"] = 0xED, ["icirc"] = 0xEE, ["iuml"] = 0xEF, ["ntilde"] = 0xF1, ["ograve"] = 0xF2, ["oacute"] = 0xF3,
        ["ocirc"] = 0xF4, ["otilde"] = 0xF5, ["ouml"] = 0xF6, ["oslash"] = 0xF8, ["ugrave"] = 0xF9, ["uacute"] = 0xFA,
        ["ucirc"] = 0xFB, ["uuml"] = 0xFC, ["yacute"] = 0xFD, ["yuml"] = 0xFF,
    };
}
