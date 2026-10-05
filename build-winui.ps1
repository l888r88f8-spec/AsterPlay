Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\AsterPlay.WinUI\AsterPlay.WinUI.csproj"
$Publish = Join-Path $Root "dist\AsterPlay.WinUI"
$PortableDotnet = Join-Path $Root "tools\dotnet\dotnet.exe"

if (-not (Test-Path $PortableDotnet)) {
    throw ".NET 8 SDK was not found. Run bootstrap-dotnet.cmd first."
}

if (Test-Path $Publish) {
    Remove-Item $Publish -Recurse -Force
}

Write-Host "=== AsterPlay WinUI 3 x64 migration build ==="
Write-Host "Project: $Project"

& $PortableDotnet publish $Project -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=false -o $Publish
if ($LASTEXITCODE -ne 0) {
    throw "WinUI 3 publish failed."
}

$MpvDir = if ($env:ASTERPLAY_MPV_DIR) { $env:ASTERPLAY_MPV_DIR } else { Join-Path $Root "third_party\mpv" }
$usingBundledMpv = -not $env:ASTERPLAY_MPV_DIR

if ($usingBundledMpv) {
    $BootstrapMpv = Join-Path $Root "bootstrap-mpv.ps1"
    Write-Host "Verifying pinned libmpv runtime for WinUI player..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $BootstrapMpv
    if ($LASTEXITCODE -ne 0) {
        throw "libmpv bootstrap failed."
    }
}

$mpv = Join-Path $MpvDir "libmpv-2.dll"
if (-not (Test-Path $mpv)) {
    throw "libmpv-2.dll is missing. Provide ASTERPLAY_MPV_DIR or use the bundled bootstrap."
}

Copy-Item (Join-Path $MpvDir "*.dll") $Publish -Force

$runtimeSource = Join-Path $MpvDir "RUNTIME-SOURCE.txt"
if (Test-Path $runtimeSource) {
    Copy-Item $runtimeSource (Join-Path $Publish "RUNTIME-SOURCE.txt") -Force
}

$exe = Join-Path $Publish "AsterPlay.WinUI.exe"
if (-not (Test-Path $exe)) {
    throw "WinUI 3 publish self-check failed: AsterPlay.WinUI.exe is missing."
}

$liquidRuntime = Join-Path $Publish "CustomEffectRuntimeNative.dll"
if (-not (Test-Path $liquidRuntime)) {
    throw "LiquidGlassWinUI native runtime is missing from the publish output."
}

$publishedMpv = Join-Path $Publish "libmpv-2.dll"
if (-not (Test-Path $publishedMpv)) {
    throw "libmpv-2.dll is missing from the WinUI publish output."
}

Write-Host "WinUI 3 publish self-check passed."
Write-Host "  EXE: $exe"
Write-Host "  LiquidGlass runtime: $liquidRuntime"
Write-Host "  libmpv runtime:      $publishedMpv"
