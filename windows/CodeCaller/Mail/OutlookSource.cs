using System.Reflection;
using System.Runtime.InteropServices;
using CodeCaller.Core.Mail;
using Microsoft.Win32;

namespace CodeCaller.Mail;

/// <summary>
/// Reads classic Outlook through its COM object model, late bound so no Office
/// libraries are needed to build or run Code Caller.
/// </summary>
/// <remarks>
/// Every call only reads. Each COM object is released as soon as the search is done,
/// and Outlook is never told to send, receive or quit. The new Outlook for Windows
/// has no COM object model, so only classic Outlook works.
/// </remarks>
internal sealed class OutlookSource : IMailSource
{
    const int FolderInbox = 6;   // olFolderInbox
    const int FolderJunk = 23;   // olFolderJunk
    const int MailItemClass = 43; // olMail
    const int MaxPerFolder = 25;

    public string Name => "Outlook";

    /// <summary>Classic Outlook is installed and has a mail profile, so starting it won't open a setup wizard.</summary>
    public bool IsAvailable => Type.GetTypeFromProgID("Outlook.Application", throwOnError: false) is not null && HasProfile();

    public Task<IReadOnlyList<MailMessage>> ReadRecentAsync(DateTimeOffset cutoff, CancellationToken cancellation) =>
        RunOnStaThread(() => Read(cutoff, cancellation));

    static bool HasProfile()
    {
        foreach (var version in new[] { "16.0", "15.0" })
        {
            using var profiles = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Office\{version}\Outlook\Profiles");
            if (profiles?.SubKeyCount > 0) return true;
        }
        return false;
    }

    static IReadOnlyList<MailMessage> Read(DateTimeOffset cutoff, CancellationToken cancellation)
    {
        // Retries calls Outlook turns away while it's busy, such as when a dialog is open.
        Native.CoRegisterMessageFilter(new RetryMessageFilter(), out var previousFilter);
        using var com = new ComObjects();
        try
        {
            var type = Type.GetTypeFromProgID("Outlook.Application", throwOnError: true)!;
            // Connects to Outlook if it's running, or starts it in the background if not.
            var application = com.Track(Activator.CreateInstance(type))!;
            var session = com.Track(Call(application, "GetNamespace", "MAPI"))!;

            var messages = new List<MailMessage>();
            foreach (var folder in Folders(session, com))
            {
                cancellation.ThrowIfCancellationRequested();
                ReadFolder(folder, cutoff, messages, com, cancellation);
            }
            messages.Sort((a, b) => b.Received.CompareTo(a.Received));
            return messages;
        }
        finally
        {
            com.Dispose();
            Native.CoRegisterMessageFilter(previousFilter, out _);
        }
    }

    /// <summary>The inbox and junk folder of every account, falling back to the default store's.</summary>
    static List<object> Folders(object session, ComObjects com)
    {
        var folders = new List<object>();
        try
        {
            var stores = com.Track(Get(session, "Stores"))!;
            var count = Convert.ToInt32(Get(stores, "Count"), System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                var store = com.Track(Call(stores, "Item", index));
                if (store is null) continue;
                foreach (var kind in new[] { FolderInbox, FolderJunk })
                {
                    // Stores like public folders or shared calendars have no inbox; skip them.
                    if (TryCall(store, "GetDefaultFolder", kind) is { } folder) folders.Add(com.Track(folder)!);
                }
            }
        }
        catch (Exception error) when (IsComFailure(error))
        {
            // Older Outlook without Stores; the default folders below still work.
        }

        if (folders.Count == 0)
        {
            foreach (var kind in new[] { FolderInbox, FolderJunk })
            {
                if (TryCall(session, "GetDefaultFolder", kind) is { } folder) folders.Add(com.Track(folder)!);
            }
        }
        return folders;
    }

    static void ReadFolder(object folder, DateTimeOffset cutoff, List<MailMessage> messages, ComObjects com, CancellationToken cancellation)
    {
        object items;
        try
        {
            items = com.Track(Get(folder, "Items"))!;
            // Sorting newest first means the search can stop at the first message older than the cutoff.
            Call(items, "Sort", "[ReceivedTime]", true);
        }
        catch (Exception error) when (IsComFailure(error))
        {
            return;
        }

        var read = 0;
        var item = com.Track(TryCall(items, "GetFirst"));
        while (item is not null && read < MaxPerFolder)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var received = ReceivedTime(item);
                if (received is DateTimeOffset time)
                {
                    if (time < cutoff) break;
                    if (Convert.ToInt32(Get(item, "Class"), System.Globalization.CultureInfo.InvariantCulture) == MailItemClass)
                    {
                        var sender = Get(item, "SenderName") as string;
                        if (string.IsNullOrWhiteSpace(sender)) sender = Get(item, "SenderEmailAddress") as string;
                        messages.Add(new MailMessage(time, sender ?? "", Get(item, "Subject") as string ?? "", Get(item, "Body") as string ?? ""));
                        read++;
                    }
                }
            }
            catch (Exception error) when (IsComFailure(error))
            {
                // An item that can't be read right now, such as one still downloading.
            }
            item = com.Track(TryCall(items, "GetNext"));
        }
    }

    static DateTimeOffset? ReceivedTime(object item)
    {
        if (Get(item, "ReceivedTime") is not DateTime local) return null;
        // Outlook uses 1 January 4501 to mean "no date".
        if (local.Year >= 4500) return null;
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local));
    }

    static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, System.Globalization.CultureInfo.InvariantCulture);

    static object? Call(object target, string name, params object?[] arguments) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments, System.Globalization.CultureInfo.InvariantCulture);

    static object? TryCall(object target, string name, params object?[] arguments)
    {
        try
        {
            return Call(target, name, arguments);
        }
        catch (Exception error) when (IsComFailure(error))
        {
            return null;
        }
    }

    static bool IsComFailure(Exception error) =>
        error is COMException or TargetInvocationException or InvalidCastException or MissingMethodException or InvalidComObjectException;

    static Task<IReadOnlyList<MailMessage>> RunOnStaThread(Func<IReadOnlyList<MailMessage>> work)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<MailMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "Outlook reader",
        };
        // Outlook's object model expects a single-threaded apartment.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>Holds every COM object a search touches, and releases them all when it's done.</summary>
    sealed class ComObjects : IDisposable
    {
        readonly List<object> objects = [];

        public object? Track(object? value)
        {
            if (value is not null && Marshal.IsComObject(value) && !objects.Exists(existing => ReferenceEquals(existing, value)))
            {
                objects.Add(value);
            }
            return value;
        }

        public void Dispose()
        {
            for (var index = objects.Count - 1; index >= 0; index--)
            {
                try
                {
                    Marshal.FinalReleaseComObject(objects[index]);
                }
                catch (Exception error) when (error is ArgumentException or InvalidComObjectException)
                {
                    // Already released.
                }
            }
            objects.Clear();
        }
    }

    /// <summary>Waits and retries when Outlook says it's busy, for up to five seconds.</summary>
    sealed class RetryMessageFilter : IOleMessageFilter
    {
        const int ServerCallIsHandled = 0;
        const int ServerCallRetryLater = 2;
        const int PendingMessageWaitDefaultProcess = 2;

        public int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) => ServerCallIsHandled;

        public int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType) =>
            dwRejectType == ServerCallRetryLater && dwTickCount < 5000 ? 150 : -1;

        public int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType) => PendingMessageWaitDefaultProcess;
    }
}
