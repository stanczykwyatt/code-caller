using Copy2FA.Core.Mail;

namespace Copy2FA.Mail;

/// <summary>
/// Reads Thunderbird's mail files directly. Thunderbird keeps each folder as an mbox
/// file in your profile, so nothing has to be running and nothing is sent anywhere.
/// </summary>
internal sealed class ThunderbirdSource : IMailSource
{
    /// <summary>Allows for a server clock that runs a little ahead of this PC's.</summary>
    static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(5);

    static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thunderbird");

    public string Name => "Thunderbird";

    public bool IsAvailable => File.Exists(Path.Combine(Directory, "profiles.ini"));

    public Task<IReadOnlyList<MailMessage>> ReadRecentAsync(DateTimeOffset cutoff, CancellationToken cancellation) =>
        Task.Run<IReadOnlyList<MailMessage>>(() =>
        {
            var messages = new List<MailMessage>();
            foreach (var file in ThunderbirdProfiles.FindMailboxes(Directory))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    // New mail is appended, so a file untouched since the cutoff has nothing new in it.
                    if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime - ClockSlack) continue;
                    messages.AddRange(MboxReader.ReadRecent(file, cutoff));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Being compacted or locked right now; the next look will get it.
                }
            }
            messages.Sort((a, b) => b.Received.CompareTo(a.Received));
            return messages;
        }, cancellation);
}
