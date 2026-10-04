Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Target = Join-Path $Root "third_party\mpv"
$Dll = Join-Path $Target "libmpv-2.dll"
$SourceInfo = Join-Path $Target "RUNTIME-SOURCE.txt"

# Step 14 release pin. This is an immutable LGPL Windows x64 libmpv build
# instead of mpv-player/mpv's rolling git-release tag.
$PinnedRelease = "2026-09-29-b4b5d69a44"
$PinnedMpvCommit = "b4b5d69a44e240e4a95c230bb7f018c381f0c5ae"
$AssetName = "mpv-dev-lgpl-x86_64-20260929-git-b4b5d69a44.7z"
$ExpectedSha256 = "8c80c506cf95f403d8a2b9d672d5f88d965885666f25c850f711a06b9510dfc8"
$DownloadUrl = "https://github.com/zhongfly/mpv-winbuild/releases/download/$PinnedRelease/$AssetName"

function Test-PinnedRuntime {
    if (-not (Test-Path $Dll) -or -not (Test-Path $SourceInfo)) {
        return $false
    }

    $info = Get-Content -Raw -Path $SourceInfo -ErrorAction SilentlyContinue
    return $info -match [Regex]::Escape("Asset: $AssetName") -and
           $info -match [Regex]::Escape("SHA256: $ExpectedSha256")
}

if (Test-PinnedRuntime) {
    Write-Host "Pinned libmpv already present: $Dll"
    exit 0
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null

if (Test-Path $Dll) {
    Write-Host "Replacing non-pinned libmpv runtime with Step 14 release pin..."
}

$Temp = Join-Path $env:TEMP ("asterplay-mpv-" + [Guid]::NewGuid().ToString("N"))
$Archive = Join-Path $Temp $AssetName
$Extract = Join-Path $Temp "extract"
New-Item -ItemType Directory -Force -Path $Temp, $Extract | Out-Null

try {
    Write-Host "Downloading pinned libmpv:"
    Write-Host "  Release: $PinnedRelease"
    Write-Host "  Asset:   $AssetName"

    Invoke-WebRequest -UseBasicParsing -Headers @{ "User-Agent" = "AsterPlay-build" } -Uri $DownloadUrl -OutFile $Archive

    $actualSha256 = (Get-FileHash -Path $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $ExpectedSha256) {
        throw "libmpv SHA-256 mismatch. Expected $ExpectedSha256 but got $actualSha256."
    }

    Write-Host "SHA-256 verified: $actualSha256"

    $tar = Get-Command tar.exe -ErrorAction SilentlyContinue
    if ($tar) {
        & $tar.Source -xf $Archive -C $Extract
        if ($LASTEXITCODE -ne 0) {
            throw "tar.exe failed to extract the pinned libmpv archive."
        }
    }
    else {
        $sevenZip = Get-Command 7z.exe -ErrorAction SilentlyContinue
        if (-not $sevenZip) {
            throw "Neither tar.exe nor 7z.exe is available to extract the pinned libmpv archive."
        }

        & $sevenZip.Source x $Archive "-o$Extract" -y | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "7z.exe failed to extract the pinned libmpv archive."
        }
    }

    $Found = Get-ChildItem -Path $Extract -Filter "libmpv-2.dll" -File -Recurse | Select-Object -First 1
    if (-not $Found) {
        throw "Pinned archive does not contain libmpv-2.dll."
    }

    Get-ChildItem -Path $Target -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne ".gitkeep" } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Copy-Item $Found.FullName $Dll -Force
    Get-ChildItem -Path $Found.Directory.FullName -Filter "*.dll" -File |
        Where-Object { $_.Name -ne "libmpv-2.dll" } |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $Target $_.Name) -Force }

    @(
        "Source: zhongfly/mpv-winbuild",
        "Release: $PinnedRelease",
        "mpv commit: $PinnedMpvCommit",
        "Asset: $AssetName",
        "SHA256: $ExpectedSha256",
        "License build: LGPL",
        "Downloaded: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ) | Set-Content -Encoding UTF8 $SourceInfo

    Write-Host "Pinned libmpv ready: $Dll"
}
finally {
    if (Test-Path $Temp) {
        Remove-Item $Temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
