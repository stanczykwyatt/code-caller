using Copy2FA.Core.Authenticator;

namespace Copy2FA.Authenticator;

/// <summary>
/// A small search window for the authenticator: type to filter, arrow keys to move,
/// Enter to copy, Esc to close. Each row shows its current code and how long it has
/// left, like the macOS picker. It closes as soon as it loses focus.
/// </summary>
internal sealed class PickerWindow : Form
{
    static readonly bool IsWindows11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    readonly List<OtpAccount> accounts;
    readonly Theme theme = Theme.Current;
    readonly TextBox search = new();
    readonly ListBox list = new();
    readonly Label empty = new();
    // Redraws the rows often enough that codes change and countdowns tick on time.
    readonly System.Windows.Forms.Timer clock = new() { Interval = 500 };
    Font titleFont = new("Segoe UI Semibold", 14f, GraphicsUnit.Pixel);
    Font subtitleFont = new("Segoe UI", 12f, GraphicsUnit.Pixel);
    Font codeFont = new(Theme.CodeFont, 20f, FontStyle.Bold, GraphicsUnit.Pixel);
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
        ClientSize = new Size(460, 380);
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
        clock.Tick += (_, _) => list.Invalidate();

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
        clock.Start();
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
        codeFont.Dispose();
        titleFont = new Font("Segoe UI Semibold", LogicalToDeviceUnits(14), GraphicsUnit.Pixel);
        subtitleFont = new Font("Segoe UI", LogicalToDeviceUnits(12), GraphicsUnit.Pixel);
        codeFont = new Font(Theme.CodeFont, LogicalToDeviceUnits(20), FontStyle.Bold, GraphicsUnit.Pixel);
        list.ItemHeight = LogicalToDeviceUnits(54);
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
        clock.Stop();
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
        var account = entry.Account;
        var graphics = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(selected ? theme.Selection : theme.Background))
        {
            graphics.FillRectangle(background, e.Bounds);
        }

        var inset = LogicalToDeviceUnits(12);
        var bounds = Rectangle.Inflate(e.Bounds, -inset, 0);
        var textRight = DrawCode(graphics, account, bounds) - LogicalToDeviceUnits(12);
        var names = new Rectangle(bounds.X, bounds.Y, Math.Max(0, textRight - bounds.X), bounds.Height);

        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Left;
        var subtitle = account.Subtitle;
        if (subtitle.Length == 0)
        {
            TextRenderer.DrawText(graphics, account.Title, titleFont, names, theme.Text, flags | TextFormatFlags.VerticalCenter);
            return;
        }
        var half = names.Height / 2;
        var top = new Rectangle(names.X, names.Y + LogicalToDeviceUnits(4), names.Width, half - LogicalToDeviceUnits(2));
        var bottom = new Rectangle(names.X, names.Y + half, names.Width, half - LogicalToDeviceUnits(4));
        TextRenderer.DrawText(graphics, account.Title, titleFont, top, theme.Text, flags | TextFormatFlags.Bottom);
        TextRenderer.DrawText(graphics, subtitle, subtitleFont, bottom, theme.SecondaryText, flags | TextFormatFlags.Top);
    }

    /// <summary>
    /// Draws the current code and a ring showing how long it has left, right-aligned in the
    /// row, and returns where they start. Codes turn orange in their last five seconds.
    /// Counter-based accounts show "Next code" instead.
    /// </summary>
    int DrawCode(Graphics graphics, OtpAccount account, Rectangle bounds)
    {
        if (account.Kind == OtpKind.Hotp)
        {
            // A counter-based code is only made when it's copied, since making one uses it up.
            const TextFormatFlags labelFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var label = TextRenderer.MeasureText(graphics, "Next code", subtitleFont, Size.Empty, labelFlags);
            var labelArea = new Rectangle(bounds.Right - label.Width, bounds.Y, label.Width, bounds.Height);
            TextRenderer.DrawText(graphics, "Next code", subtitleFont, labelArea, theme.SecondaryText, labelFlags | TextFormatFlags.VerticalCenter);
            return labelArea.Left;
        }

        var now = DateTimeOffset.UtcNow;
        var remaining = Totp.SecondsRemaining(account.Period, now);
        var ending = remaining <= 5;

        var diameter = LogicalToDeviceUnits(18);
        var stroke = 2.5f * DeviceDpi / 96f;
        var ring = new Rectangle(bounds.Right - diameter, bounds.Y + (bounds.Height - diameter) / 2, diameter, diameter);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var track = new Pen(theme.Border, stroke))
        {
            graphics.DrawEllipse(track, ring);
        }
        using (var progress = new Pen(ending ? theme.Caution : theme.Accent, stroke)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        })
        {
            graphics.DrawArc(progress, ring, -90, 360f * remaining / account.Period);
        }

        var code = CodeFormat.Grouped(Totp.Code(account, now));
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var size = TextRenderer.MeasureText(graphics, code, codeFont, Size.Empty, flags);
        var left = ring.Left - LogicalToDeviceUnits(10) - size.Width;
        var area = new Rectangle(left, bounds.Y, size.Width, bounds.Height);
        TextRenderer.DrawText(graphics, code, codeFont, area, ending ? theme.Caution : theme.Text, flags | TextFormatFlags.VerticalCenter);
        return left;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            clock.Dispose();
            titleFont.Dispose();
            subtitleFont.Dispose();
            codeFont.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>A list row. Its text is what screen readers announce.</summary>
    sealed class Entry(OtpAccount account)
    {
        public OtpAccount Account { get; } = account;

        public override string ToString()
        {
            var name = Account.Subtitle.Length > 0 ? $"{Account.Title}, {Account.Subtitle}" : Account.Title;
            return Account.Kind == OtpKind.Hotp ? name + ", next code" : name;
        }
    }
}
