using System.Text.Json;

namespace AirPlayPro;

public sealed class AppSettings
{
    public string DeviceName { get; set; } = Environment.MachineName;
    public bool AppendHostname { get; set; } = false;
    public bool Fullscreen { get; set; } = false;
    public string VideoSink { get; set; } = "d3d12videosink";
    public string AudioSink { get; set; } = "wasapisink";
    public bool RequirePin { get; set; } = false;
    /// <summary>Fixed 4-digit PIN; empty means a new random PIN per connection.</summary>
    public string Pin { get; set; } = "";
    public bool AllowTakeover { get; set; } = true;
    /// <summary>
    /// Lets the YouTube app's AirPlay button play video: the PC streams it straight
    /// from YouTube. Without it, that button only sends audio.
    /// </summary>
    public bool YouTubeStreaming { get; set; } = true;
    public string ExtraArgs { get; set; } = "";
    public bool StartReceiverOnLaunch { get; set; } = true;
    /// <summary>
    /// Fixed AirPlay device ID (MAC-style). Apple devices remember receivers by it, so it
    /// must not change between runs; created once on first launch.
    /// </summary>
    public string DeviceId { get; set; } = "";

    public static readonly string[] VideoSinks = { "d3d12videosink", "d3d11videosink", "glimagesink", "autovideosink" };
    public static readonly string[] AudioSinks = { "wasapisink", "directsoundsink", "autoaudiosink" };

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirPlayPro");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
        }
        catch (Exception)
        {
            // A corrupt file shouldn't stop the receiver from starting; fall back to defaults.
        }
        settings ??= new AppSettings();

        if (string.IsNullOrEmpty(settings.DeviceId))
        {
            settings.DeviceId = NewDeviceId();
            try { settings.Save(); } catch (Exception) { }
        }
        return settings;
    }

    private static string NewDeviceId()
    {
        var bytes = new byte[6];
        Random.Shared.NextBytes(bytes);
        bytes[0] = (byte)((bytes[0] & 0xFC) | 0x02); // locally administered, unicast
        return string.Join(":", bytes.Select(b => b.ToString("x2")));
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;
}
