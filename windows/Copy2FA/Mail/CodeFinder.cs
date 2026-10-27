using Copy2FA.Core;
using Copy2FA.Core.Mail;
using Copy2FA.Core.Settings;

namespace Copy2FA.Mail;

internal sealed record FoundCode(string Code, string Source, string Sender, DateTimeOffset Received);

internal enum LookupStatus { Found, NothingFound, NowhereToLook, NoMailApps, Failed }

internal sealed record LookupResult(LookupStatus Status, FoundCode? Found, IReadOnlyList<string> Places);

/// <summary>Finds the newest verification code across the mail apps that are turned on.</summary>
internal sealed class CodeFinder(IReadOnlyList<IMailSource> sources)
{
    /// <summary>
    /// If no code is there yet it may still be on its way, so look again a few times
    /// over about six seconds before giving up.
    /// </summary>
    static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.5)];

    public async Task<LookupResult> FindLatestAsync(AppSettings settings, CancellationToken cancellation)
    {
        var enabled = sources.Where(source => source.Name switch
        {
            "Outlook" => settings.UseOutlook,
            "Thunderbird" => settings.UseThunderbird,
            _ => true,
        }).ToList();
        if (enabled.Count == 0) return new LookupResult(LookupStatus.NowhereToLook, null, []);

        var available = enabled.Where(source => source.IsAvailable).ToList();
        if (available.Count == 0) return new LookupResult(LookupStatus.NoMailApps, null, []);
        var places = available.Select(source => source.Name).ToList();
        var lookback = TimeSpan.FromMinutes(settings.LookbackMinutes);

        for (var attempt = 0; ; attempt++)
        {
            var (found, failed) = await SearchOnceAsync(available, DateTimeOffset.Now - lookback, cancellation);
            if (found is not null) return new LookupResult(LookupStatus.Found, found, places);
            if (failed.Count == available.Count) return new LookupResult(LookupStatus.Failed, null, failed);
            if (attempt == RetryDelays.Length)
            {
                // Saying a source was searched when it couldn't be read would be misleading.
                return failed.Count > 0
                    ? new LookupResult(LookupStatus.Failed, null, failed)
                    : new LookupResult(LookupStatus.NothingFound, null, places);
            }
            await Task.Delay(RetryDelays[attempt], cancellation);
        }
    }

    /// <summary>
    /// Reads every source at once, then checks messages newest first. Messages only live
    /// in this method's variables and are gone once it returns.
    /// </summary>
    static async Task<(FoundCode? Found, List<string> Failed)> SearchOnceAsync(
        List<IMailSource> sources, DateTimeOffset cutoff, CancellationToken cancellation)
    {
        var reads = sources.Select(source => (Source: source, Task: source.ReadRecentAsync(cutoff, cancellation))).ToList();
        var messages = new List<(string Source, MailMessage Message)>();
        var failed = new List<string>();
        foreach (var (source, task) in reads)
        {
            try
            {
                messages.AddRange((await task).Select(message => (source.Name, message)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Nothing about the failure is kept; it may describe a message.
                failed.Add(source.Name);
            }
        }

        foreach (var (source, message) in messages.OrderByDescending(entry => entry.Message.Received))
        {
            if (CodeExtractor.Code(message.Subject, message.Body) is string code)
            {
                return (new FoundCode(code, source, SenderName.Display(message.Sender), message.Received), failed);
            }
        }
        return (null, failed);
    }
}
