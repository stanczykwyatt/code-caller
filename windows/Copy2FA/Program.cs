namespace Copy2FA;

static class Program
{
    [STAThread]
    static void Main()
    {
        // One copy at a time, so two never fight over the same shortcuts.
        using var instance = new Mutex(initiallyOwned: true, @"Local\Copy2FA.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Copy 2FA is already running. You'll find it in the system tray, next to the clock.",
                "Copy 2FA",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        Application.Run(app);
    }
}
