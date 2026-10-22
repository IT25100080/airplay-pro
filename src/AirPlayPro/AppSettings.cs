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
    public string ExtraArgs { get; set; } = "";
    public bool StartReceiverOnLaunch { get; set; } = true;

    public static readonly string[] VideoSinks = { "d3d12videosink", "d3d11videosink", "glimagesink", "autovideosink" };
    public static readonly string[] AudioSinks = { "wasapisink", "directsoundsink", "autoaudiosink" };

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirPlayPro");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt file shouldn't stop the receiver from starting; fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;
}
