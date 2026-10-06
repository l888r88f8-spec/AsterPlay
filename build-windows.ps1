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

# LiquidGlassWinUI 1.0.3 ships a native compatibility runtime whose private
# wuceffectsi.dll RVAs target Windows App SDK 2.2.0. Windows App SDK 2.5.1 keeps
# the same EffectType ABI used by the runtime, but the code/data moved. Patch only
# the five build-specific RVAs after strict signature-count validation.
function Replace-LiquidGlassRva {
    param(
        [byte[]]$Buffer,
        [uint32]$OldValue,
        [uint32]$NewValue,
        [int]$ExpectedCount,
        [string]$Name
    )

    $oldBytes = [BitConverter]::GetBytes($OldValue)
    $newBytes = [BitConverter]::GetBytes($NewValue)
    $matches = [System.Collections.Generic.List[int]]::new()

    for ($i = 0; $i -le $Buffer.Length - 4; $i++) {
        if ($Buffer[$i] -eq $oldBytes[0] -and
            $Buffer[$i + 1] -eq $oldBytes[1] -and
            $Buffer[$i + 2] -eq $oldBytes[2] -and
            $Buffer[$i + 3] -eq $oldBytes[3]) {
            $matches.Add($i)
        }
    }

    if ($matches.Count -ne $ExpectedCount) {
        throw "LiquidGlass SDK 2.5.1 compatibility patch refused: $Name expected $ExpectedCount match(es), found $($matches.Count)."
    }

    foreach ($offset in $matches) {
        for ($j = 0; $j -lt 4; $j++) {
            $Buffer[$offset + $j] = $newBytes[$j]
        }
    }

    Write-Host ("  Patched {0}: 0x{1:X} -> 0x{2:X} ({3} occurrence(s))" -f $Name, $OldValue, $NewValue, $matches.Count)
}

$liquidGlassNative = Join-Path $Publish "CustomEffectRuntimeNative.dll"
if (-not (Test-Path $liquidGlassNative)) {
    throw "LiquidGlass native runtime is missing after publish: CustomEffectRuntimeNative.dll"
}

Write-Host "Applying LiquidGlassWinUI compatibility patch for Windows App SDK 2.5.1..."
$liquidGlassBytes = [IO.File]::ReadAllBytes($liquidGlassNative)

# Verified by function-level comparison of the SDK 2.2.0 and 2.5.1
# self-contained wuceffectsi.dll binaries used by AsterPlay:
#   EffectType::FromGuid                 0x17C48 -> 0x193D8
#   EffectType table                     0x62150 -> 0x64150
#   EffectType::GetBounds                0x1E040 -> 0x1F7D0
#   EffectType::CalcInputBounds          0x1D700 -> 0x1EE90
#   DirectPropertyUpdater function vtbl  0x451E0 -> 0x471E0
Replace-LiquidGlassRva $liquidGlassBytes 0x00017C48 0x000193D8 5 "EffectType::FromGuid"
Replace-LiquidGlassRva $liquidGlassBytes 0x00062150 0x00064150 1 "EffectType table"
Replace-LiquidGlassRva $liquidGlassBytes 0x0001E040 0x0001F7D0 1 "EffectType::GetBounds"
Replace-LiquidGlassRva $liquidGlassBytes 0x0001D700 0x0001EE90 1 "EffectType::CalcInputBounds"
Replace-LiquidGlassRva $liquidGlassBytes 0x000451E0 0x000471E0 1 "DirectPropertyUpdater vtable"

[IO.File]::WriteAllBytes($liquidGlassNative, $liquidGlassBytes)

@(
    "LiquidGlassWinUI 1.0.3 native runtime compatibility patch",
    "Target: Windows App SDK 2.5.1",
    "Architecture: x64",
    "EffectType::FromGuid: 0x17C48 -> 0x193D8",
    "EffectType table: 0x62150 -> 0x64150",
    "EffectType::GetBounds: 0x1E040 -> 0x1F7D0",
    "EffectType::CalcInputBounds: 0x1D700 -> 0x1EE90",
    "DirectPropertyUpdater vtable: 0x451E0 -> 0x471E0"
) | Set-Content -Encoding UTF8 (Join-Path $Publish "LIQUIDGLASS-COMPAT.txt")

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
    "LiquidGlassWinUI.dll",
    "CustomEffectRuntimeNative.dll",
    "LIQUIDGLASS-COMPAT.txt",
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
