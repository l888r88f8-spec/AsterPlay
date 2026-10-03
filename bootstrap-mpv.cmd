@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0bootstrap-mpv.ps1"
exit /b %ERRORLEVEL%
