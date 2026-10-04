Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$InstallDir = Join-Path $Root "tools\dotnet"
$Installer = Join-Path $env:TEMP "asterplay-dotnet-install.ps1"
$SdkVersion = "8.0.425"

Write-Host "=== AsterPlay portable .NET 8 SDK bootstrap ==="
Write-Host "Target: $InstallDir"
Write-Host "SDK:    $SdkVersion"

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Invoke-WebRequest -UseBasicParsing "https://dot.net/v1/dotnet-install.ps1" -OutFile $Installer

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Installer -Version $SdkVersion -Architecture x64 -InstallDir $InstallDir -NoPath

if ($LASTEXITCODE -ne 0) { throw "Portable .NET 8 SDK bootstrap failed." }

$Dotnet = Join-Path $InstallDir "dotnet.exe"
if (-not (Test-Path $Dotnet)) { throw "dotnet.exe was not created at $Dotnet" }

Write-Host ""
Write-Host "Installed SDKs:"
& $Dotnet --list-sdks
Write-Host ""
Write-Host "Portable SDK is ready. Run build-windows.cmd."
