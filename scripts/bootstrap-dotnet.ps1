Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$InstallDir = Join-Path $Root "tools\dotnet"
$Installer = Join-Path $env:TEMP "asterplay-dotnet-install.ps1"
$SdkVersion = "8.0.425"

Write-Host "=== AsterPlay portable .NET 8 SDK bootstrap ==="
Write-Host "Target: $InstallDir"
Write-Host "SDK:    $SdkVersion"

$Dotnet = Join-Path $InstallDir "dotnet.exe"
if (Test-Path $Dotnet) {
    $installedSdks = @(& $Dotnet --list-sdks 2>$null)
    if ($LASTEXITCODE -eq 0 -and ($installedSdks | Where-Object { $_ -match ("^" + [Regex]::Escape($SdkVersion) + "\s") })) {
        Write-Host "Pinned SDK is already installed; skipping download."
        & $Dotnet --list-sdks
        exit 0
    }
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Invoke-WebRequest -UseBasicParsing "https://dot.net/v1/dotnet-install.ps1" -OutFile $Installer

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Installer -Version $SdkVersion -Architecture x64 -InstallDir $InstallDir -NoPath

if ($LASTEXITCODE -ne 0) { throw "Portable .NET 8 SDK bootstrap failed." }

if (-not (Test-Path $Dotnet)) { throw "dotnet.exe was not created at $Dotnet" }

Write-Host ""
Write-Host "Installed SDKs:"
& $Dotnet --list-sdks
Write-Host ""
Write-Host "Portable SDK is ready. Run build-windows.cmd."
