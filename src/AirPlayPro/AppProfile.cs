namespace AirPlayPro;

/// <summary>
/// What makes AirPlay Pro (mirroring + video + audio) and AirPlay Speaker (audio only,
/// shown to Apple devices as a speaker) different. Both apps compile from this same
/// source; AirPlaySpeaker.csproj defines SPEAKER.
/// </summary>
internal static class AppProfile
{
#if SPEAKER
    public static readonly bool IsSpeaker = true;
    public const string Name = "AirPlay Speaker";
    public const string Id = "AirPlaySpeaker";
    /// <summary>Its own ports (TCP/UDP 7200-7202), so it can run next to AirPlay Pro.</summary>
    public const string BasePort = "7200";
    public static string DefaultDeviceName => Environment.MachineName + " Speaker";
#else
    public static readonly bool IsSpeaker = false;
    public const string Name = "AirPlay Pro";
    public const string Id = "AirPlayPro";
    /// <summary>Empty: UxPlay's legacy ports (TCP 7000/7001/7100, UDP 6000/6001/7011).</summary>
    public const string BasePort = "";
    public static string DefaultDeviceName => Environment.MachineName;
#endif
}
