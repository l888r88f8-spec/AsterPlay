param([switch]$Full, [switch]$Unpacked)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# PowerShell 5.1 otherwise decodes native-tool output using the legacy code page.
# Match dotnet/MSBuild UTF-8 output when piping to Out-Host.
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Project = Join-Path $Root "src\AsterPlay.WinUI\AsterPlay.WinUI.csproj"
$Dist = Join-Path $Root "dist"
$Publish = Join-Path $Dist "AsterPlay"
# Default to a tidy portable single-file publish. -Unpacked restores the
# original directory-style output for troubleshooting WinUI/MPV startup.
$SingleFile = if ($Unpacked) { "false" } else { "true" }
$PortableDotnet = Join-Path $Root "tools\dotnet\dotnet.exe"
$MpvDir = if ($env:ASTERPLAY_MPV_DIR) { $env:ASTERPLAY_MPV_DIR } else { Join-Path $Root "third_party\mpv" }


function Has-Dotnet8([string]$Exe) {
    if (-not $Exe -or -not (Test-Path -LiteralPath $Exe -PathType Leaf)) { return $false }
    try {
        $sdks = @(& $Exe --list-sdks 2>$null)
        return ($LASTEXITCODE -eq 0 -and @($sdks | Where-Object { $_ -match '^8\.0\.\d+\s' }).Count -gt 0)
    } catch { return $false }
}
if ($env:OS -ne "Windows_NT" -or -not [Environment]::Is64BitOperatingSystem) {
    throw "Building AsterPlay requires Windows x64."
}
$Dotnet = $null
$candidates = @()
if ($env:ASTERPLAY_DOTNET) { $candidates += $env:ASTERPLAY_DOTNET }
$candidates += $PortableDotnet
$systemDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($systemDotnet) { $candidates += $systemDotnet.Source }
foreach ($exe in ($candidates | Select-Object -Unique)) {
    if (Has-Dotnet8 $exe) { $Dotnet = $exe; Write-Host "[OK] .NET 8 SDK already installed: $exe"; break }
}
if (-not $Dotnet) {
    Write-Host "[SETUP] Installing missing .NET 8 SDK (8.0.425)..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "scripts\bootstrap-dotnet.ps1") | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Has-Dotnet8 $PortableDotnet)) {
        throw "Unable to install .NET 8 SDK. Verify network access to Microsoft download servers."
    }
    $Dotnet = $PortableDotnet
}
$selectedSdk = (& $Dotnet --version).Trim()
Write-Host "=== AsterPlay automated Windows x64 build ==="
Write-Host "dotnet: $Dotnet"
Write-Host "SDK version: $selectedSdk"
Write-Host "mpv: $MpvDir"

# The normal app uses a pinned native DLL and does not need Visual Studio.
# If that DLL is unavailable (or CI requests source verification), install
# the C++ toolchain only when no compatible v145 toolchain already exists.
$prebuiltNative = Join-Path $Root "Native\LiquidGlassCompat\Prebuilt\win-x64\CustomEffectRuntimeNative.dll"
$sourceNative = Join-Path $Root "Native\LiquidGlassCompat\Output\x64\Release\CustomEffectRuntimeNative.dll"
$rebuildNative = ($env:CI -eq "true" -or $env:ASTERPLAY_USE_SOURCE_LIQUIDGLASS -eq "1" -or
    (-not (Test-Path $prebuiltNative) -and -not (Test-Path $sourceNative)))
