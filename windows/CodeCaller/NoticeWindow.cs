using System.Diagnostics;
using System.Drawing.Drawing2D;
using CodeCaller.Core;

namespace CodeCaller;

/// <summary>
/// A small notice near the top of the screen that says what happened.
/// </summary>
/// <remarks>
/// It never takes focus or blocks clicks, and asks Windows to keep it out of
/// screenshots and screen sharing so a code isn't shown to an audience.
/// </remarks>
internal sealed class NoticeWindow : Form
{
    const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    static readonly bool IsWindows11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    readonly System.Windows.Forms.Timer animation = new() { Interval = 15 };
    readonly System.Windows.Forms.Timer hideTimer = new();
    readonly Stopwatch fadeClock = new();
    Notice? notice;
    NoticeLayout? layout;
    Theme theme = Theme.Current;
    double alpha;
    double fadeFrom;
    double fadeTo;
    double fadeMilliseconds = 1;
    float spinnerAngle;

    public NoticeWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Text = "Code Caller";
        AccessibleRole = AccessibleRole.Alert;
        animation.Tick += (_, _) => Animate();
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            FadeTo(0, 250);
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style = Native.WS_POPUP;
            // Topmost, kept off the taskbar and Alt+Tab, never activated, and clicks pass straight through.
            parameters.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE
                | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetLayeredWindowAttributes(Handle, 0, 0, Native.LWA_ALPHA);
        Native.ExcludeFromCapture(Handle);
        Native.RoundCorners(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_NCHITTEST:
                m.Result = Native.HTTRANSPARENT;
                return;
            case Native.WM_MOUSEACTIVATE:
                m.Result = Native.MA_NOACTIVATE;
                return;
            case Native.WM_DPICHANGED:
                // Present() already sized the notice for the monitor it's moving to.
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Shows a notice at the top of the screen the pointer is on, replacing any notice already showing.</summary>
    public void Present(Notice notice)
    {
        this.notice = notice;
        theme = Theme.Current;

        var pointer = Cursor.Position;
        var area = Screen.FromPoint(pointer).WorkingArea;
        var dpi = Native.DpiAt(pointer);
        layout?.Dispose();
        layout = new NoticeLayout(notice, dpi);

        var size = layout.Size;
        var location = new Point(area.Left + (area.Width - size.Width) / 2, area.Top + NoticeLayout.Scale(12, dpi));
        Bounds = new Rectangle(location, size);
        if (!Visible) Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, location.X, location.Y, size.Width, size.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        if (IsWindows11) Native.SetBorderColor(Handle, theme.Border);
        Invalidate();

        hideTimer.Stop();
        FadeTo(1, 150);
        if (notice.Duration is TimeSpan duration)
        {
            hideTimer.Interval = (int)duration.TotalMilliseconds;
            hideTimer.Start();
        }

        // Read the result aloud for screen reader users.
        var spoken = notice.Detail is null ? notice.Title : $"{notice.Title}. {notice.Detail}";
        AccessibilityObject.RaiseAutomationNotification(
            System.Windows.Forms.Automation.AutomationNotificationKind.ActionCompleted,
            System.Windows.Forms.Automation.AutomationNotificationProcessing.ImportantMostRecent,
            spoken);
    }

    public void Dismiss()
    {
        hideTimer.Stop();
        if (Visible) FadeTo(0, 250);
    }

    void FadeTo(double target, double milliseconds)
    {
        fadeFrom = alpha;
        fadeTo = target;
        fadeMilliseconds = milliseconds;
        fadeClock.Restart();
        animation.Start();
    }

    void Animate()
    {
        var progress = Math.Min(1, fadeClock.Elapsed.TotalMilliseconds / fadeMilliseconds);
        var eased = 1 - Math.Pow(1 - progress, 3);
        alpha = fadeFrom + (fadeTo - fadeFrom) * eased;
        Native.SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(alpha * 255), Native.LWA_ALPHA);

        var spinning = notice?.Symbol == NoticeSymbol.Progress && fadeTo > 0;
        if (spinning)
        {
            spinnerAngle = (spinnerAngle + 9) % 360;
            Invalidate();
        }
        if (progress < 1) return;
        if (fadeTo == 0)
        {
            animation.Stop();
            Hide();
        }
        else if (!spinning)
        {
            animation.Stop();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (notice is null || layout is null) return;
        var graphics = e.Graphics;
        graphics.Clear(theme.Background);
        if (!IsWindows11)
        {
            // Windows 11 draws a border for us; Windows 10 needs one drawn.
            using var border = new Pen(theme.Border);
            graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        if (notice.Symbol == NoticeSymbol.Progress)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var diameter = NoticeLayout.Scale(18, layout.Dpi);
            var bounds = layout.Symbol;
            var circle = new Rectangle(bounds.X + (bounds.Width - diameter) / 2, bounds.Y + (bounds.Height - diameter) / 2, diameter, diameter);
            using var pen = new Pen(theme.SecondaryText, NoticeLayout.Scale(2, layout.Dpi)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(pen, circle, spinnerAngle, 270);
        }
        else
        {
            TextRenderer.DrawText(graphics, Theme.Glyph(notice.Symbol), layout.SymbolFont, layout.Symbol, theme.Tint(notice.Symbol),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        TextRenderer.DrawText(graphics, notice.Title, layout.TitleFont, layout.Title, theme.Text, TextFlags);
        if (notice.Detail is not null)
        {
            TextRenderer.DrawText(graphics, notice.Detail, layout.DetailFont, layout.Detail, theme.SecondaryText, TextFlags);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animation.Dispose();
            hideTimer.Dispose();
            layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Fonts and positions for one notice, worked out in pixels for the monitor it appears on.</summary>
    sealed class NoticeLayout : IDisposable
    {
        public NoticeLayout(Notice notice, int dpi)
        {
            Dpi = dpi;
            TitleFont = notice.ShowsCode
                ? new Font(Theme.CodeFont, Scale(22, dpi), FontStyle.Bold, GraphicsUnit.Pixel)
                : new Font("Segoe UI Semibold", Scale(15, dpi), FontStyle.Regular, GraphicsUnit.Pixel);
            DetailFont = new Font("Segoe UI", Scale(13, dpi), FontStyle.Regular, GraphicsUnit.Pixel);
            SymbolFont = new Font(Theme.SymbolFont, Scale(20, dpi), FontStyle.Regular, GraphicsUnit.Pixel);

            var maxText = Scale(520, dpi);
            var title = TextRenderer.MeasureText(notice.Title, TitleFont, Size.Empty, TextFlags);
            var detail = notice.Detail is null ? Size.Empty : TextRenderer.MeasureText(notice.Detail, DetailFont, Size.Empty, TextFlags);

            int padX = Scale(18, dpi), padY = Scale(12, dpi), symbolSize = Scale(26, dpi), gap = Scale(12, dpi);
            var spacing = notice.Detail is null ? 0 : Scale(2, dpi);
            var textWidth = Math.Min(maxText, Math.Max(title.Width, detail.Width));
            var textHeight = title.Height + spacing + detail.Height;
            var contentHeight = Math.Max(textHeight, symbolSize);

            Size = new Size(Math.Max(Scale(220, dpi), padX + symbolSize + gap + textWidth + padX), padY + contentHeight + padY);
            Symbol = new Rectangle(padX, padY + (contentHeight - symbolSize) / 2, symbolSize, symbolSize);
            var textLeft = padX + symbolSize + gap;
            var textTop = padY + (contentHeight - textHeight) / 2;
            Title = new Rectangle(textLeft, textTop, textWidth, title.Height);
            Detail = new Rectangle(textLeft, Title.Bottom + spacing, textWidth, detail.Height);
        }

        public int Dpi { get; }
        public Font TitleFont { get; }
        public Font DetailFont { get; }
        public Font SymbolFont { get; }
        public Size Size { get; }
        public Rectangle Symbol { get; }
        public Rectangle Title { get; }
        public Rectangle Detail { get; }

        public static int Scale(int pixelsAt96, int dpi) => (int)Math.Round(pixelsAt96 * dpi / 96.0);

        public void Dispose()
        {
            TitleFont.Dispose();
            DetailFont.Dispose();
            SymbolFont.Dispose();
        }
    }
}
