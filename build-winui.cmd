@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-winui.ps1"
exit /b %ERRORLEVEL%
