Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\AsterPlay.WinUI\AsterPlay.WinUI.csproj"
$Dist = Join-Path $Root "dist"
$Publish = Join-Path $Dist "AsterPlay"
$PortableDotnet = Join-Path $Root "tools\dotnet\dotnet.exe"
$MpvDir = if ($env:ASTERPLAY_MPV_DIR) { $env:ASTERPLAY_MPV_DIR } else { Join-Path $Root "third_party\mpv" }

$Dotnet = $null
if ($env:ASTERPLAY_DOTNET -and (Test-Path $env:ASTERPLAY_DOTNET)) {
    $Dotnet = $env:ASTERPLAY_DOTNET
} elseif (Test-Path $PortableDotnet) {
    $Dotnet = $PortableDotnet
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) { $Dotnet = $dotnetCommand.Source }
}

Write-Host "=== AsterPlay WinUI 3 Windows x64 build ==="
Write-Host "Project: $Project"
Write-Host "mpv:     $MpvDir"

if (-not $Dotnet) {
    throw ".NET 8 SDK was not found. Run bootstrap-dotnet.cmd or provide tools\dotnet\dotnet.exe."
}

$sdkList = @(& $Dotnet --list-sdks 2>&1)
if ($LASTEXITCODE -ne 0 -or -not ($sdkList | Where-Object { $_ -match '^8\.' })) {
    throw ".NET 8 SDK is required to compile AsterPlay."
}

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
    catch {
        throw "Could not clean '$Publish'. Close AsterPlay or any process using that folder and try again. $($_.Exception.Message)"
    }
}

New-Item -ItemType Directory -Force -Path $Publish | Out-Null

& $Dotnet publish $Project -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=false -o $Publish
if ($LASTEXITCODE -ne 0) {
    throw "WinUI 3 publish failed."
}

$usingBundledMpv = -not $env:ASTERPLAY_MPV_DIR
if ($usingBundledMpv) {
    $BootstrapMpv = Join-Path $Root "bootstrap-mpv.ps1"
    Write-Host "Verifying pinned libmpv runtime..."
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

$requiredFiles = @(
    "AsterPlay.exe",
    "AsterPlay.Core.dll",
    "libmpv-2.dll",
    "coreclr.dll",
    "hostfxr.dll",
    "hostpolicy.dll"
)

$missingFiles = @(
    $requiredFiles |
        Where-Object { -not (Test-Path (Join-Path $Publish $_)) }
)

if ($missingFiles.Count -gt 0) {
    throw "Publish self-check failed. Missing: $($missingFiles -join ', ')"
}

@(
    "AsterPlay WinUI 3 Windows x64",
    "UI: WinUI 3",
    "Configuration: Release",
    "RID: win-x64",
    "SelfContained: true",
    "PublishSingleFile: false",
    "WindowsAppSDKSelfContained: true",
    "SDK: $selectedSdk",
    "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
) | Set-Content -Encoding UTF8 (Join-Path $Publish "BUILD-INFO.txt")

Write-Host ""
Write-Host "Publish self-check passed:"
$requiredFiles | ForEach-Object { Write-Host "  OK: $_" }
Write-Host ""
Write-Host "Build complete:"
Write-Host "  Folder: $Publish"
