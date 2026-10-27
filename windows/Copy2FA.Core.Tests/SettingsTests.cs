using Copy2FA.Core.Settings;

namespace Copy2FA.Core.Tests;

public class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Shift+C", ShortcutModifiers.Control | ShortcutModifiers.Shift, 'C')]
    [InlineData("alt + win + 7", ShortcutModifiers.Alt | ShortcutModifiers.Windows, '7')]
    [InlineData("Control+F12", ShortcutModifiers.Control, 0x7B)]
    [InlineData("F9", ShortcutModifiers.None, 0x78)]
    [InlineData("Ctrl+Alt+Space", ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x20)]
    public void ParsesShortcuts(string text, ShortcutModifiers modifiers, int key)
    {
        Assert.True(Shortcut.TryParse(text, out var shortcut));
        Assert.Equal(new Shortcut(modifiers, key), shortcut);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("Shift+C")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Hyper+C")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+F25")]
    public void RejectsShortcutsThatWouldBlockTyping(string text)
    {
        Assert.False(Shortcut.TryParse(text, out _));
    }

    [Fact]
    public void DisplaysInWindowsOrder()
    {
        Assert.Equal("Ctrl+Shift+C", Shortcut.DefaultCopy.ToString());
        Assert.Equal("Ctrl+Alt+Shift+Win+F5", new Shortcut(ShortcutModifiers.Windows | ShortcutModifiers.Shift | ShortcutModifiers.Alt | ShortcutModifiers.Control, 0x74).ToString());
        Assert.Equal("Ctrl+PageDown", new Shortcut(ShortcutModifiers.Control, 0x22).ToString());
    }
}

public class SettingsStoreTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "cc-settings-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void DefaultsMatchTheMacVersion()
    {
        var settings = SettingsStore.Load(FilePath);
        Assert.Equal(Shortcut.DefaultCopy, settings.CopyShortcut);
        Assert.Equal(Shortcut.DefaultAuthenticator, settings.AuthenticatorShortcut);
        Assert.Equal(10, settings.LookbackMinutes);
        Assert.Equal(60, settings.ClearClipboardSeconds);
        Assert.True(settings.ShowCodeInNotice);
        Assert.True(settings.UseOutlook);
        Assert.True(settings.UseThunderbird);
        Assert.False(settings.HasSeenWelcome);
    }

    [Fact]
    public void RoundTrips()
    {
        var settings = new AppSettings
        {
            CopyShortcut = new Shortcut(ShortcutModifiers.Alt | ShortcutModifiers.Shift, 'V'),
            LookbackMinutes = 30,
            ClearClipboardSeconds = 0,
            ShowCodeInNotice = false,
            UseOutlook = false,
            HasSeenWelcome = true,
        };
        SettingsStore.Save(settings, FilePath);

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"copyShortcut\": \"Alt+Shift+V\"", json, StringComparison.Ordinal);

        var loaded = SettingsStore.Load(FilePath);
        Assert.Equal(settings.CopyShortcut, loaded.CopyShortcut);
        Assert.Equal(30, loaded.LookbackMinutes);
        Assert.Equal(0, loaded.ClearClipboardSeconds);
        Assert.False(loaded.ShowCodeInNotice);
        Assert.False(loaded.UseOutlook);
        Assert.True(loaded.UseThunderbird);
        Assert.True(loaded.HasSeenWelcome);
    }

    [Fact]
    public void RepairsValuesEditedByHand()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, """
            { "copyShortcut": "C", "authenticatorShortcut": "Ctrl+Shift+C", "lookbackMinutes": 9999, "clearClipboardSeconds": -5 }
            """);
        var settings = SettingsStore.Load(FilePath);
        Assert.Equal(Shortcut.DefaultCopy, settings.CopyShortcut);
        Assert.Equal(Shortcut.DefaultAuthenticator, settings.AuthenticatorShortcut);
        Assert.Equal(60, settings.LookbackMinutes);
        Assert.Equal(0, settings.ClearClipboardSeconds);
    }

    [Fact]
    public void DamagedFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Equal(10, SettingsStore.Load(FilePath).LookbackMinutes);
    }
}

public class NoticeTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 27, 16, 45, 0, TimeSpan.Zero);

    [Fact]
    public void CopiedFromMail()
    {
        var notice = Notice.Copied("482913", "Outlook", "GitHub", Now.AddSeconds(-20), Now);
        Assert.Equal("482913", notice.Title);
        Assert.Equal("Copied from Outlook · GitHub · just now", notice.Detail);
        Assert.True(notice.ShowsCode);
        Assert.Equal(TimeSpan.FromSeconds(2), notice.Duration);
    }

    [Fact]
    public void CopiedWithTheCodeHidden()
    {
        var notice = Notice.Copied(null, "Thunderbird", "google.com", Now.AddMinutes(-3), Now);
        Assert.Equal("Code copied", notice.Title);
        Assert.Equal("From Thunderbird · google.com · 3 min ago", notice.Detail);
        Assert.False(notice.ShowsCode);
    }

    [Fact]
    public void CopiedFromTheAuthenticator()
    {
        Assert.Equal("Copied from Authenticator · GitHub · 18s left", Notice.CopiedFromAuthenticator("123456", "GitHub", 18).Detail);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(150, "2 min ago")]
    [InlineData(-30, "just now")]
    public void Ages(int secondsAgo, string expected)
    {
        Assert.Equal(expected, Notice.Age(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void NothingFoundNamesThePlacesSearched()
    {
        Assert.Equal("Nothing in the last 10 minutes of Outlook and Thunderbird", Notice.NothingFound(10, ["Outlook", "Thunderbird"]).Detail);
        Assert.Equal("Nothing in the last 5 minutes of Thunderbird", Notice.NothingFound(5, ["Thunderbird"]).Detail);
        Assert.Null(Notice.Looking().Duration);
    }
}
