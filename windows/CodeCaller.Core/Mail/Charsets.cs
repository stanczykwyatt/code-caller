using System.Text;
using System.Text.Unicode;

namespace CodeCaller.Core.Mail;

/// <summary>Turns bytes in whatever charset a message declares into text.</summary>
internal static class Charsets
{
    static readonly Encoding Windows1252;

    static Charsets()
    {
        // Adds the legacy code pages (windows-1252, Shift_JIS, GB18030, EUC-KR, KOI8-R and so on)
        // that .NET leaves out by default. They ship with .NET; nothing is downloaded.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    public static string Decode(ReadOnlySpan<byte> bytes, string? charset)
    {
        var encoding = Find(charset);
        // Mail labeled ASCII or Latin-1 is often really UTF-8, and anything mislabeled reads
        // better as UTF-8 when the bytes allow it.
        if (encoding is null || encoding.CodePage is 20127 or 28591 or 1252)
        {
            return Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Windows1252.GetString(bytes);
        }
        return encoding.GetString(bytes);
    }

    /// <summary>Header text arrives as raw bytes; any 8-bit bytes in it are usually UTF-8.</summary>
    public static string DecodeHeaderBytes(ReadOnlySpan<byte> bytes) => Decode(bytes, null);

    static Encoding? Find(string? charset)
    {
        var name = charset?.Trim().Trim('"', '\'').ToLowerInvariant();
        if (string.IsNullOrEmpty(name)) return null;
        name = name switch
        {
            "utf8" or "utf-8" or "x-utf-8" => "utf-8",
            // GB18030 is a superset of both, and senders often use characters outside GB2312.
            "gb2312" or "gbk" or "x-gbk" or "cp936" => "gb18030",
            "iso-8859-8-i" => "iso-8859-8",
            "ks_c_5601-1987" or "ks_c_5601" or "x-windows-949" => "ks_c_5601-1987",
            "shift-jis" or "x-sjis" or "sjis" => "shift_jis",
            _ => name,
        };
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