if ($rebuildNative) {
    $vswhere = Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = $null
    if (Test-Path $vswhere) {
        $installs = @((& $vswhere -products '*' -format json | ConvertFrom-Json))
        foreach ($i in ($installs | Sort-Object installationVersion -Descending)) {
            $candidate = Join-Path $i.installationPath "MSBuild\Current\Bin\MSBuild.exe"
            $toolset = Join-Path $i.installationPath "MSBuild\Microsoft\VC\v180\Platforms\x64\PlatformToolsets\v145"
            if ((Test-Path $candidate) -and (Test-Path $toolset)) { $msbuild = $candidate; break }
        }
    }
    if (-not $msbuild) {
        Write-Host "[SETUP] Installing missing Microsoft C++ v145 Build Tools..."
        $installer = Join-Path $env:TEMP "asterplay-vs-buildtools.exe"
        Invoke-WebRequest -UseBasicParsing "https://aka.ms/vs/stable/vs_buildtools.exe" -OutFile $installer
        $p = Start-Process -FilePath $installer -ArgumentList @("--quiet","--wait","--norestart","--add","Microsoft.VisualStudio.Workload.VCTools","--includeRecommended") -Wait -PassThru
        if ($p.ExitCode -notin @(0,3010)) { throw "Build Tools setup failed with exit code $($p.ExitCode)." }
        if (-not (Test-Path $vswhere)) { throw "Visual Studio Build Tools installer was not detected." }
        $installs = @((& $vswhere -products '*' -format json | ConvertFrom-Json))
        foreach ($i in ($installs | Sort-Object installationVersion -Descending)) {
            $candidate = Join-Path $i.installationPath "MSBuild\Current\Bin\MSBuild.exe"
            $toolset = Join-Path $i.installationPath "MSBuild\Microsoft\VC\v180\Platforms\x64\PlatformToolsets\v145"
            if ((Test-Path $candidate) -and (Test-Path $toolset)) { $msbuild = $candidate; break }
        }
        if (-not $msbuild) { throw "C++ v145 toolset still unavailable after installation; reboot may be required." }
    }
    Write-Host "[BUILD] LiquidGlass native compatibility runtime..."
    & $msbuild (Join-Path $Root "Native\LiquidGlassCompat\CustomEffectRuntime.Native.vcxproj") "-restore" "-m" "/p:Configuration=Release" "/p:Platform=x64" | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $sourceNative)) { throw "Native LiquidGlass build failed." }
}
if (-not (Test-Path $prebuiltNative) -and -not (Test-Path $sourceNative)) { throw "Native LiquidGlass runtime missing." }
if (-not $env:ASTERPLAY_MPV_DIR) {
    Write-Host "[SETUP] Checking pinned libmpv; download only if needed..."
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "scripts\bootstrap-mpv.ps1") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Automatic libmpv setup failed." }
}
$mpvDll = Join-Path $MpvDir "libmpv-2.dll"
if (-not (Test-Path $mpvDll)) { throw "libmpv-2.dll missing from $MpvDir" }

$running = Get-Process -Name "AsterPlay" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running AsterPlay process..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}


function Fingerprint([string[]]$Paths,[string]$Salt) {
    $records = New-Object "System.Collections.Generic.List[string]"
    $records.Add($Salt)
    foreach ($file in ($Paths | Sort-Object -Unique)) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $records.Add($file + "=" + (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash)
        }
    }
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join [Environment]::NewLine))
        return [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace("-","").ToLowerInvariant()
    } finally { $hasher.Dispose() }
}
function Test-Output {
    $required = @(
        "AsterPlay.exe",
        "CustomEffectRuntimeNative.dll",
        "libmpv-2.dll",
        "Assets\AsterPlay.AppIcon.png",
        "Assets\AsterPlay.ico",
        "Info\BUILD-INFO.txt",
        "Info\LIQUIDGLASS-COMPAT.txt",
        "Info\RUNTIME-SOURCE.txt"
    )
    if ($Unpacked) {
        $required += @("AsterPlay.Core.dll", "LiquidGlassWinUI.dll",
            "coreclr.dll", "hostfxr.dll", "hostpolicy.dll")
    }
    foreach ($n in $required) {
        if (-not (Test-Path (Join-Path $Publish $n) -PathType Leaf)) { return $false }
    }
    return $true
}
function Publish-App([bool]$Clean,[bool]$NoRestore) {
    if ($Clean) {
        Write-Host "[BUILD] Full: clean outputs, restore packages, then compile."
        foreach ($folder in @($Publish,
            (Join-Path $Root "src\AsterPlay.Core\bin"),
            (Join-Path $Root "src\AsterPlay.Core\obj"),
            (Join-Path $Root "src\LiquidGlassWinUI\bin"),
            (Join-Path $Root "src\LiquidGlassWinUI\obj"),
            (Join-Path $Root "src\AsterPlay.WinUI\bin"),
            (Join-Path $Root "src\AsterPlay.WinUI\obj"))) {
            if (Test-Path $folder) { Remove-Item -LiteralPath $folder -Force -Recurse }
        }
    } else {
        Write-Host "[BUILD] Incremental: reuse existing MSBuild/NuGet caches."
    }
    New-Item -ItemType Directory -Force -Path $Publish | Out-Null
    $arguments = @("publish",$Project,"-c","Release","-r","win-x64","--self-contained","true",
        "-p:Platform=x64","-p:PublishSingleFile=$SingleFile",
        "-p:IncludeAllContentForSelfExtract=$SingleFile",
        "-p:PublishTrimmed=false","-p:DebugType=none","-p:DebugSymbols=false",
        "-o",$Publish)
    if ($NoRestore) { $arguments += "--no-restore" }
    & $Dotnet @arguments | Out-Host
    return ($LASTEXITCODE -eq 0)
}
$statePath = Join-Path $Root "obj\AsterPlay-build-state.json"
$src = @(Get-ChildItem (Join-Path $Root "src") -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
      $_.Extension.ToLowerInvariant() -in @(".cs",".xaml",".csproj",".props",
        ".targets",".resw",".json",".xml",".manifest",".ico",".png",
        ".jpg",".jpeg",".svg",".hlsl",".ttf",".otf",".txt",".config") } |
    Select-Object -ExpandProperty FullName)
