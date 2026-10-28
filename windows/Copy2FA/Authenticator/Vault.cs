using System.Security.Cryptography;
using Copy2FA.Core.Authenticator;

namespace Copy2FA.Authenticator;

/// <summary>
/// The authenticator's accounts, encrypted with DPAPI so only your Windows account
/// on this PC can read them.
/// </summary>
/// <remarks>
/// Accounts are decrypted only after Windows Hello confirms it's you, kept in memory
/// only while a window needs them, and their secrets are wiped when it closes.
/// </remarks>
internal static class Vault
{
    /// <summary>Ties the encryption to this app, so another program calling DPAPI with no entropy can't read it.</summary>
    static readonly byte[] Entropy = "Copy 2FA authenticator vault, version 1"u8.ToArray();

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Copy2FA", "vault.bin");

    public static bool Exists => File.Exists(FilePath);

    /// <exception cref="CryptographicException">The vault was saved by another Windows account or PC.</exception>
    /// <exception cref="FormatException">The vault is damaged.</exception>
    public static List<OtpAccount> Load()
    {
        if (!Exists) return [];
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return VaultFormat.Deserialize(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static void Save(IReadOnlyList<OtpAccount> accounts)
    {
        var plain = VaultFormat.Serialize(accounts);
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Written beside the vault, then swapped in, so a crash can't leave half a file.
            var temporary = FilePath + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// Makes a change to the vault as it is on disk right now, then saves it. Working from the
    /// saved copy means an older copy held by another window can never undo a change, such as
    /// moving a counter-based account's counter on. The decrypted accounts are wiped afterwards.
    /// </summary>
    /// <returns>Whatever <paramref name="change"/> returns.</returns>
    public static T Update<T>(Func<List<OtpAccount>, T> change)
    {
        var accounts = Load();
        try
        {
            var result = change(accounts);
            Save(accounts);
            return result;
        }
        finally
        {
            Forget(accounts);
        }
    }

    /// <summary>
    /// Makes the next code for a counter-based account and saves its new counter before
    /// returning the code, so a code is never handed out without the counter moving on.
    /// Returns null if the account is gone.
    /// </summary>
    public static string? TakeNextHotpCode(Guid id) =>
        Update(accounts => accounts.Find(account => account.Id == id && account.Kind == OtpKind.Hotp)?.TakeNextHotpCode());

    /// <summary>Wipes the secrets of accounts that are no longer needed in memory.</summary>
    public static void Forget(List<OtpAccount> accounts)
    {
        foreach (var account in accounts) CryptographicOperations.ZeroMemory(account.Secret);
        accounts.Clear();
    }
}
