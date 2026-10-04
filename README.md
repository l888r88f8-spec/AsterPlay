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
- Player: libmpv hosted in a native Win32 child window
- Session data: %LOCALAPPDATA%\AsterPlay\session.json
- Build: local Windows build, no Qt/CMake/MSYS2 required

## Build

Run:

```bat
bootstrap-dotnet.cmd
build-windows.cmd
```

The .NET SDK bootstrap is optional if .NET 8 SDK is already installed. If libmpv is missing, the build script downloads a Windows x64 LGPL libmpv development build from the mpv project's GitHub release assets.

Output:

```text
dist/AsterPlay/
```

The published folder is self-contained and does not require .NET to be installed on the target PC.

## Acknowledgements

AsterPlay originated from a rewrite of ideas explored in [AlanHJ/qEmby](https://github.com/AlanHJ/qEmby). The original MIT copyright notice is retained in this repository.

The home-screen visual direction references [Vanvy Emby Suite](https://github.com/micimo13/emby-beautify), implemented natively in WPF rather than injected into Emby's web UI.

libmpv is provided by the [mpv project](https://github.com/mpv-player/mpv) under its applicable license.