$settings = @(
    (Join-Path $Root "AsterPlay.sln"),(Join-Path $Root "scripts\build-windows.ps1"),
    (Join-Path $Root "build-windows.cmd"),(Join-Path $Root "scripts\bootstrap-dotnet.ps1"),
    (Join-Path $Root "scripts\bootstrap-mpv.ps1"),(Join-Path $Root "NuGet.Config"),
    (Join-Path $Root "global.json"),(Join-Path $Root "Directory.Build.props"),
    (Join-Path $Root "Directory.Build.targets"),(Join-Path $Root "Directory.Packages.props")
) + @($src | Where-Object { $_ -match '\.(csproj|props|targets)$' })
$glassInput = if ($env:CI -eq "true" -or $env:ASTERPLAY_USE_SOURCE_LIQUIDGLASS -eq "1") {
    $sourceNative
} elseif (Test-Path $prebuiltNative) { $prebuiltNative } else { $sourceNative }
$configHash = Fingerprint ($settings + @($glassInput,$mpvDll)) "release|win-x64|$selectedSdk|$MpvDir|single=$SingleFile"
$inputHash = Fingerprint ($src + $settings + @($glassInput,$mpvDll)) $configHash
$state = $null
if (Test-Path $statePath) {
    try { $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json }
    catch { Write-Warning "Corrupt build cache record; full build will run." }
}
$missingAssets = @(@("AsterPlay.Core","AsterPlay.WinUI","LiquidGlassWinUI") |
    Where-Object { -not (Test-Path (Join-Path $Root "src\$_\obj\project.assets.json")) })
$canIncremental = (-not $Full) -and ($null -ne $state) -and
    ($state.configuration -eq $configHash) -and ($missingAssets.Count -eq 0) -and (Test-Output)
if ($canIncremental -and $state.inputs -eq $inputHash) {
    Write-Host "[SKIP] Inputs unchanged; current Release package is complete."
    Write-Host "Output: $Publish"
    exit 0
}
if ($canIncremental) {
    $ok = Publish-App $false $true
    if (-not $ok) {
        Write-Warning "Incremental publish failed; falling back to a full clean build."
        $ok = Publish-App $true $false
    }
} else {
    if ($Full) { Write-Host "[INFO] Explicit full build." }
    else { Write-Host "[INFO] New build, missing cache, or dependency configuration changed." }
    $ok = Publish-App $true $false
}
if (-not $ok) { throw "WinUI 3 Release build failed." }

