# AsterPlay

AsterPlay is a native Windows desktop client for Emby, built with **C# / .NET 8 / WPF / libmpv**.

## Current features

- Native Emby server login with persisted session
- Immersive home Hero with library tabs
- Continue Watching and Latest Media
- Favorite toggle and details window
- Native libmpv playback
- Resume playback from Emby progress
- Playback start/progress/stop reporting
- Seek, volume, audio track, subtitle and fullscreen controls
- Portable self-contained Windows x64 publish

## Architecture

- UI: WPF on .NET 8
- Emby API: HttpClient
- Player: libmpv Render API rendered into the WPF OpenGL surface
- Session data: %LOCALAPPDATA%\AsterPlay\session.json
- Build: local Windows build, no Qt/CMake/MSYS2 required

## Build

Run:

```bat
bootstrap-dotnet.cmd
build-windows.cmd
```

The .NET SDK bootstrap is optional if .NET 8 SDK is already installed. The default release build uses a fixed Windows x64 LGPL libmpv package pinned by release, mpv commit and SHA-256. `build-windows.ps1` verifies that pin before publishing; set `ASTERPLAY_MPV_DIR` only when intentionally supplying a different tested runtime.

Output:

```text
dist/AsterPlay/
```

The published folder is self-contained and does not require .NET to be installed on the target PC. The build fails if `AsterPlay.exe`, `libmpv-2.dll`, `coreclr.dll`, `hostfxr.dll` or `hostpolicy.dll` is missing, and writes `BUILD-INFO.txt` plus `RUNTIME-SOURCE.txt` into the publish folder.

## Acknowledgements

AsterPlay originated from a rewrite of ideas explored in [AlanHJ/qEmby](https://github.com/AlanHJ/qEmby). The original MIT copyright notice is retained in this repository.

The home-screen visual direction references [Vanvy Emby Suite](https://github.com/micimo13/emby-beautify), implemented natively in WPF rather than injected into Emby's web UI.

libmpv is provided under its applicable LGPL build license. The pinned Windows runtime is sourced from [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild) and records the corresponding mpv commit in `RUNTIME-SOURCE.txt`.
