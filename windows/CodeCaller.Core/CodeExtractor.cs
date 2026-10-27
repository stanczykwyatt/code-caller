using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeCaller.Core;

/// <summary>
/// Finds one-time verification codes in the text of an email.
/// </summary>
/// <remarks>
/// A message has to read like a sign-in or verification email before any code
/// is accepted. Every number or code-shaped word in it is then scored by its
/// shape and by the words around it, and the best one wins if it scores high
/// enough. This mirrors the macOS version's CodeExtractor.swift rule for rule,
/// so both apps pick the same code from the same message.
/// </remarks>
public static class CodeExtractor
{
    /// <summary>The score a candidate needs before it counts as a code.</summary>
    internal const int Threshold = 6;

    /// <summary>Returns the most likely verification code in a message, or null if it doesn't contain one.</summary>
    public static string? Code(string subject, string body)
    {
        subject = Normalize(subject);
        body = Normalize(Prefix(body, 20_000));
        if (!IsVerificationMessage(subject, body)) return null;

        string? bestCode = null;
        var bestScore = int.MinValue;
        foreach (var (text, inSubject) in new[] { (subject, true), (body, false) })
        {
            var context = new Context(
                text,
                inSubject,
                Ranges(Patterns.Keyword, text),
                Ranges(Patterns.Unrelated, text),
                Ranges(Patterns.Date, text));
            foreach (var candidate in Candidates(text))
            {
                var score = Score(candidate, context);
                if (score >= Threshold && score > bestScore)
                {
                    bestCode = candidate.Code;
                    bestScore = score;
                }
            }
        }
        return bestCode;
    }

    // Candidates

    internal enum Shape { Digits, GroupedDigits, Hyphenated, Mixed }

    /// <summary>A UTF-16 range, the same unit NSString and NSRange use.</summary>
    internal readonly record struct Span(int Location, int Length)
    {
        public int End => Location + Length;

        public bool Intersects(Span other) => Math.Min(End, other.End) - Math.Max(Location, other.Location) > 0;
    }

    internal readonly record struct Candidate(Span Range, string Code, Shape Shape)
    {
        public int ShapeScore => Shape switch
        {
            Shape.Digits => Code.Length == 6 ? 4 : 3,
            Shape.GroupedDigits => 4,
            Shape.Hyphenated => 3,
            _ => 2,
        };

        public bool CouldBeYear =>
            Shape == Shape.Digits && Code.Length == 4 && (Code.StartsWith("19", StringComparison.Ordinal) || Code.StartsWith("20", StringComparison.Ordinal));
    }

