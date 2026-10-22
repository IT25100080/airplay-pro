using System.Diagnostics;
using System.Text;

namespace AirPlayPro;

public enum ReceiverState { Stopped, Starting, Waiting, Connected, Error }

/// <summary>
/// Runs the bundled UxPlay engine (uxplay.exe) as a child process, keeps it alive,
/// and turns its console output into state changes and a log file.
/// </summary>
public sealed class ReceiverHost : IDisposable
{
    private const int MaxQuickRestarts = 5;

    private readonly object _gate = new();
    private readonly object _logGate = new();
    private Process? _process;
    private bool _wantRunning;
    private int _quickRestarts;
    private DateTime _lastStart;
    private StreamWriter? _log;

    public ReceiverState State { get; private set; } = ReceiverState.Stopped;
    public string? LastError { get; private set; }

    /// <summary>Raised on a thread-pool thread; marshal to the UI thread before touching controls.</summary>
    public event Action<ReceiverState>? StateChanged;

    public static string EngineDir => Path.Combine(AppContext.BaseDirectory, "uxplay");
    public static string EngineExe => Path.Combine(EngineDir, "bin", "uxplay.exe");
    public static string LogPath => Path.Combine(AppSettings.DataDir, "receiver.log");
    public static string KeyPath => Path.Combine(AppSettings.DataDir, "uxplay.pem");

    public void Start(AppSettings settings)
    {
        lock (_gate)
        {
            _wantRunning = true;
            _quickRestarts = 0;
            StartProcess(settings);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _wantRunning = false;
            KillProcess();
            CloseLog();
        }
        SetState(ReceiverState.Stopped);
    }

    public void Restart(AppSettings settings)
    {
        Stop();
        Start(settings);
    }

    private void StartProcess(AppSettings settings)
    {
        if (!File.Exists(EngineExe))
        {
            Fail($"Receiver engine not found at {EngineExe}. Reinstall AirPlay Pro.");
            return;
        }

        SetState(ReceiverState.Starting);
        OpenLog();

        var psi = new ProcessStartInfo(EngineExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.Combine(EngineDir, "bin"),
        };
        foreach (var arg in BuildArguments(settings))
            psi.ArgumentList.Add(arg);
        ConfigureGStreamerEnvironment(psi);

        WriteLog($"--- starting: uxplay {string.Join(' ', psi.ArgumentList)}");

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => OnOutput(e.Data);
        p.ErrorDataReceived += (_, e) => OnOutput(e.Data);
        p.Exited += (_, _) => OnExited(p, settings);

        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            Fail($"Could not start the receiver engine: {ex.Message}");
            return;
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _process = p;
        _lastStart = DateTime.UtcNow;
        SetState(ReceiverState.Waiting);
    }

