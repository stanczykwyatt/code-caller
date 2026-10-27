namespace CodeCaller.Core.Mail;

/// <summary>
/// A message read from a mail app. It only lives as long as it takes to look for a
/// code in it, and is never logged or written anywhere.
/// </summary>
public sealed record MailMessage(DateTimeOffset Received, string Sender, string Subject, string Body);
