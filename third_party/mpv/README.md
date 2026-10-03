# libmpv runtime

AsterPlay uses `libmpv-2.dll` for video playback.

`build-windows.ps1` automatically runs `bootstrap-mpv.ps1` when the DLL is missing, or you can place compatible Windows x64 libmpv DLLs here manually.

You may also set `ASTERPLAY_MPV_DIR` to another directory containing the runtime DLLs.
