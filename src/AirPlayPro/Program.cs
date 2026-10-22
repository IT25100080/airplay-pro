namespace AirPlayPro;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one tray instance: two receivers would fight over the same ports.
        using var mutex = new Mutex(true, @"Local\AirPlayPro.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("AirPlay Pro is already running. Look for its icon in the system tray.",
                "AirPlay Pro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
