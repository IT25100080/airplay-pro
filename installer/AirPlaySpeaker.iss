; Inno Setup script for AirPlay Speaker (audio-only receiver; the PC shows up as an AirPlay speaker).
; Expects the build output in ..\dist\speaker-app (tray app) and ..\dist\uxplay (engine).
; Build with: iscc /DAppVersion=0.3.0 installer\AirPlaySpeaker.iss

#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif

[Setup]
AppId={{B2D84C61-3E7A-4F59-9C0B-71A6E5F3D428}
AppName=AirPlay Speaker
AppVersion={#AppVersion}
AppPublisher=AirPlay Speaker
DefaultDirName={autopf}\AirPlay Speaker
DefaultGroupName=AirPlay Speaker
UninstallDisplayIcon={app}\AirPlaySpeaker.exe
OutputDir=..\dist
OutputBaseFilename=AirPlaySpeaker-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Admin is needed to add the firewall rule for the receiver.
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=yes
LicenseFile=..\LICENSE-NOTICE.txt

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\speaker-app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\uxplay\*"; DestDir: "{app}\uxplay"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE-NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\AirPlay Speaker"; Filename: "{app}\AirPlaySpeaker.exe"
Name: "{group}\Uninstall AirPlay Speaker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AirPlay Speaker"; Filename: "{app}\AirPlaySpeaker.exe"; Tasks: desktopicon

[Run]
; Let iPhones, iPads and Macs reach the speaker on private (home/work) networks.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirPlay Speaker Receiver"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AirPlay Speaker Receiver"" dir=in action=allow program=""{app}\uxplay\bin\uxplay.exe"" enable=yes profile=private,domain"; Flags: runhidden
Filename: "{app}\AirPlaySpeaker.exe"; Description: "Start AirPlay Speaker now"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Stop only this app and its own engine (the other AirPlay app may be running too).
Filename: "{cmd}"; Parameters: "/c taskkill /f /im AirPlaySpeaker.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-Process uxplay -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -like '{app}\*' } | Stop-Process -Force"""; Flags: runhidden; RunOnceId: "KillEngine"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirPlay Speaker Receiver"""; Flags: runhidden; RunOnceId: "DelFirewall"

[Code]
function BonjourInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\Bonjour Service');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not BonjourInstalled()) and (not WizardSilent()) then
    MsgBox('AirPlay Speaker needs Apple''s Bonjour service so iPhones, iPads and Macs can find this PC.' + #13#10#13#10 +
           'Install "Bonjour Print Services for Windows" from Apple (support.apple.com/en-us/106380), ' +
           'or install iTunes, which includes it.', mbInformation, MB_OK);
end;
