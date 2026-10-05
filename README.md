# AsterPlay

AsterPlay is a native Windows desktop client for Emby built with **C# / .NET 8 / WinUI 3 / libmpv**.

## Current features

- Native Emby login, saved servers, session restore and logout
- DPAPI-protected Emby access token storage
- Windows light/dark theme integration
- Liquid-glass WinUI shell
- Immersive home page with Hero, Continue Watching, media libraries and per-library sections
- Library browsing with search, type/year/favorite filters, sorting and paging
- Movie and series details, seasons, episodes, cast, tags and media-source information
- Favorite toggle
- Native libmpv playback through D3D11 composition and WinUI SwapChainPanel
- DirectPlay / DirectStream / Transcode negotiation
- Resume playback and Emby start/progress/pause/stop reporting
- Timeline seek, server-side reopen/seek, volume, mute and playback speed
- Explicit audio-track and subtitle selection
- Previous/next episode and episode list
- Fullscreen and playback diagnostics
- LogVar danmaku with automatic/manual matching, filters, density and overlap controls
- Persistent image cache and viewport-based lazy image loading
- Portable self-contained Windows x64 publish

## Architecture

- UI: WinUI 3 on .NET 8
- Windows App SDK: self-contained deployment
- Visual effects: LiquidGlassWinUI
- Emby API: HttpClient
- Player: libmpv gpu-next with D3D11 composition into SwapChainPanel
- Core: AsterPlay.Core contains the shared Emby/session/playback/details/danmaku logic
- Session data: %LOCALAPPDATA%\AsterPlay\session.json
- Image cache: %LOCALAPPDATA%\AsterPlay\cache\images
- Build: local Windows build, no Qt/CMake/MSYS2 required

The legacy WPF source remains in the repository for shared-source compatibility and regression reference, but it is no longer the default application or release target.

## Build

Run:

~~~bat
bootstrap-dotnet.cmd
build-windows.cmd
~~~

The portable bootstrap installs the pinned .NET SDK 8.0.425 when needed. The release build publishes the WinUI 3 application, includes the Windows App SDK runtime, LiquidGlass native runtime and the pinned Windows x64 LGPL libmpv package.

Output:

~~~text
dist/AsterPlay/
~~~

The published folder is self-contained and does not require .NET or the Windows App SDK runtime to be installed separately on the target PC.

The build verifies that the release contains at least:

- AsterPlay.exe
- AsterPlay.Core.dll
- CustomEffectRuntimeNative.dll
- libmpv-2.dll
- coreclr.dll
- hostfxr.dll
- hostpolicy.dll

It also writes BUILD-INFO.txt and copies RUNTIME-SOURCE.txt into the publish folder.


## Acknowledgements

AsterPlay originated from a rewrite of ideas explored in [AlanHJ/qEmby](https://github.com/AlanHJ/qEmby). The original MIT copyright notice is retained in this repository.

The home-screen visual direction references [Vanvy Emby Suite](https://github.com/micimo13/emby-beautify), implemented natively in WinUI 3.

libmpv is provided under its applicable LGPL build license. The pinned Windows runtime is sourced from [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild) and records the corresponding mpv commit in RUNTIME-SOURCE.txt.
