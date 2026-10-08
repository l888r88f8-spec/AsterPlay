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
- Visual effects: compositor-native LiquidGlass / WinUI Composition
- Emby API: HttpClient
- Player: libmpv gpu-next with D3D11 composition into SwapChainPanel
- Core: AsterPlay.Core contains the shared Emby/session/playback/details/danmaku logic
- Session data: %LOCALAPPDATA%\AsterPlay\session.json
- Image cache: %LOCALAPPDATA%\AsterPlay\cache\images
- Build: local Windows build, no Qt/CMake/MSYS2 required


## Repository structure

```text
AsterPlay/
├── build-windows.cmd          # The only build command
├── AsterPlay.sln              # Visual Studio solution
├── src/                       # WinUI app, core logic, LiquidGlass library
├── Native/LiquidGlassCompat/  # LiquidGlass C++ source and pinned runtime
├── scripts/                   # Internal build and dependency bootstrap scripts
├── docs/                      # Implementation and release documentation
├── third_party/mpv/           # Downloaded libmpv runtime
├── tools/dotnet/              # Optional local .NET SDK installation
└── .github/workflows/        # CI pipelines
```

Additional documents: [architecture and implementation](docs/IMPLEMENTATION.md),
[release checklist](docs/RELEASE-CHECKLIST.md).

Run `build-windows.cmd` from the repository root; the scripts in `scripts/` are internal helpers.

## Build on Windows x64

AsterPlay has **one build entry point**. On Windows, run:

~~~bat
build-windows.cmd
~~~

The script detects missing build dependencies and installs them only when needed:

- Reuses an existing .NET 8 SDK; otherwise installs the pinned .NET 8.0.425 SDK into tools/dotnet.
- Uses the repository's pinned LiquidGlass native runtime. If it is missing or source rebuilding is requested, uses the Visual Studio 2026 C++ v145 toolchain (installing Build Tools if necessary).
- Reuses the pinned Windows x64 libmpv package, or downloads and verifies it (and its extractor) on first use.
- Restores Microsoft.WindowsAppSDK, Microsoft.Windows.SDK.BuildTools, and other NuGet package dependencies as part of a full build.

No Qt, CMake, MSYS2, full Visual Studio IDE, or separate Windows App SDK installation is required for a normal Release build. The first setup requires Internet access. Administrator approval may be necessary only if the C++ Build Tools installer runs.

**Automatic build selection:** the script hashes source files and dependency metadata and stores the last successful build state under obj/. On the first build, missing/invalid caches, or a dependency change, it runs a clean Release publish. With unchanged dependencies and modified source files it runs an incremental Release publish while retaining bin/obj and NuGet assets. An incremental failure automatically triggers one full clean rebuild. If nothing changed and the package is complete, compilation is skipped.

To manually force a clean build for troubleshooting:

~~~bat
build-windows.cmd -Full
~~~

Output: dist/AsterPlay/ (self-contained Windows x64). The script checks that the release contains AsterPlay.exe, AsterPlay.Core.dll, LiquidGlassWinUI.dll, CustomEffectRuntimeNative.dll, libmpv-2.dll, coreclr.dll, hostfxr.dll and hostpolicy.dll, and writes BUILD-INFO.txt / RUNTIME-SOURCE.txt.

The .ps1 helpers for SDK and media-runtime provisioning are internal implementation details; build-windows.cmd is the only .cmd entry point for users and CI.

## License

AsterPlay is distributed under the [MIT License](LICENSE).

- AsterPlay contributions: Copyright (c) 2026 [l888r88f8-spec](https://github.com/l888r88f8-spec).
- Upstream qEmby copyright: Copyright (c) 2025-2026 AlanHJ, retained under the original MIT terms.
- Third-party components retain their own applicable licenses, including the bundled LiquidGlass code and libmpv.

## Acknowledgements

AsterPlay originated from a rewrite of ideas explored in [AlanHJ/qEmby](https://github.com/AlanHJ/qEmby). The original MIT copyright notice is retained in this repository.

The home-screen visual direction references [Vanvy Emby Suite](https://github.com/micimo13/emby-beautify), implemented natively in WinUI 3.

libmpv is provided under its applicable LGPL build license. The pinned Windows runtime is sourced from [zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild) and records the corresponding mpv commit in RUNTIME-SOURCE.txt.
