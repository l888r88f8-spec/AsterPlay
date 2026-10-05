Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Build = Join-Path $Root "build-windows.ps1"

Write-Host "build-winui.ps1 is retained as a compatibility alias."
Write-Host "AsterPlay's default Windows build is now WinUI 3."

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Build
exit $LASTEXITCODE
