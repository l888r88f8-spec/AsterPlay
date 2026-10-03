Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Target = Join-Path $Root "third_party\mpv"
$Dll = Join-Path $Target "libmpv-2.dll"

if (Test-Path $Dll) {
    Write-Host "libmpv already present: $Dll"
    exit 0
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null
$Headers = @{ "User-Agent" = "AsterPlay-build"; "Accept" = "application/vnd.github+json" }

Write-Host "Finding mpv Windows x64 LGPL libmpv build..."
$Release = Invoke-RestMethod -Headers $Headers -Uri "https://api.github.com/repos/mpv-player/mpv/releases/tags/git-release"

$Asset = $Release.assets | Where-Object { $_.name -match '^libmpv-' -and $_.name -match 'x86_64-w64-mingw32-lgpl\.zip$' } | Select-Object -First 1
if (-not $Asset) { throw "No compatible Windows x64 LGPL libmpv asset was found." }

$Temp = Join-Path $env:TEMP ("asterplay-mpv-" + [Guid]::NewGuid().ToString("N"))
$Zip = Join-Path $Temp $Asset.name
$Extract = Join-Path $Temp "extract"
New-Item -ItemType Directory -Force -Path $Temp, $Extract | Out-Null

try {
    Write-Host "Downloading $($Asset.name)..."
    Invoke-WebRequest -UseBasicParsing -Headers $Headers -Uri $Asset.browser_download_url -OutFile $Zip
    Write-Host "Extracting libmpv..."
    Expand-Archive -Path $Zip -DestinationPath $Extract -Force
    $Found = Get-ChildItem -Path $Extract -Filter "libmpv-2.dll" -File -Recurse | Select-Object -First 1
    if (-not $Found) { throw "Downloaded archive does not contain libmpv-2.dll." }
    Copy-Item $Found.FullName $Dll -Force
    Get-ChildItem -Path $Found.Directory.FullName -Filter "*.dll" -File | Where-Object { $_.Name -ne "libmpv-2.dll" } | ForEach-Object { Copy-Item $_.FullName (Join-Path $Target $_.Name) -Force }
    @("Source: mpv-player/mpv git-release", "Asset: $($Asset.name)", "Release: $($Release.tag_name)", "Downloaded: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')") | Set-Content -Encoding UTF8 (Join-Path $Target "RUNTIME-SOURCE.txt")
    Write-Host "libmpv ready: $Dll"
}
finally {
    if (Test-Path $Temp) { Remove-Item $Temp -Recurse -Force -ErrorAction SilentlyContinue }
}
