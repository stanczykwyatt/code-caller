using System.Security.Cryptography;
using Copy2FA.Authenticator;
using Copy2FA.Core.Authenticator;
using Copy2FA.Core.Settings;

namespace Copy2FA;

/// <summary>What the settings window needs from the rest of the app.</summary>
internal interface ISettingsHost
{
    AppSettings Settings { get; }
    /// <summary>Saves the settings after a change.</summary>
    void SaveSettings();
    /// <summary>Registers a new shortcut. Returns false, keeping the old one, if another app has it.</summary>
    bool TrySetShortcut(int id, Shortcut shortcut);
    bool IsShortcutTaken(int id);
    /// <summary>Turns the app's shortcuts off while a new one is being typed, and back on after.</summary>
    void SetRecordingShortcut(bool recording);
    bool IsOutlookAvailable { get; }
    bool IsThunderbirdAvailable { get; }
    /// <summary>Checks Windows Hello, then decrypts the accounts. Null if that didn't work.</summary>
    Task<List<OtpAccount>?> UnlockAccountsAsync(IntPtr owner);
}

/// <summary>Copy 2FA's settings. Changes apply and save straight away.</summary>
internal sealed class SettingsWindow : Form
{
    const long MaxImportBytes = 20 * 1024 * 1024;

    readonly ISettingsHost host;
    readonly TableLayoutPanel layout = new();
    readonly ShortcutBox copyShortcut = new();
    readonly ShortcutBox authenticatorShortcut = new();
    readonly Label copyShortcutTaken = new();
    readonly Label authenticatorShortcutTaken = new();
    readonly Panel lockedPanel = new();
    readonly TableLayoutPanel unlockedPanel = new();
    readonly ListBox accountList = new();
    readonly TextBox linkBox = new();
    readonly Label authenticatorStatus = new();
    List<OtpAccount>? accounts;

    public SettingsWindow(ISettingsHost host)
    {
        this.host = host;
        var settings = host.Settings;

        Text = "Copy 2FA Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Segoe UI", 9f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        ShowIcon = true;
        Icon = AppIcon.Load();

        layout.ColumnCount = 2;
        layout.AutoSize = true;
        layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layout.Dock = DockStyle.Fill;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        Controls.Add(layout);

        // Shortcuts
        AddHeader("Shortcuts", first: true);
        SetUpShortcut(copyShortcut, copyShortcutTaken, HotKeys.CopyLatestCode, settings.CopyShortcut, "Copy latest code");
        SetUpShortcut(authenticatorShortcut, authenticatorShortcutTaken, HotKeys.OpenAuthenticator, settings.AuthenticatorShortcut, "Open authenticator");
        AddNote("Press a shortcut in any app. Click a box, then type a new one.");

        // Mail
        AddHeader("Look for codes in");
        AddSpanning(SourceCheckBox("Outlook (classic)", settings.UseOutlook, host.IsOutlookAvailable, value => settings.UseOutlook = value));
        AddSpanning(SourceCheckBox("Thunderbird", settings.UseThunderbird, host.IsThunderbirdAvailable, value => settings.UseThunderbird = value));
        var lookback = Choices(
            AppSettings.LookbackChoices.Append(settings.LookbackMinutes).Distinct().Order().ToArray(),
            settings.LookbackMinutes,
            minutes => $"{minutes} minutes",
            minutes => settings.LookbackMinutes = minutes);
        AddRow("Look back", lookback);

        // Privacy
        AddHeader("Privacy");
        var clearChoices = AppSettings.ClearClipboardChoices.Contains(settings.ClearClipboardSeconds)
            ? AppSettings.ClearClipboardChoices
            : [.. AppSettings.ClearClipboardChoices, settings.ClearClipboardSeconds];
        AddRow("Clear clipboard after", Choices(clearChoices, settings.ClearClipboardSeconds, DescribeDelay, seconds => settings.ClearClipboardSeconds = seconds));
        AddSpanning(Toggle("Show the code in the notice", settings.ShowCodeInNotice, value => settings.ShowCodeInNotice = value));
        var startWithWindows = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = StartupRegistration.IsEnabled };
        startWithWindows.CheckedChanged += (_, _) =>
        {
            try
            {
                StartupRegistration.SetEnabled(startWithWindows.Checked);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                System.Media.SystemSounds.Beep.Play();
            }
        };
        AddSpanning(startWithWindows);

