# AirPlay Pro

Turn a Windows 11 PC into an AirPlay receiver, like an Apple TV. Mirror an iPhone,
iPad or Mac screen to the PC, or play its audio through the PC's speakers.

AirPlay Pro is a small system-tray app around [UxPlay](https://github.com/FDH2/UxPlay),
an open-source AirPlay mirroring server that handles Apple's protocol and
encryption. GStreamer decodes the video and audio.

```
iPhone / iPad / Mac ── Wi-Fi ──►  uxplay.exe (AirPlay protocol, GStreamer playback)
                                       ▲
                    AirPlayPro.exe (tray icon, settings, auto-restart, logs)
```

## Using it

1. Install **Bonjour Print Services for Windows** from Apple
   (<https://support.apple.com/en-us/106380>), or install iTunes, which includes it.
   iPhones find the PC through Bonjour.
2. Run `AirPlayPro-Setup-x.y.z.exe`. The installer adds a firewall rule for the receiver.
3. Make sure Windows treats your Wi-Fi as a **Private** network: Settings → Network &
   internet → Wi-Fi → your network → *Private network*.
4. On the iPhone, open Control Center → **Screen Mirroring** → pick your PC's name.

The tray icon's dot shows the state: blue means ready, green means connected,
orange means starting, red means there's a problem, and grey means off.
Right-click the icon for settings, the log and connection help.

### Settings

| Setting | What it does | UxPlay flag |
|---|---|---|
| Name on iPhone/iPad | Name shown in the Screen Mirroring list | `-n`, `-nh` |
| Video output | `d3d12videosink` (default), `d3d11videosink`, `glimagesink`, `autovideosink` | `-vs` |
| Full screen | Open the mirrored screen full screen | `-fs` |
| Audio output | `wasapisink` (default), `directsoundsink`, `autoaudiosink` | `-as` |
| Require a PIN | Show a PIN that the device must enter. Leave the PIN empty for a new random one each time. | `-pin` |
| Let a new device take over | A second device can replace the current one | `-nohold` |
| Play video from the YouTube app | The YouTube app's AirPlay button (iPhone/iPad) plays video, not just audio: the PC streams it straight from YouTube. On by default. AirPlay from a web browser still isn't supported, so use Screen Mirroring for that. | `-hls` |
| Extra options | Passed to UxPlay unchanged, for advanced use | |

The receiver always uses fixed ports so the firewall rule works:
TCP 7000, 7001, 7100 and UDP 6000, 6001, 7011.

Settings are stored in `%LOCALAPPDATA%\AirPlayPro\settings.json` and the log in
`%LOCALAPPDATA%\AirPlayPro\receiver.log`.

## Building

You need a Windows build machine. The easiest way is GitHub Actions: push this
folder to a GitHub repository and the [workflow](.github/workflows/build.yml)
builds `AirPlayPro-Setup-*.exe` on every push. Download it from the run's
*Artifacts* section. Pushing a tag such as `v0.1.0` also attaches the installer
to a GitHub Release.

To build on a Windows PC with one command, open PowerShell in this folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
```

It installs whatever is missing (MSYS2, .NET 8 SDK, Inno Setup 6) with `winget`,
builds everything, and puts the installer in `dist\`. The first run takes 10–20
minutes. Add `-UxPlayRef <tag-or-commit>` to build a different UxPlay version.

To build by hand on Windows:

1. Install [MSYS2](https://www.msys2.org), the [.NET 8 SDK](https://dotnet.microsoft.com/download)
   and [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. In the **MSYS2 UCRT64** shell, from this folder, build the engine:
   ```bash
   ./scripts/build-uxplay.sh dist/uxplay
   ```
   This installs the needed MSYS2 packages, builds UxPlay (`UXPLAY_REF`, default
   `v1.73.7`), and copies `uxplay.exe` with all its DLLs and GStreamer plugins into
   `dist/uxplay`. You don't need Apple's Bonjour SDK. If it isn't installed, the
   script makes a small stand-in from Apple's open-source `dns_sd.h`, because UxPlay
   loads `dnssd.dll` at runtime.
3. In PowerShell, build the tray app and the installer:
   ```powershell
   dotnet publish src/AirPlayPro/AirPlayPro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/app
   iscc installer/AirPlayPro.iss
   ```

To try the tray app without the installer, copy `dist/uxplay` to `dist/app/uxplay`
and run `dist/app/AirPlayPro.exe`.

## Project layout

```
src/AirPlayPro/         C# (.NET 8, WinForms) tray app
  TrayApp.cs            tray icon, menu, notifications
  ReceiverHost.cs       starts and watches uxplay.exe, sets up GStreamer, writes the log
  SettingsForm.cs       settings window
  AppSettings.cs        settings model (JSON)
  SystemIntegration.cs  start at sign-in, Bonjour check, firewall repair
  TrayIcons.cs          icons drawn in code
scripts/build-uxplay.sh builds and packages the UxPlay engine (MSYS2)
build-windows.ps1       one-command local build on Windows
installer/              Inno Setup script
.github/workflows/      Windows CI build
```

## Troubleshooting

- **The PC doesn't appear on the iPhone.** Check that Bonjour is installed, that the
  network is set to Private, and that both devices are on the same network (not a
  guest network). Use *Fix firewall* in the tray menu. Some routers block discovery
  through "AP/client isolation" or multicast filtering.
- **The PC appears but the connection fails.** Usually the firewall. Use *Fix firewall*.
- **Black or stuttering video.** Try `d3d11videosink` under Video output.
- **No sound.** Try `directsoundsink` under Audio output.
- **YouTube app's AirPlay button plays sound but no picture.** Turn on *Play video from
  the YouTube app* in Settings. AirPlay from YouTube in a web browser isn't supported;
  use Screen Mirroring instead.
- **More detail.** Add `-d` under Extra options and open the log.

## License

GPL-3.0, because it ships UxPlay. See [LICENSE](LICENSE) and
[LICENSE-NOTICE.txt](LICENSE-NOTICE.txt). AirPlay is a trademark of Apple Inc.,
and this project has no connection to Apple.
