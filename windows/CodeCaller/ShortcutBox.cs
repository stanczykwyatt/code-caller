using System.Media;
using CodeCaller.Core.Settings;

namespace CodeCaller;

/// <summary>
/// A button that records a shortcut: click it, then press the keys. Esc cancels.
/// While it's recording, the app's own shortcuts are turned off so they can be typed.
/// </summary>
internal sealed class ShortcutBox : Button
{
    Shortcut shortcut;
    bool recording;

    public ShortcutBox()
    {
        AccessibleDescription = "Press to record a new shortcut, then type it. Escape cancels.";
        Click += (_, _) =>
        {
            if (recording) StopRecording();
            else StartRecording();
        };
    }

    /// <summary>Raised when a new shortcut has been typed.</summary>
    public event Action<Shortcut>? Recorded;

    /// <summary>Raised with true when recording starts and false when it ends.</summary>
    public event Action<bool>? RecordingChanged;

    public Shortcut Shortcut
    {
        get => shortcut;
        set
        {
            shortcut = value;
            if (!recording) Text = value.ToString();
        }
    }

    public void StopRecording()
    {
        if (!recording) return;
        recording = false;
        Text = shortcut.ToString();
        RecordingChanged?.Invoke(false);
    }

    void StartRecording()
    {
        recording = true;
        Text = "Type a shortcut…";
        RecordingChanged?.Invoke(true);
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        StopRecording();
    }

    protected override bool IsInputKey(Keys keyData) => recording || base.IsInputKey(keyData);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Takes every key while recording, including Alt combinations that would otherwise open menus.
        if (recording && Record(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    bool Record(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var modifierKeys = keyData & Keys.Modifiers;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return true;
        if (key == Keys.Escape && modifierKeys == Keys.None)
        {
            StopRecording();
            return true;
        }
        if (key == Keys.Tab && modifierKeys == Keys.None)
        {
            StopRecording();
            return false;
        }

        var modifiers = ShortcutModifiers.None;
        if (modifierKeys.HasFlag(Keys.Control)) modifiers |= ShortcutModifiers.Control;
        if (modifierKeys.HasFlag(Keys.Alt)) modifiers |= ShortcutModifiers.Alt;
        if (modifierKeys.HasFlag(Keys.Shift)) modifiers |= ShortcutModifiers.Shift;
        if (Native.IsWindowsKeyDown()) modifiers |= ShortcutModifiers.Windows;

        // Needs Ctrl, Alt or Windows (or a function key) so plain typing is never taken over.
        var candidate = new Shortcut(modifiers, (int)key);
        if (!candidate.IsValid)
        {
            SystemSounds.Beep.Play();
            return true;
        }
        shortcut = candidate;
        StopRecording();
        Recorded?.Invoke(candidate);
        return true;
    }
}
