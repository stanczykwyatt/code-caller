using System.Text;

namespace CodeCaller.Core.Mail;

/// <summary>A message or one part of a multipart message: its headers, and where its body sits in the original bytes.</summary>
internal sealed class MimeEntity
{
    /// <summary>Messages are cut off here, which is far more than any code email needs.</summary>
    const int MaxBodyBytes = 1024 * 1024;
    const int MaxDepth = 8;

    MimeEntity(List<KeyValuePair<string, string>> headers, ReadOnlyMemory<byte> body)
    {
        Headers = headers;
        Body = body;
    }

    /// <summary>Unfolded header values, still raw: encoded words and 8-bit text aren't decoded yet.</summary>
    public List<KeyValuePair<string, string>> Headers { get; }
    public ReadOnlyMemory<byte> Body { get; }

    public static MimeEntity Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var headers = new List<KeyValuePair<string, string>>();
        string? name = null;
        var value = new StringBuilder();
        var position = 0;

        void Finish()
        {
            if (name is not null) headers.Add(new(name, value.ToString().Trim()));
            name = null;
            value.Clear();
        }

        while (position < span.Length)
        {
            var end = span[position..].IndexOf((byte)'\n');
            var lineEnd = end < 0 ? span.Length : position + end;
            var next = end < 0 ? span.Length : lineEnd + 1;
            var line = span[position..lineEnd];
            if (!line.IsEmpty && line[^1] == (byte)'\r') line = line[..^1];
            position = next;

            if (line.IsEmpty) break;
            if (line[0] is (byte)' ' or (byte)'\t')
            {
                // A folded continuation of the previous header.
                if (name is not null) value.Append(' ').Append(Latin1(line.TrimStart(" \t"u8)));
                continue;
            }
            Finish();
            var colon = line.IndexOf((byte)':');
            if (colon <= 0) continue;
            name = Latin1(line[..colon]).Trim();
            value.Append(Latin1(line[(colon + 1)..]));
        }
        Finish();

        var body = data[Math.Min(position, data.Length)..];
        if (body.Length > MaxBodyBytes) body = body[..MaxBodyBytes];
        return new MimeEntity(headers, body);
    }

    public string? RawHeader(string name)
    {
        foreach (var header in Headers)
        {
            if (header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return header.Value;
        }
        return null;
    }

    public IEnumerable<string> RawHeaders(string name) =>
        Headers.Where(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value);

    /// <summary>A header as readable text, with 8-bit bytes and RFC 2047 encoded words decoded.</summary>
    public string Header(string name)
    {
        var raw = RawHeader(name);
        if (raw is null) return "";
        return EncodedWords.Decode(DecodeLatin1Text(raw));
    }

    /// <summary>The plain text of the message: its text/plain part if it has one, or its HTML with the tags removed.</summary>
    public string BodyText()
    {
        string? plain = null;
        string? html = null;
        FindText(this, 0, ref plain, ref html);
        if (!string.IsNullOrWhiteSpace(plain)) return plain;
        if (html is not null) return HtmlText.ToPlainText(html);
        return plain ?? "";
    }

    static void FindText(MimeEntity entity, int depth, ref string? plain, ref string? html)
    {
        if (depth > MaxDepth) return;
        var contentType = ContentType.Parse(entity.RawHeader("Content-Type"));
        var disposition = entity.RawHeader("Content-Disposition");
        if (disposition is not null && disposition.TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase)) return;

        if (contentType.MediaType.StartsWith("multipart/", StringComparison.Ordinal))
        {
            if (contentType.Parameter("boundary") is not { Length: > 0 } boundary) return;
            foreach (var part in Multipart.Split(entity.Body, boundary))
            {
                FindText(Parse(part), depth + 1, ref plain, ref html);
                if (plain is not null && !string.IsNullOrWhiteSpace(plain)) return;
            }
            return;
        }

        if (contentType.MediaType is not ("text/plain" or "text/html")) return;
        var bytes = Transfer.Decode(entity.Body.Span, entity.RawHeader("Content-Transfer-Encoding"));
        var text = Charsets.Decode(bytes, contentType.Parameter("charset"));
        if (contentType.MediaType == "text/plain") plain ??= text;
        else html ??= text;
    }

    /// <summary>Headers are kept as Latin-1 so no byte is lost; this turns any 8-bit run back into real text.</summary>
    static string DecodeLatin1Text(string raw)
    {
        foreach (var c in raw)
        {
            if (c > 0x7F) return Charsets.DecodeHeaderBytes(Encoding.Latin1.GetBytes(raw));
        }
        return raw;
    }

    static string Latin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);
}

