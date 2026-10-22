namespace AirPlayPro;

/// <summary>Settings window, laid out in code so there's no designer file to maintain.</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private readonly TextBox _name = new() { Width = 260 };
    private readonly CheckBox _appendHost = new() { Text = "Add \"@PC-name\" to the name", AutoSize = true };
    private readonly CheckBox _fullscreen = new() { Text = "Open mirrored screen in full screen", AutoSize = true };
    private readonly ComboBox _video = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _audio = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _requirePin = new() { Text = "Require a PIN to connect", AutoSize = true };
    private readonly TextBox _pin = new() { Width = 80, MaxLength = 4, PlaceholderText = "random" };
    private readonly CheckBox _takeover = new() { Text = "Let a new device take over from the current one", AutoSize = true };
    private readonly CheckBox _youTube = new() { Text = "Play video from the YouTube app's AirPlay button", AutoSize = true };
    private readonly CheckBox _autoStart = new() { Text = "Start receiver when AirPlay Pro opens", AutoSize = true };
    private readonly CheckBox _withWindows = new() { Text = "Open AirPlay Pro when I sign in to Windows", AutoSize = true };
    private readonly TextBox _extra = new() { Width = 260, PlaceholderText = "advanced UxPlay options" };

    public AppSettings Result => _settings;

    public SettingsForm(AppSettings current)
    {
        _settings = current.Clone();

        Text = "AirPlay Pro Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 9.5f);
        Icon = TrayIcons.For(ReceiverState.Waiting);

        _video.Items.AddRange(AppSettings.VideoSinks);
        _audio.Items.AddRange(AppSettings.AudioSinks);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, "Name on iPhone/iPad:", _name);
        AddRow(grid, "", _appendHost);
        AddRow(grid, "Video output:", _video);
        AddRow(grid, "", _fullscreen);
        AddRow(grid, "Audio output:", _audio);
        AddRow(grid, "", _requirePin);
        AddRow(grid, "PIN (4 digits):", _pin);
        AddRow(grid, "", _takeover);
        AddRow(grid, "", _youTube);
        AddRow(grid, "", _autoStart);
        AddRow(grid, "", _withWindows);
        AddRow(grid, "Extra options:", _extra);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;

        LoadValues();
        _requirePin.CheckedChanged += (_, _) => _pin.Enabled = _requirePin.Checked;
        _pin.Enabled = _requirePin.Checked;
        ok.Click += OnSave;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6),
        }, 0, row);
        control.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(control, 1, row);
    }

    private void LoadValues()
    {
        _name.Text = _settings.DeviceName;
        _appendHost.Checked = _settings.AppendHostname;
        _fullscreen.Checked = _settings.Fullscreen;
        _video.SelectedItem = AppSettings.VideoSinks.Contains(_settings.VideoSink) ? _settings.VideoSink : AppSettings.VideoSinks[0];
        _audio.SelectedItem = AppSettings.AudioSinks.Contains(_settings.AudioSink) ? _settings.AudioSink : AppSettings.AudioSinks[0];
        _requirePin.Checked = _settings.RequirePin;
        _pin.Text = _settings.Pin;
        _takeover.Checked = _settings.AllowTakeover;
        _youTube.Checked = _settings.YouTubeStreaming;
        _autoStart.Checked = _settings.StartReceiverOnLaunch;
        _withWindows.Checked = SystemIntegration.StartsWithWindows;
        _extra.Text = _settings.ExtraArgs;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        string pin = _pin.Text.Trim();
        if (_requirePin.Checked && pin.Length > 0 && (pin.Length != 4 || !pin.All(char.IsDigit)))
        {
            MessageBox.Show("The PIN must be exactly 4 digits, or left empty for a random PIN each time.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _settings.DeviceName = string.IsNullOrWhiteSpace(_name.Text) ? Environment.MachineName : _name.Text.Trim();
        _settings.AppendHostname = _appendHost.Checked;
        _settings.Fullscreen = _fullscreen.Checked;
        _settings.VideoSink = (string)_video.SelectedItem!;
        _settings.AudioSink = (string)_audio.SelectedItem!;
        _settings.RequirePin = _requirePin.Checked;
        _settings.Pin = pin;
        _settings.AllowTakeover = _takeover.Checked;
        _settings.YouTubeStreaming = _youTube.Checked;
        _settings.StartReceiverOnLaunch = _autoStart.Checked;
        _settings.ExtraArgs = _extra.Text.Trim();

        try { SystemIntegration.StartsWithWindows = _withWindows.Checked; }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't change the start-with-Windows setting:\n{ex.Message}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