    public static List<string> BuildArguments(AppSettings s)
    {
        var args = new List<string>();

        args.Add("-n");
        args.Add(string.IsNullOrWhiteSpace(s.DeviceName) ? Environment.MachineName : s.DeviceName.Trim());
        if (!s.AppendHostname) args.Add("-nh");

        // Same identity every run: without a stored key UxPlay makes a new one on each
        // start, and Apple devices that paired before then ask for a PIN again.
        if (!string.IsNullOrEmpty(s.DeviceId))
        {
            args.Add("-m");
            args.Add(s.DeviceId);
        }
        args.Add("-key");
        args.Add(KeyPath);

        // Fixed legacy ports (TCP 7000/7001/7100, UDP 6000/6001/7011) so the
        // installer's firewall rules always match.
        args.Add("-p");

        args.Add("-vs");
        args.Add(s.VideoSink);
        args.Add("-as");
        args.Add(s.AudioSink);
        if (s.Fullscreen) args.Add("-fs");

        if (s.RequirePin)
        {
            args.Add("-pin");
            if (s.Pin.Length == 4 && s.Pin.All(char.IsDigit)) args.Add(s.Pin);
        }

        if (s.AllowTakeover) args.Add("-nohold");
        if (s.YouTubeStreaming) args.Add("-hls");

        if (!string.IsNullOrWhiteSpace(s.ExtraArgs))
            args.AddRange(s.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return args;
    }

    /// <summary>
    /// Point GStreamer at the plugins bundled next to uxplay.exe instead of any
    /// system-wide install, and keep its plugin cache in our own data folder.
    /// </summary>
    private static void ConfigureGStreamerEnvironment(ProcessStartInfo psi)
    {
        string bin = Path.Combine(EngineDir, "bin");
        string plugins = Path.Combine(EngineDir, "lib", "gstreamer-1.0");
        string scanner = Path.Combine(EngineDir, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");

        psi.Environment["PATH"] = bin + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
        psi.Environment["GST_PLUGIN_SYSTEM_PATH_1_0"] = plugins;
        psi.Environment["GST_PLUGIN_PATH_1_0"] = plugins;
        psi.Environment["GST_REGISTRY_1_0"] = Path.Combine(AppSettings.DataDir, "gst-registry.bin");
        if (File.Exists(scanner))
            psi.Environment["GST_PLUGIN_SCANNER_1_0"] = scanner;

        // YouTube streaming fetches video over HTTPS, which needs GLib's TLS module
        // and a CA bundle; both are bundled with the engine.
        psi.Environment["GIO_MODULE_DIR"] = Path.Combine(EngineDir, "lib", "gio", "modules");
        string caBundle = Path.Combine(EngineDir, "etc", "ssl", "certs", "ca-bundle.crt");
        if (File.Exists(caBundle))
            psi.Environment["SSL_CERT_FILE"] = caBundle;
        psi.Environment["GSETTINGS_SCHEMA_DIR"] = Path.Combine(EngineDir, "share", "glib-2.0", "schemas");
    }

    private void OnOutput(string? line)
    {
        if (line is null) return;
        WriteLog(line);

        // UxPlay's console messages are not a stable API, so this is best effort:
        // it only drives the tray tooltip, never correctness.
        string l = line.ToLowerInvariant();
        if (l.Contains("accepted") && l.Contains("client"))
            SetState(ReceiverState.Connected);
        else if (l.Contains("connection closed") || l.Contains("teardown") || l.Contains("client disconnected"))
            SetState(ReceiverState.Waiting);
    }

    private void OnExited(Process p, AppSettings settings)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(p, _process)) return;
            int code = SafeExitCode(p);
            WriteLog($"--- engine exited with code {code}");
            _process = null;
            p.Dispose();

            if (!_wantRunning) return;

            // Restart after crashes, but give up if it keeps dying right away
            // (bad setting, port in use, missing plugin...).
            bool quickCrash = DateTime.UtcNow - _lastStart < TimeSpan.FromSeconds(15);
            _quickRestarts = quickCrash ? _quickRestarts + 1 : 0;
            if (_quickRestarts >= MaxQuickRestarts)
            {
                _wantRunning = false;
                Fail("The receiver keeps stopping right after it starts. Open the log for details " +
                     "(common causes: another AirPlay receiver is running, or a video/audio output setting isn't supported).");
                return;
            }
        }

        Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * (_quickRestarts + 1), 10))).ContinueWith(_ =>
        {
            lock (_gate)
            {
                if (_wantRunning && _process is null) StartProcess(settings);
            }
        });
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }

    private void Fail(string message)
    {
        LastError = message;
        WriteLog("!!! " + message);
        SetState(ReceiverState.Error);
    }

    private void SetState(ReceiverState state)
    {
        if (State == state) return;
        State = state;
        if (state != ReceiverState.Error) LastError = null;
        StateChanged?.Invoke(state);
    }

    private void KillProcess()
    {
        var p = _process;
        _process = null;
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
        }
        catch (Exception) { /* already gone */ }
        p.Dispose();
    }

    private void OpenLog()
    {
        lock (_logGate)
        {
            if (_log is not null) return;
            Directory.CreateDirectory(AppSettings.DataDir);
            // Keep the previous run's log around for troubleshooting, but don't grow forever.
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5_000_000)
                File.Move(LogPath, LogPath + ".old", overwrite: true);
            _log = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
            };
        }
    }

    private void WriteLog(string line)
    {
        lock (_logGate)
        {
            try { _log?.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"); } catch (Exception) { }
        }
    }

    private void CloseLog()
    {
        lock (_logGate)
        {
            _log?.Dispose();
            _log = null;
        }
    }

    public void Dispose() => Stop();
}
