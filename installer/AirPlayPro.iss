; Inno Setup script for AirPlay Pro.
; Expects the build output in ..\dist\app (tray app) and ..\dist\uxplay (engine).
; Build with: iscc /DAppVersion=0.3.1 installer\AirPlayPro.iss

#ifndef AppVersion
  #define AppVersion "0.3.1"
#endif

[Setup]
AppId={{6F3B1E2A-9C4D-4E8B-A1F7-2D5C8E9B0A13}
AppName=AirPlay Pro
AppVersion={#AppVersion}
AppPublisher=AirPlay Pro
DefaultDirName={autopf}\AirPlay Pro
DefaultGroupName=AirPlay Pro
UninstallDisplayIcon={app}\AirPlayPro.exe
OutputDir=..\dist
OutputBaseFilename=AirPlayPro-Setup-{#AppVersion}
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
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\uxplay\*"; DestDir: "{app}\uxplay"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE-NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\AirPlay Pro"; Filename: "{app}\AirPlayPro.exe"
Name: "{group}\Uninstall AirPlay Pro"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AirPlay Pro"; Filename: "{app}\AirPlayPro.exe"; Tasks: desktopicon

[Run]
; Let iPhones/iPads reach the receiver on private (home/work) networks.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirPlay Pro Receiver"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AirPlay Pro Receiver"" dir=in action=allow program=""{app}\uxplay\bin\uxplay.exe"" enable=yes profile=private,domain"; Flags: runhidden
Filename: "{app}\AirPlayPro.exe"; Description: "Start AirPlay Pro now"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Stop only this app and its own engine (the other AirPlay app may be running too).
Filename: "{cmd}"; Parameters: "/c taskkill /f /im AirPlayPro.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-Process uxplay -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -like '{app}\*' } | Stop-Process -Force"""; Flags: runhidden; RunOnceId: "KillEngine"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AirPlay Pro Receiver"""; Flags: runhidden; RunOnceId: "DelFirewall"

[Code]
function BonjourInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\Bonjour Service');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not BonjourInstalled()) and (not WizardSilent()) then
    MsgBox('AirPlay Pro needs Apple''s Bonjour service so iPhones and iPads can find this PC.' + #13#10#13#10 +
           'Install "Bonjour Print Services for Windows" from Apple (support.apple.com/en-us/106380), ' +
           'or install iTunes, which includes it.', mbInformation, MB_OK);
end;
