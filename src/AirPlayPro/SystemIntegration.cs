using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace AirPlayPro;

/// <summary>Windows plumbing: run-at-login, Bonjour detection, firewall rules.</summary>
internal static class SystemIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AirPlayPro";
    public const string FirewallRuleName = "AirPlay Pro Receiver";
    public const string BonjourDownloadUrl = "https://support.apple.com/en-us/106380";

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// iPhones find the PC through Bonjour (mDNS). UxPlay 1.73 uses Apple's Bonjour
    /// service for that, which ships with iTunes or "Bonjour Print Services".
    /// </summary>
    public static bool IsBonjourInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Bonjour Service");
        return key is not null;
    }

    /// <summary>
    /// Re-creates the inbound firewall rule for uxplay.exe (needs admin, so Windows shows a UAC prompt).
    /// The installer creates the same rule; this is the repair path.
    /// </summary>
    public static bool RepairFirewall()
    {
        string exe = ReceiverHost.EngineExe;
        string cmd =
            $"netsh advfirewall firewall delete rule name=\"{FirewallRuleName}\" & " +
            $"netsh advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow " +
            $"program=\"{exe}\" enable=yes profile=private,domain";

        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + cmd)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(15000);
            return p is { HasExited: true, ExitCode: 0 };
        }
        catch (Win32Exception)
        {
            return false; // user declined the UAC prompt
        }
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open {target}:\n{ex.Message}", "AirPlay Pro",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
