using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeCaller.Core.Settings;

/// <summary>
/// Everything Code Caller remembers between runs. None of it is sensitive: no codes,
/// no messages, no account names. Starting with Windows lives in the registry instead.
/// </summary>
public sealed class AppSettings
{
    public static readonly int[] LookbackChoices = [5, 10, 15, 30];
    /// <summary>Zero means the clipboard is never cleared.</summary>
    public static readonly int[] ClearClipboardChoices = [30, 60, 120, 0];

    public Shortcut CopyShortcut { get; set; } = Shortcut.DefaultCopy;
    public Shortcut AuthenticatorShortcut { get; set; } = Shortcut.DefaultAuthenticator;
    public int LookbackMinutes { get; set; } = 10;
    public int ClearClipboardSeconds { get; set; } = 60;
    public bool ShowCodeInNotice { get; set; } = true;
    public bool UseOutlook { get; set; } = true;
    public bool UseThunderbird { get; set; } = true;
    public bool HasSeenWelcome { get; set; }

    /// <summary>Puts any value edited by hand, or left by an older version, back in range.</summary>
    public void Normalize()
    {
        if (!CopyShortcut.IsValid) CopyShortcut = Shortcut.DefaultCopy;
        if (!AuthenticatorShortcut.IsValid) AuthenticatorShortcut = Shortcut.DefaultAuthenticator;
        if (AuthenticatorShortcut == CopyShortcut)
        {
            AuthenticatorShortcut = CopyShortcut == Shortcut.DefaultAuthenticator ? Shortcut.DefaultCopy : Shortcut.DefaultAuthenticator;
        }
        LookbackMinutes = Math.Clamp(LookbackMinutes, 1, 60);
        ClearClipboardSeconds = Math.Clamp(ClearClipboardSeconds, 0, 600);
    }

    public AppSettings Copy() => (AppSettings)MemberwiseClone();
}

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON.</summary>
public static class SettingsStore
{
    public static AppSettings Load(string path)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path)) settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged or unreadable file just means starting over with the defaults.
        }
        settings ??= new AppSettings();
        settings.Normalize();
        return settings;
    }

    /// <summary>Writes the file in one step, so a crash halfway never leaves it damaged.</summary>
    public static void Save(AppSettings settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings));
        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
