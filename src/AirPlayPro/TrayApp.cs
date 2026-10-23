namespace AirPlayPro;

/// <summary>The app has no main window: it lives in the system tray and owns the receiver.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ReceiverHost _receiver = new();
    private readonly SynchronizationContext _ui;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _toggle = new();
    private AppSettings _settings = AppSettings.Load();
    private SettingsForm? _settingsForm;

    public TrayApp()
    {
        // ApplicationContext is created on the UI thread after Application.Run sets this up.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _toggle.Click += (_, _) => ToggleReceiver();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggle);
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open log", null, (_, _) => SystemIntegration.Open(ReceiverHost.LogPath));
        menu.Items.Add("Fix firewall (needs admin)…", null, (_, _) => RepairFirewall());
        menu.Items.Add("Help: iPhone can't find this PC", null, (_, _) => ShowDiscoveryHelp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add($"Quit {AppProfile.Name}", null, (_, _) => Quit());

        _tray = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _receiver.StateChanged += state => _ui.Post(_ => OnStateChanged(state), null);
        OnStateChanged(ReceiverState.Stopped);

        if (!SystemIntegration.IsBonjourInstalled())
        {
            _tray.ShowBalloonTip(10000, "Bonjour is missing",
                "iPhones and iPads find this PC through Apple's Bonjour service. Choose " +
                "\"Help: iPhone can't find this PC\" from the tray menu to install it.",
                ToolTipIcon.Warning);
        }

        if (_settings.StartReceiverOnLaunch)
            _receiver.Start(_settings);
    }

    private void OnStateChanged(ReceiverState state)
    {
        string name = _settings.DeviceName;
        string text = state switch
        {
            ReceiverState.Starting => "Starting…",
            ReceiverState.Waiting => $"Ready as \"{name}\"",
            ReceiverState.Connected => "A device is connected",
            ReceiverState.Error => "Problem: " + (_receiver.LastError ?? "see log"),
            _ => "Receiver is off",
        };

        _status.Text = text.Length > 120 ? text[..117] + "…" : text;
        // NotifyIcon tooltips are limited to 127 characters.
        string tip = $"{AppProfile.Name} – {text}";
        _tray.Text = tip.Length > 127 ? tip[..124] + "…" : tip;
        _tray.Icon = TrayIcons.For(state);

        bool running = state is ReceiverState.Starting or ReceiverState.Waiting or ReceiverState.Connected;
        _toggle.Text = running ? "Turn receiver off" : "Turn receiver on";

        if (state == ReceiverState.Error)
            _tray.ShowBalloonTip(8000, AppProfile.Name, _receiver.LastError ?? "The receiver stopped.", ToolTipIcon.Error);
    }

    private void ToggleReceiver()
    {
        if (_receiver.State is ReceiverState.Stopped or ReceiverState.Error)
            _receiver.Start(_settings);
        else
            _receiver.Stop();
    }

    private void ShowSettings()
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        using (_settingsForm = new SettingsForm(_settings))
        {
            if (_settingsForm.ShowDialog() == DialogResult.OK)
            {
                _settings = _settingsForm.Result;
                _settings.Save();
                if (_receiver.State is not (ReceiverState.Stopped or ReceiverState.Error))
                    _receiver.Restart(_settings);
                else
                    OnStateChanged(_receiver.State);
            }
        }
        _settingsForm = null;
    }

    private void RepairFirewall()
    {
        if (SystemIntegration.RepairFirewall())
        {
            _tray.ShowBalloonTip(5000, AppProfile.Name, "Firewall rule updated.", ToolTipIcon.Info);
            if (_receiver.State is not (ReceiverState.Stopped or ReceiverState.Error))
                _receiver.Restart(_settings);
        }
        else
        {
            _tray.ShowBalloonTip(5000, AppProfile.Name, "The firewall rule wasn't changed.", ToolTipIcon.Warning);
        }
    }

    private void ShowDiscoveryHelp()
    {
        string bonjour = SystemIntegration.IsBonjourInstalled()
            ? "✔ Bonjour is installed."
            : "✘ Bonjour is NOT installed. Install \"Bonjour Print Services for Windows\" from Apple (Yes opens the download page).";

        var answer = MessageBox.Show(
            (AppProfile.IsSpeaker
                ? "For your iPhone/iPad/Mac to see this PC as an AirPlay speaker:\n\n"
                : "For your iPhone/iPad to see this PC in Screen Mirroring:\n\n") +
            $"{bonjour}\n\n" +
            "• Both devices must be on the same Wi-Fi/network (not a guest network).\n" +
            "• Windows must treat that network as Private: Settings → Network & internet → your network → Private network.\n" +
            $"• The firewall must allow {AppProfile.Name} (tray menu → Fix firewall).\n" +
            "• Some routers block device discovery (\"AP/client isolation\", \"multicast filtering\").\n\n" +
            "Open Apple's Bonjour download page now?",
            $"{AppProfile.Name} – connection help",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (answer == DialogResult.Yes)
            SystemIntegration.Open(SystemIntegration.BonjourDownloadUrl);
    }

    private void Quit()
    {
        _receiver.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}
