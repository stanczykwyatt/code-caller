using System.Globalization;

namespace CodeCaller.Core;

public static class SenderName
{
    /// <summary>A short, readable name for a sender such as <c>"GitHub" &lt;noreply@github.com&gt;</c>.</summary>
    public static string Display(string sender)
    {
        var trimmed = sender.Trim();
        var open = trimmed.IndexOf('<');
        if (open < 0)
        {
            return trimmed.Contains('@') ? Domain(trimmed) : trimmed;
        }
        var name = TrimNameMarks(trimmed.AsSpan(0, open));
        if (!name.IsEmpty) return name.ToString();
        var address = trimmed.AsSpan(open + 1);
        var close = address.IndexOf('>');
        return Domain((close < 0 ? address : address[..close]).ToString());
    }

    internal static string Domain(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 0) return address;
        var labels = address[(at + 1)..].ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2) return string.Join('.', labels);
        // Shorten "accounts.google.com" to "google.com", but keep "example.co.uk" whole.
        var isCountryPair = Length(labels[^2]) <= 3 && Length(labels[^1]) == 2;
        return string.Join('.', labels[^(isCountryPair ? 3 : 2)..]);
    }

    static int Length(string label) => new StringInfo(label).LengthInTextElements;

    /// <summary>Trims spaces, tabs and quotes, but not line breaks, like the macOS version.</summary>
    static ReadOnlySpan<char> TrimNameMarks(ReadOnlySpan<char> text)
    {
        static bool IsMark(char c) => c is '\t' or '"' or '\'' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

        var start = 0;
        var end = text.Length;
        while (start < end && IsMark(text[start])) start++;
        while (end > start && IsMark(text[end - 1])) end--;
        return text[start..end];
    }
}
