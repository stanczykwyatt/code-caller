using System.Runtime.InteropServices;

namespace Copy2FA;

/// <summary>
/// Puts a code on the clipboard so it stays on this PC and out of clipboard history,
/// then takes it off again after a short time.
/// </summary>
internal sealed class SecureClipboard : IDisposable
{
    // Formats Windows checks before saving a copy anywhere. See "Clipboard formats" in the Win32 docs.
    static readonly uint ExcludeFromMonitoring = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    static readonly uint CanIncludeInHistory = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    static readonly uint CanUploadToCloud = Native.RegisterClipboardFormat("CanUploadToCloudClipboard");

    readonly IntPtr owner;
    readonly System.Windows.Forms.Timer clearTimer = new();
    uint? copiedSequence;

    /// <param name="owner">A window of this app. The clipboard needs one to belong to.</param>
    public SecureClipboard(IntPtr owner)
    {
        this.owner = owner;
        clearTimer.Tick += (_, _) =>
        {
            clearTimer.Stop();
            ClearIfUnchanged();
        };
    }

    /// <summary>Copies <paramref name="code"/>, and clears it after <paramref name="clearAfterSeconds"/> unless that's zero.</summary>
    public bool Copy(string code, int clearAfterSeconds)
    {
        clearTimer.Stop();
        if (!TryOpen()) return false;
        try
        {
            Native.EmptyClipboard();
            if (!SetData(Native.CF_UNICODETEXT, code)) return false;
            // Clipboard managers and monitoring apps that honor this skip the copy.
            SetDword(ExcludeFromMonitoring, 0);
            // Keeps the code out of Win+V history, and off your other devices through cloud clipboard.
            SetDword(CanIncludeInHistory, 0);
            SetDword(CanUploadToCloud, 0);
        }
        finally
        {
            Native.CloseClipboard();
        }
        copiedSequence = Native.GetClipboardSequenceNumber();

        if (clearAfterSeconds > 0)
        {
            clearTimer.Interval = clearAfterSeconds * 1000;
            clearTimer.Start();
        }
        return true;
    }

    /// <summary>Clears the clipboard if it still holds the code, leaving anything copied since alone.</summary>
    public void ClearIfUnchanged()
    {
        clearTimer.Stop();
        if (copiedSequence is not uint sequence) return;
        copiedSequence = null;
        if (Native.GetClipboardSequenceNumber() != sequence || !TryOpen()) return;
        try
        {
            Native.EmptyClipboard();
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    /// <summary>Another app may be holding the clipboard for a moment, so try a few times.</summary>
    bool TryOpen()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (Native.OpenClipboard(owner)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    static bool SetData(uint format, string text)
    {
        var bytes = (text.Length + 1) * sizeof(char);
        var memory = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (UIntPtr)bytes);
        if (memory == IntPtr.Zero) return false;
        var target = Native.GlobalLock(memory);
        if (target == IntPtr.Zero)
        {
            Native.GlobalFree(memory);
            return false;
        }
        try
        {
            var chars = text.ToCharArray();
            Marshal.Copy(chars, 0, target, chars.Length);
            Marshal.WriteInt16(target, chars.Length * sizeof(char), 0);
            Array.Clear(chars);
        }
        finally
        {
            Native.GlobalUnlock(memory);
        }
        // Once the clipboard accepts the memory it owns it; otherwise it's still ours to free.
        if (Native.SetClipboardData(format, memory) != IntPtr.Zero) return true;
        Native.GlobalFree(memory);
        return false;
    }

    static void SetDword(uint format, int value)
    {
        if (format == 0) return;
        var memory = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (UIntPtr)sizeof(int));
        if (memory == IntPtr.Zero) return;
        var target = Native.GlobalLock(memory);
        if (target == IntPtr.Zero)
        {
            Native.GlobalFree(memory);
            return;
        }
        Marshal.WriteInt32(target, value);
        Native.GlobalUnlock(memory);
        if (Native.SetClipboardData(format, memory) == IntPtr.Zero) Native.GlobalFree(memory);
    }

    public void Dispose()
    {
        ClearIfUnchanged();
        clearTimer.Dispose();
    }
}
