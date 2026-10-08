# Internal build helpers

These PowerShell scripts are invoked by the root-level `build-windows.cmd` command. They are not separate user-facing build entry points.

- `build-windows.ps1` — detects dependencies, chooses incremental/full Release builds and verifies the output.
- `bootstrap-dotnet.ps1` — installs the pinned .NET 8 SDK when required.
- `bootstrap-mpv.ps1` — downloads and verifies the pinned Windows x64 libmpv runtime.

Use `build-windows.cmd` (or `build-windows.cmd -Full`) from the repository root.
