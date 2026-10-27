using Copy2FA.Core.Authenticator;

namespace Copy2FA.Authenticator;

/// <summary>
/// A small search window for the authenticator: type to filter, arrow keys to move,
/// Enter to copy, Esc to close. It closes as soon as it loses focus.
/// </summary>
internal sealed class PickerWindow : Form
{
    static readonly bool IsWindows11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    readonly List<OtpAccount> accounts;
    readonly Theme theme = Theme.Current;
    readonly TextBox search = new();
    readonly ListBox list = new();
    readonly Label empty = new();
    Font titleFont = new("Segoe UI Semibold", 14f, GraphicsUnit.Pixel);
    Font subtitleFont = new("Segoe UI", 12f, GraphicsUnit.Pixel);
    bool closing;

    /// <param name="accounts">Decrypted accounts. The window wipes their secrets when it closes.</param>
    public PickerWindow(List<OtpAccount> accounts)
    {
        this.accounts = accounts;

        Text = "Copy 2FA Authenticator";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(420, 330);
        BackColor = theme.Background;
        ForeColor = theme.Text;
        Padding = new Padding(10);

        search.BorderStyle = BorderStyle.None;
        search.Font = new Font("Segoe UI", 12f);
        search.PlaceholderText = "Search accounts";
        search.BackColor = theme.Background;
        search.ForeColor = theme.Text;
        search.Dock = DockStyle.Top;
        search.AccessibleName = "Search accounts";
        search.TextChanged += (_, _) => Filter();

        var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = theme.Border };
        var gap = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = theme.Background };
        var gapBelow = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = theme.Background };

        list.Dock = DockStyle.Fill;
        list.BorderStyle = BorderStyle.None;
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.IntegralHeight = false;
        list.BackColor = theme.Background;
        list.ForeColor = theme.Text;
        list.AccessibleName = "Accounts";
        list.DrawItem += DrawItem;
        list.DoubleClick += (_, _) => Choose();

        empty.Dock = DockStyle.Fill;
        empty.Text = "No accounts match";
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.ForeColor = theme.SecondaryText;
        empty.Visible = false;

        Controls.Add(list);
        Controls.Add(empty);
        Controls.Add(gapBelow);
        Controls.Add(divider);
        Controls.Add(gap);
        Controls.Add(search);

        Filter();
    }

    /// <summary>Raised with the account to copy a code from. The window closes right after.</summary>
    public event Action<OtpAccount>? Chosen;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Account names stay out of screenshots and screen sharing, like the notice.
        Native.ExcludeFromCapture(Handle);
        Native.RoundCorners(Handle);
        if (IsWindows11) Native.SetBorderColor(Handle, theme.Border);
        UpdateForDpi();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + area.Height / 4);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Native.SetForegroundWindow(Handle);
        Activate();
        search.Focus();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateForDpi();
    }

    /// <summary>Sizes the list's own fonts and rows for the monitor the window is on.</summary>
    void UpdateForDpi()
    {
        titleFont.Dispose();
        subtitleFont.Dispose();
        titleFont = new Font("Segoe UI Semibold", LogicalToDeviceUnits(14), GraphicsUnit.Pixel);
        subtitleFont = new Font("Segoe UI", LogicalToDeviceUnits(12), GraphicsUnit.Pixel);
        list.ItemHeight = LogicalToDeviceUnits(46);
        list.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (IsWindows11) return;
        using var border = new Pen(theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        CloseOnce();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                CloseOnce();
                return true;
            case Keys.Enter:
                Choose();
                return true;
            case Keys.Down:
                MoveSelection(1);
                return true;
            case Keys.Up:
                MoveSelection(-1);
                return true;
            case Keys.PageDown:
                MoveSelection(5);
                return true;
            case Keys.PageUp:
                MoveSelection(-5);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        list.Items.Clear();
        Vault.Forget(accounts);
    }

    void Filter()
    {
        var query = search.Text;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var account in accounts.Where(account => account.Matches(query))) list.Items.Add(new Entry(account));
        list.EndUpdate();
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        empty.Visible = list.Items.Count == 0;
        list.Visible = list.Items.Count > 0;
    }

    void MoveSelection(int step)
    {
        if (list.Items.Count == 0) return;
        list.SelectedIndex = Math.Clamp(list.SelectedIndex + step, 0, list.Items.Count - 1);
    }

    void Choose()
    {
        if (list.SelectedItem is not Entry entry) return;
        // Close first, so focus goes back to the app the code is being pasted into.
        var account = entry.Account;
        Hide();
        Chosen?.Invoke(account);
        CloseOnce();
    }

    void CloseOnce()
    {
        if (closing) return;
        closing = true;
        BeginInvoke(Close);
    }

    void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || list.Items[e.Index] is not Entry entry) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(selected ? theme.Selection : theme.Background))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var inset = LogicalToDeviceUnits(10);
        var bounds = Rectangle.Inflate(e.Bounds, -inset, 0);
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Left;
        var subtitle = entry.Account.Subtitle;
        if (subtitle.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, entry.Account.Title, titleFont, bounds, theme.Text, flags | TextFormatFlags.VerticalCenter);
            return;
        }
        var half = bounds.Height / 2;
        var top = new Rectangle(bounds.X, bounds.Y + LogicalToDeviceUnits(4), bounds.Width, half - LogicalToDeviceUnits(2));
        var bottom = new Rectangle(bounds.X, bounds.Y + half, bounds.Width, half - LogicalToDeviceUnits(4));
        TextRenderer.DrawText(e.Graphics, entry.Account.Title, titleFont, top, theme.Text, flags | TextFormatFlags.Bottom);
        TextRenderer.DrawText(e.Graphics, subtitle, subtitleFont, bottom, theme.SecondaryText, flags | TextFormatFlags.Top);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            titleFont.Dispose();
            subtitleFont.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>A list row. Its text is what screen readers announce.</summary>
    sealed class Entry(OtpAccount account)
    {
        public OtpAccount Account { get; } = account;

        public override string ToString() =>
            Account.Subtitle.Length > 0 ? $"{Account.Title}, {Account.Subtitle}" : Account.Title;
    }
}
