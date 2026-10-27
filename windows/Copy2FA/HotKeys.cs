using Copy2FA.Core.Settings;

namespace Copy2FA;

/// <summary>
/// Global shortcuts through RegisterHotKey. Windows only reports the registered key
/// combinations, so Copy 2FA never sees anything else you type.
/// </summary>
internal sealed class HotKeys : NativeWindow, IDisposable
{
    public const int CopyLatestCode = 1;
    public const int OpenAuthenticator = 2;

    readonly HashSet<int> registered = [];

    public HotKeys()
    {
        // An invisible window that only exists to receive WM_HOTKEY. It also owns the clipboard.
        CreateHandle(new CreateParams { Caption = "Copy 2FA" });
    }

    public event Action<int>? Pressed;

    /// <summary>Registers a shortcut, replacing any earlier one with the same id. Returns false if another app has it.</summary>
    public bool Register(int id, Shortcut shortcut)
    {
        Unregister(id);
        if (!shortcut.IsValid) return false;
        var modifiers = (uint)shortcut.Modifiers | Native.MOD_NOREPEAT;
        if (!Native.RegisterHotKey(Handle, id, modifiers, (uint)shortcut.Key)) return false;
        registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (registered.Remove(id)) Native.UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in registered.ToList()) Unregister(id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            Pressed?.Invoke(m.WParam.ToInt32());
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
