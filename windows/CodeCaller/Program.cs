namespace CodeCaller;

static class Program
{
    [STAThread]
    static void Main()
    {
        // One copy at a time, so two never fight over the same shortcuts.
        using var instance = new Mutex(initiallyOwned: true, @"Local\CodeCaller.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Code Caller is already running. You'll find it in the system tray, next to the clock.",
                "Code Caller",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        Application.Run(app);
    }
}
