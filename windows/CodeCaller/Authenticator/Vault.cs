using System.Security.Cryptography;
using CodeCaller.Core.Authenticator;

namespace CodeCaller.Authenticator;

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
    static readonly byte[] Entropy = "Code Caller authenticator vault, version 1"u8.ToArray();

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeCaller", "vault.bin");

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

    /// <summary>Wipes the secrets of accounts that are no longer needed in memory.</summary>
    public static void Forget(List<OtpAccount> accounts)
    {
        foreach (var account in accounts) CryptographicOperations.ZeroMemory(account.Secret);
        accounts.Clear();
    }
}