# WinUI URI loading needs assets inside the extracted bundle, while the
# native GDI+ splash and AppWindow icon need physical files by the EXE.
# Ship the two tiny assets beside the EXE as well as inside the bundle.
$portableAssets = Join-Path $Publish "Assets"
New-Item -ItemType Directory -Force -Path $portableAssets | Out-Null
foreach ($assetName in @("AsterPlay.AppIcon.png", "AsterPlay.ico")) {
    Copy-Item -LiteralPath (Join-Path $Root "src\AsterPlay.WinUI\Assets\$assetName") `
        -Destination (Join-Path $portableAssets $assetName) -Force
}

$sourceBuiltLiquidGlassNative = Join-Path $Root "Native\LiquidGlassCompat\Output\x64\Release\CustomEffectRuntimeNative.dll"
$pinnedLiquidGlassNative = Join-Path $Root "Native\LiquidGlassCompat\Prebuilt\win-x64\CustomEffectRuntimeNative.dll"
$liquidGlassNativeSource = $null
$preferSourceBuiltLiquidGlass = ($env:CI -eq "true") -or ($env:ASTERPLAY_USE_SOURCE_LIQUIDGLASS -eq "1")

if ($preferSourceBuiltLiquidGlass -and (Test-Path $sourceBuiltLiquidGlassNative)) {
    $liquidGlassNativeSource = $sourceBuiltLiquidGlassNative
    Write-Host "Using source-built LiquidGlass runtime for Windows App SDK 2.5.1..."
}
elseif (Test-Path $pinnedLiquidGlassNative) {
    $liquidGlassNativeSource = $pinnedLiquidGlassNative
    Write-Host "Using pinned verified LiquidGlass runtime for Windows App SDK 2.5.1..."
}
elseif (Test-Path $sourceBuiltLiquidGlassNative) {
    $liquidGlassNativeSource = $sourceBuiltLiquidGlassNative
    Write-Warning "Pinned LiquidGlass runtime is missing; falling back to the local source-built runtime."
}
else {
    throw "LiquidGlass SDK 2.5.1 compatibility runtime is missing. Pull the latest main branch or build Native\LiquidGlassCompat first."
}

$liquidGlassNative = Join-Path $Publish "CustomEffectRuntimeNative.dll"
Copy-Item $liquidGlassNativeSource $liquidGlassNative -Force

$infoDirectory = Join-Path $Publish "Info"
New-Item -ItemType Directory -Force -Path $infoDirectory | Out-Null

$liquidGlassHash = (Get-FileHash -Algorithm SHA256 $liquidGlassNative).Hash.ToLowerInvariant()
@(
    "AsterPlay LiquidGlass native compatibility runtime",
    "Target: Windows App SDK 2.5.1",
    "Architecture: x64",
    "Source: $liquidGlassNativeSource",
    "SHA256: $liquidGlassHash",
    "EffectType::FromGuid: 0x193D8",
    "EffectType table: 0x64150",
    "EffectType::GetBounds: 0x1F7D0",
    "EffectType::CalcInputBounds: 0x1EE90",
    "DirectPropertyUpdater vtable: 0x471E0"
) | Set-Content -Encoding UTF8 (Join-Path $infoDirectory "LIQUIDGLASS-COMPAT.txt")

Copy-Item (Join-Path $MpvDir "*.dll") $Publish -Force

$runtimeSource = Join-Path $MpvDir "RUNTIME-SOURCE.txt"
if (Test-Path $runtimeSource) {
    Copy-Item $runtimeSource (Join-Path $infoDirectory "RUNTIME-SOURCE.txt") -Force
} elseif ($env:ASTERPLAY_MPV_DIR) {
    @(
        "Source: external ASTERPLAY_MPV_DIR",
        "DLL: $mpvDll",
        "SHA256: $((Get-FileHash -Algorithm SHA256 $mpvDll).Hash.ToLowerInvariant())"
    ) | Set-Content -Encoding UTF8 (Join-Path $infoDirectory "RUNTIME-SOURCE.txt")
} else {
    throw "Pinned libmpv source manifest is missing."
}

$requiredFiles = @(
    "AsterPlay.exe",
    "CustomEffectRuntimeNative.dll",
    "libmpv-2.dll",
    "Assets\AsterPlay.AppIcon.png",
    "Assets\AsterPlay.ico",
    "Info\LIQUIDGLASS-COMPAT.txt"
)
if ($Unpacked) {
    $requiredFiles += @("AsterPlay.Core.dll", "LiquidGlassWinUI.dll",
        "coreclr.dll", "hostfxr.dll", "hostpolicy.dll")
}

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
    "PublishSingleFile: $SingleFile",
    "WindowsAppSDKSelfContained: true",
    "SDK: $selectedSdk",
    "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
) | Set-Content -Encoding UTF8 (Join-Path $infoDirectory "BUILD-INFO.txt")

# Non-runnable diagnostics are not needed at the portable root.
Get-ChildItem -Path $Publish -Filter "*.pdb" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

if (-not $Unpacked) {
    # Localization satellites are optional. Keep Chinese/English; .NET and
    # Windows App SDK fall back to neutral resources for other UI languages.
    # Only remove culture directories made entirely of satellite resources.
    $preservedCultures = @("zh-Hans", "zh-Hant", "zh-CN", "zh-TW", "en", "en-US")
    foreach ($folder in @(Get-ChildItem -Path $Publish -Directory)) {
        if ($folder.Name -in $preservedCultures) { continue }
        try { $null = [System.Globalization.CultureInfo]::GetCultureInfo($folder.Name) }
        catch { continue }

        $files = @(Get-ChildItem -LiteralPath $folder.FullName -File -Recurse)
        $subfolders = @(Get-ChildItem -LiteralPath $folder.FullName -Directory -Recurse)
        if ($subfolders.Count -ne 0 -or $files.Count -eq 0) { continue }
        $notSatellites = @($files | Where-Object {
            $_.Name -notlike "*.resources.dll" -and
            $_.Extension -notin @(".pri", ".mui")
        })
        if ($notSatellites.Count -ne 0) { continue }
        Remove-Item -LiteralPath $folder.FullName -Force -Recurse
        Write-Host "[PACK] Pruned optional culture satellites: $($folder.Name)"
    }
}

Write-Host ""
Write-Host "Publish self-check passed:"
$requiredFiles | ForEach-Object { Write-Host "  OK: $_" }
Write-Host ""
Write-Host "Build complete:"
Write-Host "  Folder: $Publish"

if (-not (Test-Output)) { throw "Final release package verification failed." }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $statePath) | Out-Null
@{ configuration = $configHash; inputs = $inputHash; schema = 1 } |
    ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath $statePath
Write-Host "[OK] Updated incremental build cache."
