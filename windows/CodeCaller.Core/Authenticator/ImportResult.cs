namespace CodeCaller.Core.Authenticator;

/// <summary>
/// The accounts read from an import, and how many entries were left out because they
/// aren't time-based codes (such as HOTP counters or Steam codes) or couldn't be read.
/// </summary>
public sealed record ImportResult(IReadOnlyList<OtpAccount> Accounts, int Skipped)
{
    public static ImportResult Combine(IEnumerable<ImportResult> results)
    {
        var accounts = new List<OtpAccount>();
        var skipped = 0;
        foreach (var result in results)
        {
            accounts.AddRange(result.Accounts);
            skipped += result.Skipped;
        }
        return new ImportResult(accounts, skipped);
    }
}