/// <summary>A Content-Type header: the media type and its parameters.</summary>
internal sealed class ContentType
{
    readonly Dictionary<string, string> parameters;

    ContentType(string mediaType, Dictionary<string, string> parameters)
    {
        MediaType = mediaType;
        this.parameters = parameters;
    }

    public string MediaType { get; }

    public string? Parameter(string name) => parameters.TryGetValue(name, out var value) ? value : null;

    public static ContentType Parse(string? header)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(header)) return new ContentType("text/plain", parameters);

        var semicolon = header.IndexOf(';');
        var mediaType = (semicolon < 0 ? header : header[..semicolon]).Trim().ToLowerInvariant();
        if (!mediaType.Contains('/', StringComparison.Ordinal)) mediaType = "text/plain";

        var position = semicolon < 0 ? header.Length : semicolon + 1;
        while (position < header.Length)
        {
            while (position < header.Length && header[position] is ' ' or '\t' or ';') position++;
            var equals = header.IndexOf('=', position);
            if (equals < 0) break;
            var name = header[position..equals].Trim().ToLowerInvariant();
            position = equals + 1;
            while (position < header.Length && header[position] is ' ' or '\t') position++;

            string value;
            if (position < header.Length && header[position] == '"')
            {
                var builder = new StringBuilder();
                position++;
                while (position < header.Length && header[position] != '"')
                {
                    if (header[position] == '\\' && position + 1 < header.Length) position++;
                    builder.Append(header[position++]);
                }
                position++;
                value = builder.ToString();
            }
            else
            {
                var end = header.IndexOf(';', position);
                if (end < 0) end = header.Length;
                value = header[position..end].Trim();
                position = end;
            }

            // RFC 2231 extended values, such as charset*=utf-8''..., are rare for the parameters read here.
            if (name.EndsWith('*'))
            {
                name = name.TrimEnd('*');
                var quote = value.IndexOf('\'');
                var second = quote < 0 ? -1 : value.IndexOf('\'', quote + 1);
                if (second >= 0) value = Uri.UnescapeDataString(value[(second + 1)..]);
            }
            parameters.TryAdd(name, value);
        }
        return new ContentType(mediaType, parameters);
    }
}

/// <summary>Splits a multipart body at its boundary lines.</summary>
internal static class Multipart
{
    public static List<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> body, string boundary)
    {
        var parts = new List<ReadOnlyMemory<byte>>();
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var span = body.Span;
        var position = 0;
        int? partStart = null;

        while (position < span.Length)
        {
            var end = span[position..].IndexOf((byte)'\n');
            var lineEnd = end < 0 ? span.Length : position + end;
            var next = end < 0 ? span.Length : lineEnd + 1;
            var line = span[position..lineEnd];
            if (!line.IsEmpty && line[^1] == (byte)'\r') line = line[..^1];

            if (line.StartsWith(delimiter))
            {
                var rest = line[delimiter.Length..];
                var isClose = rest.StartsWith("--"u8);
                if (isClose) rest = rest[2..];
                if (rest.Trim(" \t"u8).IsEmpty)
                {
                    if (partStart is int start)
                    {
                        // The line break before a boundary belongs to the boundary.
                        var partEnd = position;
                        if (partEnd > start && span[partEnd - 1] == (byte)'\n') partEnd--;
                        if (partEnd > start && span[partEnd - 1] == (byte)'\r') partEnd--;
                        parts.Add(body[start..partEnd]);
                    }
                    if (isClose) return parts;
                    partStart = next;
                }
            }
            position = next;
        }
        // A message cut off before its closing boundary still has a usable last part.
        if (partStart is int last && last < span.Length) parts.Add(body[last..]);
        return parts;
    }
}
