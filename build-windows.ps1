<#
.SYNOPSIS
  Builds AirPlayPro-Setup-<version>.exe and AirPlaySpeaker-Setup-<version>.exe on this Windows PC, no GitHub needed.

.DESCRIPTION
  1. Installs any missing build tools with winget: MSYS2, .NET 8 SDK, Inno Setup 6.
  2. Builds the UxPlay engine in MSYS2 (scripts/build-uxplay.sh).
  3. Builds both tray apps (AirPlay Pro and AirPlay Speaker) and their installers.
  The installers end up in .\dist.

.EXAMPLE
  .\build-windows.ps1
  .\build-windows.ps1 -UxPlayRef v1.73.7      # build on the last stable UxPlay release
#>
param(
    # UxPlay tag, branch or full commit hash. Default: development snapshot with
    # YouTube/HLS crash fixes. Use "v1.73.7" for the last stable release.
    [string]$UxPlayRef = "b29411ffe30eb6b62b755af5ce6a03b2a25c2371",
    [string]$Version = "0.3.1",
    # Don't install missing tools; fail instead.
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Msys = "C:\msys64"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [Environment]::GetEnvironmentVariable("Path", "User")
}

function Install-WithWinget($id, $name) {
    if ($NoInstall) { throw "$name is not installed (and -NoInstall was given)." }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "$name is missing and winget isn't available. Install $name manually, then run this again."
    }
    Step "Installing $name"
    winget install --id $id -e --source winget --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { throw "winget couldn't install $name (exit code $LASTEXITCODE)." }
    Refresh-Path
}

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Invoke-Msys($command) {
    # Run in the UCRT64 environment, starting in the project folder.
    $env:MSYSTEM = "UCRT64"
    $env:CHERE_INVOKING = "1"
    & "$Msys\usr\bin\bash.exe" -lc $command
    if ($LASTEXITCODE -ne 0) { throw "MSYS2 command failed (exit code $LASTEXITCODE): $command" }
}

# --- Tools ---------------------------------------------------------------------
Step "Checking build tools"

if (-not (Test-Path "$Msys\usr\bin\bash.exe")) { Install-WithWinget "MSYS2.MSYS2" "MSYS2" }
if (-not (Test-Path "$Msys\usr\bin\bash.exe")) { throw "MSYS2 wasn't found at $Msys after installing." }

$hasDotnet8 = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks) -match "^8\.")
if (-not $hasDotnet8) { Install-WithWinget "Microsoft.DotNet.SDK.8" ".NET 8 SDK" }

if (-not (Find-Iscc)) { Install-WithWinget "JRSoftware.InnoSetup" "Inno Setup 6" }
$Iscc = Find-Iscc
if (-not $Iscc) { throw "Inno Setup's ISCC.exe wasn't found after installing." }

# First run of a fresh MSYS2 updates its core; the second pass finishes the update.
Step "Updating MSYS2 packages"
Push-Location $Root
try {
    Invoke-Msys "pacman -Syu --noconfirm"
    Invoke-Msys "pacman -Syu --noconfirm"

    # --- Engine ----------------------------------------------------------------
    Step "Building the UxPlay engine ($UxPlayRef)"
    Invoke-Msys "UXPLAY_REF='$UxPlayRef' ./scripts/build-uxplay.sh dist/uxplay"
    Invoke-Msys "./dist/uxplay/bin/uxplay.exe -h | head -2"

    # --- Tray apps -------------------------------------------------------------
    # AirPlay Pro (mirroring + video + audio) and AirPlay Speaker (audio only) share
    # the same source and the same engine; each gets its own installer.
    $apps = @(
        @{ Project = "AirPlayPro";     Out = "app";         Iss = "AirPlayPro.iss" },
        @{ Project = "AirPlaySpeaker"; Out = "speaker-app"; Iss = "AirPlaySpeaker.iss" }
    )
    foreach ($a in $apps) {
        Step "Building $($a.Project)"
        $out = "$Root\dist\$($a.Out)"
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }
        dotnet publish "$Root\src\$($a.Project)\$($a.Project).csproj" -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($a.Project)." }

        Step "Building the $($a.Project) installer"
        & $Iscc "/DAppVersion=$Version" "$Root\installer\$($a.Iss)"
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for $($a.Project)." }
    }
}
finally {
    Pop-Location
}

Write-Host ""
foreach ($name in "AirPlayPro", "AirPlaySpeaker") {
    $setup = Get-ChildItem "$Root\dist\$name-Setup-$Version.exe"
    Write-Host "Done: $($setup.FullName) ($([math]::Round($setup.Length / 1MB)) MB)" -ForegroundColor Green
}
