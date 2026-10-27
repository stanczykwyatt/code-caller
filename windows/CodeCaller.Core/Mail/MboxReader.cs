using System.Globalization;
using System.Text;

namespace CodeCaller.Core.Mail;

/// <summary>
/// Reads recent messages from an mbox file, the format Thunderbird keeps each folder in.
/// </summary>
/// <remarks>
/// New mail is added to the end of the file, so only the last few megabytes are read.
/// Messages Thunderbird has deleted but not yet compacted away are skipped.
/// </remarks>
public static class MboxReader
{
    /// <summary>How much of the end of each file is read.</summary>
    public const int TailBytes = 4 * 1024 * 1024;

    /// <summary>Thunderbird's flag for a deleted message that's still in the file.</summary>
    const int ExpungedFlag = 0x0008;

    /// <summary>Messages received at or after <paramref name="cutoff"/> in the file at <paramref name="path"/>, newest first.</summary>
    public static List<MailMessage> ReadRecent(string path, DateTimeOffset cutoff, int maxMessages = 25)
    {
        var tail = ReadTail(path, TailBytes);
        return Parse(tail, cutoff, maxMessages);
    }

    /// <summary>Messages received at or after <paramref name="cutoff"/> in mbox text, newest first.</summary>
    public static List<MailMessage> Parse(string mbox, DateTimeOffset cutoff, int maxMessages = 25) =>
        Parse(Encoding.UTF8.GetBytes(mbox), cutoff, maxMessages);

    /// <summary>Messages received at or after <paramref name="cutoff"/> in raw mbox bytes, newest first.</summary>
    public static List<MailMessage> Parse(byte[] mbox, DateTimeOffset cutoff, int maxMessages = 25, TimeZoneInfo? localZone = null)
    {
        localZone ??= TimeZoneInfo.Local;
        var recent = new List<(DateTimeOffset Received, MimeEntity Entity)>();
        foreach (var (envelope, start, end) in Split(mbox))
        {
            var entity = MimeEntity.Parse(mbox.AsMemory(start, end - start));
            if (IsDeleted(entity)) continue;
            var received = ReceivedTime(entity, envelope, localZone);
            if (received is null || received < cutoff) continue;
            recent.Add((received.Value, entity));
        }

        // Only the newest few are decoded; the rest are never looked at.
        return recent
            .OrderByDescending(message => message.Received)
            .Take(maxMessages)
            .Select(message => new MailMessage(
                message.Received,
                message.Entity.Header("From"),
                message.Entity.Header("Subject"),
                message.Entity.BodyText()))
            .ToList();
    }

    /// <summary>
    /// The last <paramref name="maxBytes"/> of a file, starting at a message boundary.
    /// The file is opened so Thunderbird can keep writing to it.
    /// </summary>
    internal static byte[] ReadTail(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[stream.Length - start];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (start == 0) return read == buffer.Length ? buffer : buffer[..read];

        // Starting partway through a message; skip ahead to the next one.
        var span = buffer.AsSpan(0, read);
        var from = span.IndexOf("\nFrom "u8);
        return from < 0 ? [] : span[(from + 1)..].ToArray();
    }

    /// <summary>Finds each message in the file: its "From " line, and where its headers and body sit.</summary>
    internal static List<(string Envelope, int Start, int End)> Split(byte[] mbox)
    {
        var messages = new List<(string Envelope, int Start, int End)>();
        var position = 0;
        var previousWasBlank = true;
        string? envelope = null;
        var messageStart = 0;

        while (position < mbox.Length)
        {
            var newline = Array.IndexOf(mbox, (byte)'\n', position);
            var lineEnd = newline < 0 ? mbox.Length : newline;
            var next = newline < 0 ? mbox.Length : newline + 1;
            var line = mbox.AsSpan(position, lineEnd - position);
            if (!line.IsEmpty && line[^1] == (byte)'\r') line = line[..^1];

            if (line.StartsWith("From "u8) && (previousWasBlank || LooksLikeEnvelope(line)))
            {
                if (envelope is not null) messages.Add((envelope, messageStart, position));
                envelope = Encoding.Latin1.GetString(line);
                messageStart = next;
            }
            previousWasBlank = line.IsEmpty;
            position = next;
        }
        if (envelope is not null) messages.Add((envelope, messageStart, mbox.Length));
        return messages;
    }

    /// <summary>A "From " line that carries a time, like "From - Tue Oct 27 09:59:58 2026", and isn't just a sentence.</summary>
    static bool LooksLikeEnvelope(ReadOnlySpan<byte> line)
    {
        for (var i = 0; i + 4 < line.Length; i++)
        {
            if (IsDigit(line[i]) && IsDigit(line[i + 1]) && line[i + 2] == (byte)':' && IsDigit(line[i + 3]) && IsDigit(line[i + 4])) return true;
        }
        return false;

        static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';
    }

    static bool IsDeleted(MimeEntity entity)
    {
        var status = entity.RawHeader("X-Mozilla-Status");
        return status is not null
            && int.TryParse(status.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags)
            && (flags & ExpungedFlag) != 0;
    }

    /// <summary>
    /// When the message reached the mail server, from its newest Received header. Falls back to
    /// the Date header, then to the time Thunderbird wrote it to the file.
    /// </summary>
    internal static DateTimeOffset? ReceivedTime(MimeEntity entity, string envelope, TimeZoneInfo localZone)
    {
        var received = entity.RawHeader("Received");
        if (received is not null)
        {
            var semicolon = received.LastIndexOf(';');
            if (semicolon >= 0 && MailDate.Parse(received[(semicolon + 1)..]) is DateTimeOffset time) return time;
        }
        if (entity.RawHeader("Date") is string date && MailDate.Parse(date) is DateTimeOffset sent) return sent;
        return MailDate.ParseEnvelope(envelope, localZone);
    }
}