    internal static List<Candidate> Candidates(string text)
    {
        var found = new List<Candidate>();
        void Add(Regex pattern, Shape shape, Func<string, string>? code = null)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var range = new Span(match.Index, match.Length);
                if (found.Exists(c => c.Range.Intersects(range)) || IsPartOfLargerValue(range, text)) continue;
                found.Add(new Candidate(range, code is null ? match.Value : code(match.Value), shape));
            }
        }
        // Grouped digits go first so "482 913" isn't also read as two shorter numbers.
        Add(Patterns.GroupedDigits, Shape.GroupedDigits, value => string.Concat(value.Where(Characters.IsDigit)));
        Add(Patterns.Digits, Shape.Digits);
        Add(Patterns.Hyphenated, Shape.Hyphenated);
        Add(Patterns.Mixed, Shape.Mixed);
        return found;
    }

    /// <summary>
    /// Prices, percentages, phone numbers, dates and order numbers have telltale
    /// characters right next to the digits.
    /// </summary>
    internal static bool IsPartOfLargerValue(Span range, string text)
    {
        char? CharacterAt(int index) => index >= 0 && index < text.Length ? text[index] : null;

        if (CharacterAt(range.Location - 1) is char before)
        {
            if (Characters.ValueMarks.Contains(before)) return true;
            if (Characters.Joiners.Contains(before) && CharacterAt(range.Location - 2) is char other && Characters.IsDigit(other))
            {
                return true;
            }
        }
        if (CharacterAt(range.End) is char after)
        {
            if (after == '%') return true;
            if (Characters.Joiners.Contains(after) && CharacterAt(range.End + 1) is char other && Characters.IsDigit(other))
            {
                return true;
            }
        }
        return false;
    }

    // Scoring

    internal sealed record Context(string Text, bool InSubject, List<Span> Keywords, List<Span> Unrelated, List<Span> Dates);

    internal static int Score(Candidate candidate, Context context)
    {
        var score = candidate.ShapeScore;

        var keywordBefore = DistanceBefore(candidate.Range, context.Keywords);
        var keywordAfter = DistanceAfter(candidate.Range, context.Keywords);
        score += Math.Max(BonusForKeywordBefore(keywordBefore), BonusForKeywordAfter(keywordAfter));

        if (context.InSubject) score += 2;
        if (IsOnItsOwnLine(candidate.Range, context.Text)) score += 4;

        // An order number or a total sits closer to its own label than to the word "code".
        if (DistanceBefore(candidate.Range, context.Unrelated) is int unrelated
            && unrelated <= 40 && unrelated < (keywordBefore ?? int.MaxValue))
        {
            score -= 4;
        }
        if (candidate.CouldBeYear && DistanceBefore(candidate.Range, context.Dates) is int date && date <= 20)
        {
            score -= 5;
        }
        return score;
    }

    internal static int BonusForKeywordBefore(int? distance) => distance switch
    {
        null => 0,
        <= 15 => 5,
        <= 40 => 4,
        <= 100 => 2,
        _ => 0,
    };

    /// <summary>Covers subjects like "482913 is your Instagram code".</summary>
    internal static int BonusForKeywordAfter(int? distance) => distance is int value && value <= 25 ? 3 : 0;

    internal static bool IsOnItsOwnLine(Span range, string text)
    {
        var line = LineRange(range, text);
        var trimmed = Trim(text.AsSpan(line.Location, line.Length));
        return trimmed.SequenceEqual(text.AsSpan(range.Location, range.Length));
    }

    /// <summary>The whole line or lines holding a range, line break included, like NSString.lineRange(for:).</summary>
    internal static Span LineRange(Span range, string text)
    {
        var start = range.Location;
        while (start > 0 && !Characters.IsLineBreak(text[start - 1])) start--;

        var end = range.Length > 0 ? range.End - 1 : range.Location;
        while (end < text.Length && !Characters.IsLineBreak(text[end])) end++;
        if (end < text.Length)
        {
            end += text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
        }
        return new Span(start, end - start);
    }

    internal static int? DistanceBefore(Span range, List<Span> others)
    {
        int? best = null;
        foreach (var other in others)
        {
            if (other.End > range.Location) continue;
            var distance = range.Location - other.End;
            if (best is null || distance < best) best = distance;
        }
        return best;
    }

    internal static int? DistanceAfter(Span range, List<Span> others)
    {
        int? best = null;
        foreach (var other in others)
        {
            if (other.Location < range.End) continue;
            var distance = other.Location - range.End;
            if (best is null || distance < best) best = distance;
        }
        return best;
    }

    // Text

    internal static bool IsVerificationMessage(string subject, string body) =>
        Patterns.Verification.IsMatch(subject)
        || Patterns.Verification.IsMatch(body)
        || Patterns.CodeWord.IsMatch(subject);

    /// <summary>
    /// Folds full-width digits and letters to ASCII, and removes links and email
    /// addresses, whose numbers are never the code.
    /// </summary>
    internal static string Normalize(string text)
    {
        var folded = string.Create(text.Length, text, static (buffer, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                buffer[i] = c switch
                {
                    >= '\uFF10' and <= '\uFF19' or >= '\uFF21' and <= '\uFF3A' or >= '\uFF41' and <= '\uFF5A' => (char)(c - 0xFEE0),
                    '\u00A0' or '\u2007' or '\u202F' => ' ',
                    _ => c,
                };
            }
        });
        return Patterns.Link.Replace(folded, " ");
    }

    /// <summary>The first <paramref name="count"/> user-perceived characters, like Swift's String.prefix.</summary>
    internal static string Prefix(string text, int count)
    {
        if (text.Length <= count) return text;
        var index = 0;
        for (var taken = 0; taken < count && index < text.Length; taken++)
        {
            index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
        }
        return text[..index];
    }

    internal static List<Span> Ranges(Regex pattern, string text)
    {
        var ranges = new List<Span>();
        foreach (Match match in pattern.Matches(text)) ranges.Add(new Span(match.Index, match.Length));
        return ranges;
    }

    static ReadOnlySpan<char> Trim(ReadOnlySpan<char> text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && Characters.IsLineTrim(text[start])) start++;
        while (end > start && Characters.IsLineTrim(text[end - 1])) end--;
        return text[start..end];
    }

    static class Characters
    {
        public const string ValueMarks = "$€£¥₹#+№";
        public const string Joiners = " -.,:/";
        const string TrimMarks = ".:*-–—|>\"'";

        public static bool IsDigit(char c) => c is >= '0' and <= '9';

        /// <summary>Whitespace, line breaks and the punctuation that often sits around a code on its own line.</summary>
        public static bool IsLineTrim(char c) => char.IsWhiteSpace(c) || TrimMarks.Contains(c);

        /// <summary>The characters NSString treats as ending a line.</summary>
        public static bool IsLineBreak(char c) => c is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029';
    }

    static class Patterns
    {
        /// <summary>Latin-script words need letter boundaries so "code" doesn't match "barcode".</summary>
        static string Words(string alternatives) => @"(?<!\p{L})(?:" + alternatives + @")(?!\p{L})";

        // ICU's \w and \S, which NSRegularExpression uses, spelled out so .NET matches the same characters.
        const string Word = @"\p{L}\p{M}\p{Nd}\p{Pc}\p{Nl}\u200C\u200D\u24B6-\u24E9";
        const string NonSpace = @"[^\t\n\f\r\p{Z}]";

        static readonly string CodeWords = Words("codes?|passcodes?|otp|pin|c[oó]digos?|codice|kod|код") + "|码|碼|コード|번호";

        static readonly string VerificationWords = Words(
            @"v[eé]rif\p{L}*|authenticat\p{L}*|one[- ]?time|otp|passcodes?|2fa|mfa|two[- ]?(?:factor|step)"
            + @"|sign(?:ing)?[- ]?in|log(?:ging)?[- ]?in|login|security codes?|access codes?|confirmation codes?"
            + @"|launch codes?|magic codes?|connexion|best[äa]tig\p{L}*|sicherheitscode|anmeld\p{L}*|einmal\p{L}*"
            + @"|inlog\p{L}*|accesso|acceso|iniciar sesi[oó]n|подтвержд\p{L}*|вход\p{L}*|код\p{L}*"
        ) + "|验证码|驗證碼|校验码|校驗碼|动态码|動態碼|动态密码|确认码|確認碼|登录|登入|登錄|認証|ワンタイム|ログイン|인증|로그인";

        public static readonly Regex CodeWord = Make(CodeWords);
        public static readonly Regex Verification = Make(VerificationWords);
        public static readonly Regex Keyword = Make(
            CodeWords + "|" + VerificationWords + "|"
            + Words("passwords?|passwort|kennwort|contrase[ñn]a|mot de passe|security"));
        public static readonly Regex Unrelated = Make(Words(
            "orders?|invoice|receipt|tracking|shipment|ticket|case|promo(?:tion)?|coupon|discount|voucher|gift"
            + "|phone|tel|call|fax|zip|postal|booking|reservation|flight|ref(?:erence)?|ending in|card"
            + "|account number|amount|total|price|pedido|commande|bestellung|factura|rechnung"));
        public static readonly Regex Date = Make(Words(
            "jan|feb|mar|apr|may|jun|jul|aug|sept?|oct|nov|dec|january|february|march|april|june|july"
            + "|august|september|october|november|december|copyright") + @"|©|\(c\)");
        public static readonly Regex Link = Make(
            @"(?:https?://|www\.)" + NonSpace + "+"
            + "|[" + Word + ".+-]+@[" + Word + @"-]+(?:\.[" + Word + "-]+)+");

        public static readonly Regex GroupedDigits = Make(
            "(?<![0-9A-Za-z])(?:[0-9]{3}[ -][0-9]{3}|[0-9]{4}[ -][0-9]{4})(?![0-9A-Za-z])", caseSensitive: true);
        public static readonly Regex Digits = Make("(?<![0-9A-Za-z])[0-9]{4,8}(?![0-9A-Za-z])", caseSensitive: true);
        public static readonly Regex Hyphenated = Make(
            "(?<![0-9A-Za-z-])(?=[0-9-]*[A-Z])[A-Z0-9]{3,4}-[A-Z0-9]{3,4}(?![0-9A-Za-z-])", caseSensitive: true);
        public static readonly Regex Mixed = Make(
            "(?<![0-9A-Za-z])(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{5,8}(?![0-9A-Za-z])", caseSensitive: true);

        static Regex Make(string pattern, bool caseSensitive = false) =>
            new(pattern, RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase));
    }
}