        // Authenticator
        AddHeader("Authenticator");
        BuildLockedPanel();
        BuildUnlockedPanel();
        AddSpanning(lockedPanel);
        AddSpanning(unlockedPanel);
        unlockedPanel.Visible = false;

        var footer = new Label
        {
            Text = "Copy 2FA has no internet access. It only reads your mail when you press the shortcut, and never saves anything it reads.",
            AutoSize = true,
            MaximumSize = new Size(490, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 18, 0, 0),
        };
        AddSpanning(footer);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Switching away mid-recording would leave the app's shortcuts off.
        copyShortcut.StopRecording();
        authenticatorShortcut.StopRecording();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        copyShortcut.StopRecording();
        authenticatorShortcut.StopRecording();
        Lock();
    }

    // Building rows

    void AddHeader(string text, bool first = false)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5f),
            Margin = new Padding(0, first ? 0 : 16, 0, 6),
        };
        AddSpanning(label);
    }

    void AddNote(string text) =>
        AddSpanning(new Label { Text = text, AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 0) });

    void AddRow(string label, Control control)
    {
        var row = layout.RowCount++;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    void AddSpanning(Control control)
    {
        var row = layout.RowCount++;
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, 2);
    }

    void SetUpShortcut(ShortcutBox box, Label taken, int id, Shortcut shortcut, string label)
    {
        box.Shortcut = shortcut;
        box.Width = 160;
        box.Height = 28;
        box.AccessibleName = label + " shortcut";
        box.RecordingChanged += host.SetRecordingShortcut;
        box.Recorded += candidate =>
        {
            var other = id == HotKeys.CopyLatestCode ? host.Settings.AuthenticatorShortcut : host.Settings.CopyShortcut;
            if (candidate == other || !host.TrySetShortcut(id, candidate))
            {
                System.Media.SystemSounds.Beep.Play();
                box.Shortcut = id == HotKeys.CopyLatestCode ? host.Settings.CopyShortcut : host.Settings.AuthenticatorShortcut;
                taken.Text = candidate == other ? "Already used by the other shortcut" : "Another app is using that";
                taken.Visible = true;
                return;
            }
            taken.Visible = host.IsShortcutTaken(id);
            taken.Text = "Another app is using this";
        };

        taken.AutoSize = true;
        taken.ForeColor = Color.FromArgb(0x9D, 0x5D, 0x00);
        taken.Text = "Another app is using this";
        taken.Visible = host.IsShortcutTaken(id);
        taken.Margin = new Padding(8, 7, 0, 0);

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(box);
        row.Controls.Add(taken);
        AddRow(label, row);
    }

    CheckBox Toggle(string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Text = text, AutoSize = true, Checked = value, Margin = new Padding(0, 4, 0, 4) };
        box.CheckedChanged += (_, _) =>
        {
            set(box.Checked);
            host.SaveSettings();
        };
        return box;
    }

    Control SourceCheckBox(string name, bool value, bool available, Action<bool> set)
    {
        var box = Toggle(name, value, set);
        if (available) return box;
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(box);
        row.Controls.Add(new Label { Text = "Not set up on this PC", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 7, 0, 0) });
        return row;
    }

    ComboBox Choices(int[] values, int current, Func<int, string> describe, Action<int> set)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        foreach (var value in values) box.Items.Add(new Choice(value, describe(value)));
        box.SelectedIndex = Array.IndexOf(values, current);
        box.SelectedIndexChanged += (_, _) =>
        {
            if (box.SelectedItem is not Choice choice) return;
            set(choice.Value);
            host.SaveSettings();
        };
        return box;
    }

    static string DescribeDelay(int seconds) => seconds switch
    {
        0 => "Never",
        < 60 => $"{seconds} seconds",
        60 => "1 minute",
        _ when seconds % 60 == 0 => $"{seconds / 60} minutes",
        _ => $"{seconds} seconds",
    };

    sealed record Choice(int Value, string Text)
    {
        public override string ToString() => Text;
    }

    // Authenticator

    void BuildLockedPanel()
    {
        lockedPanel.AutoSize = true;
        lockedPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        lockedPanel.Margin = Padding.Empty;
        var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.Add(new Label
        {
            Text = "Your accounts are encrypted for your Windows account. Unlock them with Windows Hello to see, add or delete them.",
            AutoSize = true,
            MaximumSize = new Size(490, 0),
            Margin = new Padding(0, 0, 0, 8),
        });
        var unlock = new Button { Text = "Unlock with Windows Hello…", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        unlock.Click += async (_, _) =>
        {
            unlock.Enabled = false;
            try
            {
                await UnlockAsync();
            }
            catch (Exception)
            {
                System.Media.SystemSounds.Beep.Play();
            }
            finally
            {
                unlock.Enabled = true;
            }
        };
        flow.Controls.Add(unlock);
        lockedPanel.Controls.Add(flow);
    }

    void BuildUnlockedPanel()
    {
        unlockedPanel.ColumnCount = 2;
        unlockedPanel.AutoSize = true;
        unlockedPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        unlockedPanel.Margin = Padding.Empty;
        unlockedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        unlockedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        accountList.Width = 372;
        accountList.Height = 150;
        accountList.IntegralHeight = false;
        accountList.AccessibleName = "Accounts";
        accountList.Margin = new Padding(0, 0, 8, 0);
        unlockedPanel.Controls.Add(accountList, 0, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        var import = new Button { Text = "Import…", AutoSize = true, MinimumSize = new Size(100, 0) };
        import.Click += (_, _) => ImportFromFile();
        var delete = new Button { Text = "Delete", AutoSize = true, MinimumSize = new Size(100, 0) };
        delete.Click += (_, _) => DeleteSelected();
        buttons.Controls.Add(import);
        buttons.Controls.Add(delete);
        unlockedPanel.Controls.Add(buttons, 1, 0);

        linkBox.Width = 372;
        linkBox.PlaceholderText = "Paste an otpauth:// link";
        linkBox.AccessibleName = "otpauth link";
        // The link holds the secret, so it's masked like a password.
        linkBox.UseSystemPasswordChar = true;
        linkBox.Margin = new Padding(0, 10, 8, 0);
        unlockedPanel.Controls.Add(linkBox, 0, 1);
        var add = new Button { Text = "Add", AutoSize = true, MinimumSize = new Size(100, 0), Margin = new Padding(3, 9, 3, 0) };
        add.Click += (_, _) => AddLink();
        unlockedPanel.Controls.Add(add, 1, 1);

        authenticatorStatus.AutoSize = true;
        authenticatorStatus.MaximumSize = new Size(490, 0);
        authenticatorStatus.Margin = new Padding(0, 8, 0, 0);
        unlockedPanel.Controls.Add(authenticatorStatus, 0, 2);
        unlockedPanel.SetColumnSpan(authenticatorStatus, 2);
        var formats = new Label
        {
            Text = "Import from a list of otpauth:// links, a Google Authenticator export, a Safari or Apple Passwords CSV, or an unencrypted Bitwarden, Aegis or 2FAS file. Passwords in those files are skipped and never kept.",
            AutoSize = true,
            MaximumSize = new Size(490, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 6, 0, 0),
        };
        unlockedPanel.Controls.Add(formats, 0, 3);
        unlockedPanel.SetColumnSpan(formats, 2);
    }

    async Task UnlockAsync()
    {
        var unlocked = await host.UnlockAccountsAsync(Handle);
        if (unlocked is null || IsDisposed)
        {
            if (unlocked is not null) Vault.Forget(unlocked);
            return;
        }
        accounts = unlocked;
        // Account names stay out of screenshots and screen sharing while they're showing.
        Native.ExcludeFromCapture(Handle);
        lockedPanel.Visible = false;
        unlockedPanel.Visible = true;
        authenticatorStatus.Text = accounts.Count == 0 ? "No accounts yet." : "";
        RefreshAccounts();
    }

    void Lock()
    {
        if (accounts is not null) Vault.Forget(accounts);
        accounts = null;
        accountList.Items.Clear();
    }

    void RefreshAccounts()
    {
        accountList.BeginUpdate();
        accountList.Items.Clear();
        foreach (var account in accounts ?? []) accountList.Items.Add(new AccountRow(account));
        accountList.EndUpdate();
    }

    void ImportFromFile()
    {
        if (accounts is null) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Import accounts",
            Filter = "Authenticator exports (*.txt;*.csv;*.json)|*.txt;*.csv;*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        byte[]? file = null;
        try
        {
            if (new FileInfo(dialog.FileName).Length > MaxImportBytes)
            {
                authenticatorStatus.Text = "That file is too large to be an authenticator export.";
                return;
            }
            file = File.ReadAllBytes(dialog.FileName);
            var result = AccountImporter.Import(file);
            Save(result, fromFile: true);
        }
        catch (ImportException problem)
        {
            authenticatorStatus.Text = problem.Message;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            authenticatorStatus.Text = "That file couldn't be opened.";
        }
        finally
        {
            // The export may hold passwords; don't leave its bytes lying around in memory.
            if (file is not null) CryptographicOperations.ZeroMemory(file);
        }
    }

    void AddLink()
    {
        if (accounts is null || linkBox.Text.Trim().Length == 0) return;
        try
        {
            Save(AccountImporter.ImportLinks(linkBox.Text), fromFile: false);
            linkBox.Clear();
        }
        catch (ImportException problem)
        {
            authenticatorStatus.Text = problem.Message;
        }
    }

    void Save(ImportResult result, bool fromFile)
    {
        if (accounts is null) return;
        int added;
        try
        {
            added = Vault.Update(saved => OtpAccount.Merge(saved, result.Accounts));
            Reload();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
        {
            authenticatorStatus.Text = "Your accounts couldn't be saved.";
            return;
        }

        var message = added switch
        {
            0 => "Those accounts were already here.",
            1 => "Added 1 account.",
            _ => $"Added {added} accounts.",
        };
        if (result.Skipped > 0)
        {
            message += result.Skipped == 1
                ? " Skipped 1 that uses a kind of code Copy 2FA doesn't support, such as Steam, or couldn't be read."
                : $" Skipped {result.Skipped} that use a kind of code Copy 2FA doesn't support, such as Steam, or couldn't be read.";
        }
        if (fromFile) message += " The export file isn't encrypted, so delete it now that it's imported.";
        authenticatorStatus.Text = message;
    }

    void DeleteSelected()
    {
        if (accounts is null || accountList.SelectedItem is not AccountRow row) return;
        var answer = MessageBox.Show(
            this,
            $"Delete {row.Account.Title}? You won't get codes for it from Copy 2FA any more, so make sure you can still sign in another way.",
            "Delete account",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        var title = row.Account.Title;
        var id = row.Account.Id;
        try
        {
            Vault.Update(saved =>
            {
                var removed = saved.FindAll(account => account.Id == id);
                saved.RemoveAll(account => account.Id == id);
                Vault.Forget(removed);
                return removed.Count;
            });
            Reload();
            authenticatorStatus.Text = $"Deleted {title}.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or FormatException)
        {
            authenticatorStatus.Text = "Your accounts couldn't be saved.";
        }
    }

    /// <summary>Replaces the accounts on show with the vault as it's now saved.</summary>
    void Reload()
    {
        var fresh = Vault.Load();
        if (accounts is not null) Vault.Forget(accounts);
        accounts = fresh;
        RefreshAccounts();
    }

    sealed class AccountRow(OtpAccount account)
    {
        public OtpAccount Account { get; } = account;

        public override string ToString() =>
            Account.Subtitle.Length > 0 ? $"{Account.Title}  ({Account.Subtitle})" : Account.Title;
    }
}
