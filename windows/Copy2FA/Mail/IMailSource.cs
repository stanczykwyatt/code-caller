using Copy2FA.Core.Mail;

namespace Copy2FA.Mail;

/// <summary>A mail app Code Caller can read recent messages from.</summary>
internal interface IMailSource
{
    /// <summary>The app's name, as shown in notices.</summary>
    string Name { get; }

    /// <summary>Whether the app is installed and set up on this PC.</summary>
    bool IsAvailable { get; }

    /// <summary>Inbox and junk messages received at or after <paramref name="cutoff"/>, newest first.</summary>
    Task<IReadOnlyList<MailMessage>> ReadRecentAsync(DateTimeOffset cutoff, CancellationToken cancellation);
}
