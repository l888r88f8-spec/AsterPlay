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

$exe = Join-Path $Publish "AsterPlay.WinUI.exe"
if (-not (Test-Path $exe)) {
    throw "WinUI 3 publish self-check failed: AsterPlay.WinUI.exe is missing."
}

Write-Host "WinUI 3 publish self-check passed."
Write-Host "  EXE: $exe"
