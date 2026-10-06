param(
    [switch]$ForceRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\AsterPlay.WinUI\AsterPlay.WinUI.csproj"
$Solution = Join-Path $Root "AsterPlay.sln"
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

if (-not $Dotnet) {
    throw ".NET 8 SDK was not found. Run bootstrap-dotnet.cmd first."
}

Write-Host "=== AsterPlay fast incremental build ==="
Write-Host "dotnet:  $Dotnet"
Write-Host "project: $Project"
Write-Host ""

$assetFiles = @(
    (Join-Path $Root "src\AsterPlay.Core\obj\project.assets.json"),
    (Join-Path $Root "src\AsterPlay.WinUI\obj\project.assets.json")
)

$restoreInputs = @(
    (Get-ChildItem -Path (Join-Path $Root "src") -Recurse -File -Filter *.csproj -ErrorAction SilentlyContinue)
)

foreach ($metadataName in @(
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "NuGet.Config"
)) {
    $metadataPath = Join-Path $Root $metadataName
    if (Test-Path $metadataPath) {
        $restoreInputs += Get-Item $metadataPath
    }
}

$needsRestore = $ForceRestore
if (-not $needsRestore) {
    foreach ($asset in $assetFiles) {
        if (-not (Test-Path $asset)) {
            $needsRestore = $true
            break
        }
    }
}

if (-not $needsRestore -and $restoreInputs.Count -gt 0) {
    $latestRestoreInput = ($restoreInputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    $oldestAssets = ($assetFiles | ForEach-Object { Get-Item $_ } | Sort-Object LastWriteTimeUtc | Select-Object -First 1).LastWriteTimeUtc
    $needsRestore = $latestRestoreInput -gt $oldestAssets
}

if ($needsRestore) {
    Write-Host "Restoring packages because project/package metadata changed..."
    & $Dotnet restore $Solution -p:Platform=x64 -p:WindowsAppSDKSelfContained=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed." }
} else {
    Write-Host "NuGet restore: skipped (assets are current)."
}

Write-Host ""
Write-Host "Building only changed inputs..."
$buildArgs = @(
    "build",
    $Project,
    "-c", "Debug",
    "-p:Platform=x64",
    "-p:WindowsAppSDKSelfContained=false",
    "-p:PublishReadyToRun=false",
    "-p:BuildInParallel=true",
    "-p:UseSharedCompilation=true",
    "--no-restore",
    "--nologo",
    "-m"
)
& $Dotnet @buildArgs

if ($LASTEXITCODE -ne 0) { throw "Fast incremental build failed." }

Write-Host ""
Write-Host "Incremental build complete."
Write-Host "Unchanged C# projects and MSBuild targets are reused automatically."
Write-Host "Use build-windows.cmd only when you need the full self-contained release package."
