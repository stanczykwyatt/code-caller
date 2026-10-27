using System.Reflection;
using System.Security.Cryptography;
using CodeCaller.Authenticator;
using CodeCaller.Core;
using CodeCaller.Core.Authenticator;
using CodeCaller.Core.Settings;
using CodeCaller.Mail;
using Microsoft.Win32;

namespace CodeCaller;

/// <summary>The app itself: a tray icon, two global shortcuts, and the windows they open.</summary>
internal sealed class TrayApp : ApplicationContext
{
    static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeCaller", "settings.json");

    readonly NoticeWindow notice = new();
    readonly HotKeys hotKeys = new();
    readonly SecureClipboard clipboard;
    readonly NotifyIcon trayIcon;
    readonly ToolStripMenuItem copyItem;
    readonly ToolStripMenuItem authenticatorItem;
    readonly OutlookSource outlook = new();
    readonly ThunderbirdSource thunderbird = new();
    readonly CodeFinder finder;
    readonly HashSet<int> takenShortcuts = [];
    readonly System.Windows.Forms.Timer progressDelay = new() { Interval = 400 };
    bool isLooking;
    bool isUnlocking;
    PickerWindow? picker;

    public TrayApp()
    {
        Settings = SettingsStore.Load(SettingsPath);
        clipboard = new SecureClipboard(hotKeys.Handle);
        finder = new CodeFinder([outlook, thunderbird]);
        hotKeys.Pressed += id =>
        {
            if (id == HotKeys.CopyLatestCode) CopyLatestCode();
            else if (id == HotKeys.OpenAuthenticator) OpenAuthenticator();
        };
        progressDelay.Tick += (_, _) =>
        {
            progressDelay.Stop();
            if (isLooking) notice.Present(Notice.Looking());
        };

        copyItem = new ToolStripMenuItem("Copy Latest Code", null, (_, _) => CopyLatestCode());
        authenticatorItem = new ToolStripMenuItem("Authenticator…", null, (_, _) => OpenAuthenticator());
        var menu = new ContextMenuStrip();
        menu.Items.Add(copyItem);
        menu.Items.Add(authenticatorItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));

        trayIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            Text = "Code Caller",
            ContextMenuStrip = menu,
            Visible = true,
        };
        // A left click opens the same menu as a right click, like the macOS menu bar icon.
        trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowMenu();
        };

        RegisterShortcuts();
        try
        {
            StartupRegistration.RefreshPath();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Not worth bothering anyone about; the setting can be turned on again in Settings.
        }

        // Codes shouldn't outlive the app, whether it quits or Windows signs out.
        SystemEvents.SessionEnding += OnSessionEnding;

        if (!Settings.HasSeenWelcome)
        {
            Settings.HasSeenWelcome = true;
            SaveSettings();
            trayIcon.ShowBalloonTip(
                10_000,
                "Code Caller is running",
                $"Press {Settings.CopyShortcut} in any app to copy your newest code. Code Caller lives here in the system tray.",
                ToolTipIcon.None);
        }
        else if (takenShortcuts.Contains(HotKeys.CopyLatestCode))
        {
            notice.Present(Notice.ShortcutTaken(Settings.CopyShortcut.ToString()));
        }
    }

    public AppSettings Settings { get; }

    // Copying codes from mail

    async void CopyLatestCode()
    {
        // Pressing the shortcut again while a search is running does nothing.
        if (isLooking) return;
        isLooking = true;
        // Only show progress if the search takes long enough to notice.
        progressDelay.Start();
        var options = Settings.Copy();
        LookupResult result;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            result = await finder.FindLatestAsync(options, timeout.Token);
        }
        catch (Exception)
        {
            // Timed out, or something unexpected. Nothing about it is kept, since it may describe a message.
            result = new LookupResult(LookupStatus.Failed, null, ["your mail"]);
        }
        finally
        {
            progressDelay.Stop();
            isLooking = false;
        }
        Show(result, options);
    }

    void Show(LookupResult result, AppSettings options)
    {
        switch (result.Status)
        {
            case LookupStatus.Found when result.Found is { } found:
                notice.Present(clipboard.Copy(found.Code, options.ClearClipboardSeconds)
                    ? Notice.Copied(options.ShowCodeInNotice ? found.Code : null, found.Source, found.Sender, found.Received, DateTimeOffset.Now)
                    : Notice.CopyFailed());
                break;
            case LookupStatus.NothingFound:
                notice.Present(Notice.NothingFound(options.LookbackMinutes, result.Places));
                break;
            case LookupStatus.NowhereToLook:
                notice.Present(Notice.NowhereToLook());
                break;
            case LookupStatus.NoMailApps:
                notice.Present(Notice.NoMailApps());
                break;
            default:
                notice.Present(Notice.ReadFailed(result.Places));
                break;
        }
    }

    // Authenticator

    async void OpenAuthenticator()
    {
        if (picker is not null)
        {
            picker.Activate();
            return;
        }
        if (!Vault.Exists)
        {
            notice.Present(Notice.NoAccounts());
            return;
        }

        List<OtpAccount>? accounts;
        try
        {
            accounts = await UnlockAccountsAsync(hotKeys.Handle);
        }
        catch (Exception)
        {
            notice.Present(Notice.VaultUnreadable());
            return;
        }
        if (accounts is null) return;
        if (accounts.Count == 0)
        {
            notice.Present(Notice.NoAccounts());
            return;
        }
        picker = new PickerWindow(accounts);
        picker.Chosen += CopyFromAuthenticator;
        picker.FormClosed += (_, _) => picker = null;
        picker.Show();
    }

    void CopyFromAuthenticator(OtpAccount account)
    {
        var now = DateTimeOffset.UtcNow;
        var code = Totp.Code(account, now);
        if (!clipboard.Copy(code, Settings.ClearClipboardSeconds))
        {
            notice.Present(Notice.CopyFailed());
            return;
        }
        notice.Present(Notice.CopiedFromAuthenticator(
            Settings.ShowCodeInNotice ? code : null,
            account.Title,
            Totp.SecondsRemaining(account.Period, now)));
    }

    async Task<List<OtpAccount>?> UnlockAccountsAsync(IntPtr owner)
    {
        if (isUnlocking) return null;
        isUnlocking = true;
        try
        {
            switch (await WindowsHello.VerifyAsync(owner, "Unlock your Code Caller accounts"))
            {
                case HelloResult.Unavailable:
                    notice.Present(Notice.HelloUnavailable());
                    return null;
                case HelloResult.Failed:
                    notice.Present(Notice.NotVerified());
                    return null;
                case HelloResult.Canceled:
                    return null;
            }
            try
            {
                return Vault.Load();
            }
            catch (Exception error) when (error is CryptographicException or FormatException or IOException or UnauthorizedAccessException)
            {
                notice.Present(Notice.VaultUnreadable());
                return null;
            }
        }
        finally
        {
            isUnlocking = false;
        }
    }

    // Shortcuts

    void RegisterShortcuts()
    {
        takenShortcuts.Clear();
        if (!hotKeys.Register(HotKeys.CopyLatestCode, Settings.CopyShortcut)) takenShortcuts.Add(HotKeys.CopyLatestCode);
        if (!hotKeys.Register(HotKeys.OpenAuthenticator, Settings.AuthenticatorShortcut)) takenShortcuts.Add(HotKeys.OpenAuthenticator);
        copyItem.ShortcutKeyDisplayString = Settings.CopyShortcut.ToString();
        authenticatorItem.ShortcutKeyDisplayString = Settings.AuthenticatorShortcut.ToString();
    }

    // The menu

    void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings, SettingsPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The change still applies until Code Caller quits.
        }
    }

    void ShowMenu()
    {
        // NotifyIcon only opens its menu on a right click by itself; this is the same method it uses.
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(trayIcon, null);
    }

    void OnSessionEnding(object? sender, SessionEndingEventArgs e) => clipboard.ClearIfUnchanged();

    void Quit()
    {
        clipboard.ClearIfUnchanged();
        trayIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            picker?.Close();
            clipboard.Dispose();
            hotKeys.Dispose();
            trayIcon.Dispose();
            notice.Dispose();
            progressDelay.Dispose();
        }
        base.Dispose(disposing);
    }
}
