@echo off
setlocal
cd /d "%~dp0"
rem Use UTF-8 for native .NET CLI / MSBuild output, including Chinese diagnostics.
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-windows.ps1" %*
exit /b %ERRORLEVEL%
