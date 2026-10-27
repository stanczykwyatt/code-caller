namespace Copy2FA.Core;

/// <summary>The symbol beside a notice's text.</summary>
public enum NoticeSymbol { Progress, Copied, Search, Lock, Mail, Warning }

/// <summary>
/// What the small notice at the top of the screen says after the shortcut is pressed.
/// The wording follows the macOS version.
/// </summary>
public sealed record Notice(string Title, string? Detail, NoticeSymbol Symbol, bool ShowsCode, TimeSpan? Duration)
{
    static readonly TimeSpan Short = TimeSpan.FromSeconds(2);
    static readonly TimeSpan Long = TimeSpan.FromSeconds(3.5);

    public const string Authenticator = "Authenticator";

    /// <summary>Shown while still looking, if that takes long enough to notice. Stays up until replaced.</summary>
    public static Notice Looking() => new("Looking for a code…", null, NoticeSymbol.Progress, false, null);

    /// <summary>A code copied from mail. <paramref name="code"/> is null when codes are hidden in Settings.</summary>
    public static Notice Copied(string? code, string source, string sender, DateTimeOffset received, DateTimeOffset now) =>
        new(code ?? "Code copied", CopiedDetail(code is not null, [source, sender, Age(received, now)]), NoticeSymbol.Copied, code is not null, Short);

    /// <summary>A code copied from the authenticator, and how long it has left.</summary>
    public static Notice CopiedFromAuthenticator(string? code, string account, int secondsLeft) =>
        new(code ?? "Code copied", CopiedDetail(code is not null, [Authenticator, account, $"{secondsLeft}s left"]), NoticeSymbol.Copied, code is not null, Short);

    public static Notice NothingFound(int minutes, IReadOnlyList<string> places) =>
        new("No code found", $"Nothing in the last {minutes} minutes of {JoinPlaces(places)}", NoticeSymbol.Search, false, Long);

    public static Notice NoMailApps() =>
        new("No mail app found", "Copy 2FA reads Outlook (classic) and Thunderbird", NoticeSymbol.Mail, false, Long);

    public static Notice NowhereToLook() =>
        new("Nowhere to look", "Turn on Outlook or Thunderbird in Copy 2FA Settings", NoticeSymbol.Search, false, Long);

    public static Notice ReadFailed(IReadOnlyList<string> places) =>
        new($"Couldn't read {JoinPlaces(places)}", "Try again in a moment", NoticeSymbol.Warning, false, Long);

    public static Notice CopyFailed() =>
        new("Couldn't copy the code", "Another app is using the clipboard. Try again", NoticeSymbol.Warning, false, Long);

    public static Notice ShortcutTaken(string shortcut) =>
        new("Shortcut unavailable", $"Another app is using {shortcut}. Pick a different one in Settings", NoticeSymbol.Warning, false, Long);

    public static Notice NoAccounts() =>
        new("No accounts yet", "Add accounts in Copy 2FA Settings", NoticeSymbol.Lock, false, Long);

    public static Notice HelloUnavailable() =>
        new("Windows Hello is off", "Set up a PIN, face or fingerprint to use the authenticator", NoticeSymbol.Lock, false, Long);

    public static Notice NotVerified() =>
        new("Not verified", "Your accounts stay locked", NoticeSymbol.Lock, false, Long);

    public static Notice VaultUnreadable() =>
        new("Couldn't open your accounts", "They can only be opened by the Windows account that saved them", NoticeSymbol.Warning, false, Long);

    /// <summary>"just now", "1 min ago", "5 min ago".</summary>
    public static string Age(DateTimeOffset received, DateTimeOffset now)
    {
        var minutes = (int)Math.Floor((now - received).TotalMinutes);
        return minutes switch
        {
            < 1 => "just now",
            1 => "1 min ago",
            _ => $"{minutes} min ago",
        };
    }

    /// <summary>"Outlook", "Outlook and Thunderbird".</summary>
    public static string JoinPlaces(IReadOnlyList<string> places) => places.Count switch
    {
        0 => "your mail",
        1 => places[0],
        _ => string.Join(", ", places.Take(places.Count - 1)) + " and " + places[^1],
    };

    static string CopiedDetail(bool showsCode, string[] parts) =>
        (showsCode ? "Copied from " : "From ") + string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
