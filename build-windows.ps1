Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\AsterPlay\AsterPlay.csproj"
$Dist = Join-Path $Root "dist"
$Publish = Join-Path $Dist "AsterPlay"
$MpvDir = if ($env:ASTERPLAY_MPV_DIR) { $env:ASTERPLAY_MPV_DIR } else { Join-Path $Root "third_party\mpv" }

$PortableDotnet = Join-Path $Root "tools\dotnet\dotnet.exe"
$Dotnet = $null

if ($env:ASTERPLAY_DOTNET -and (Test-Path $env:ASTERPLAY_DOTNET)) {
    $Dotnet = $env:ASTERPLAY_DOTNET
} elseif (Test-Path $PortableDotnet) {
    $Dotnet = $PortableDotnet
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) { $Dotnet = $dotnetCommand.Source }
}

Write-Host "=== AsterPlay Windows x64 local build ==="
Write-Host "Project: $Project"
Write-Host "mpv:     $MpvDir"

if (-not $Dotnet) { throw ".NET 8 SDK was not found. Run bootstrap-dotnet.cmd or provide tools\dotnet\dotnet.exe." }

$sdkList = @(& $Dotnet --list-sdks 2>&1)
if ($LASTEXITCODE -ne 0 -or -not ($sdkList | Where-Object { $_ -match '^8\.' })) { throw ".NET 8 SDK is required to compile AsterPlay." }

$selectedSdk = ($sdkList | Where-Object { $_ -match '^8\.' } | Select-Object -Last 1)
Write-Host "dotnet:   $Dotnet"
Write-Host ".NET SDK: $selectedSdk"

$running = Get-Process -Name "AsterPlay" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running AsterPlay process..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

if (Test-Path $Publish) {
    try { Remove-Item $Publish -Recurse -Force }
    catch { throw "Could not clean '$Publish'. Close AsterPlay or any process using that folder and try again. $($_.Exception.Message)" }
}
New-Item -ItemType Directory -Force -Path $Publish | Out-Null

& $Dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $Publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$mpv = Join-Path $MpvDir "libmpv-2.dll"
if (-not (Test-Path $mpv)) {
    $BootstrapMpv = Join-Path $Root "bootstrap-mpv.ps1"
    Write-Host "libmpv is missing; preparing it automatically..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $BootstrapMpv
    if ($LASTEXITCODE -ne 0) { throw "libmpv bootstrap failed." }
}

if (-not (Test-Path $mpv)) { throw "libmpv-2.dll is still missing after bootstrap." }
Copy-Item (Join-Path $MpvDir "*.dll") $Publish -Force

$zip = Join-Path $Dist "AsterPlay-Windows-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $Publish "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "Build complete:"
Write-Host "  Folder: $Publish"
Write-Host "  ZIP:    $zip"
