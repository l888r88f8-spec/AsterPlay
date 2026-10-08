# AsterPlay Release Checklist

This checklist is the final manual acceptance pass for Step 14. Automated Windows CI covers SDK/bootstrap reproducibility, the pinned libmpv runtime, self-contained publish contents, and packaged-app startup. The items below require a real Windows desktop and a live Emby server.

## 1. Clean Windows x64 build

Run from a clean clone:

```bat
git clone https://github.com/l888r88f8-spec/AsterPlay.git
cd AsterPlay
build-windows.cmd
dist\AsterPlay\AsterPlay.exe
```

Pass criteria:

- Only the root `AsterPlay.exe` and `resources/` appear at the portable root.
- `resources/AsterPlay.exe` is the real .NET 8 / WinUI 3 executable.
- `resources/AsterPlay.Core.dll`, `resources/LiquidGlassWinUI.dll`, `resources/coreclr.dll`, `resources/CustomEffectRuntimeNative.dll` and `resources/libmpv-2.dll` exist.
- Required culture directories and `resources/Assets/AsterPlay.AppIcon.png` remain beside the real executable.
- `resources/Info/BUILD-INFO.txt` records `PublishSingleFile: false` and the root-launcher / resources layout.
- `resources/Info/RUNTIME-SOURCE.txt` contains the pinned libmpv release, commit, and checksum.
- Starting the root `AsterPlay.exe` launches `resources/AsterPlay.exe` without a visible command window.
- Copying the whole portable directory to another drive preserves startup and media playback. No additional .NET 8 or Windows App SDK installation is required.
- Future installer should deploy this same directory structure; it may add an uninstaller but should not relocate runtime DLLs.

## 2. Login and session

Verify:

- Valid Emby login succeeds.
- Restart restores the session.
- A temporary network outage does not delete the saved session.
- A confirmed 401/403 invalid session returns to login and clears the invalid session.
- Logout removes the saved session.
- `%LOCALAPPDATA%\AsterPlay\session.json` contains no plaintext AccessToken.

## 3. Home, library, and details

Verify:

- Hero shows at most six candidates.
- Hero thumbnail overlay, hover, and keyboard focus are visible.
- Continue Watching opens playback.
- Latest Media opens details.
- Library search, type/year/favorite filters, sorting, and paging work.
- Library cards support keyboard focus/navigation and right-click Details.
- Empty search/filter results show the empty state.
- Library loading shows the skeleton/loading state.
- Movie and series details load without layout regressions.

## 4. Playback matrix

Test at least one real item in each available route:

- DirectPlay
- DirectStream
- Transcode

For each route verify:

- Playback starts.
- Pause/resume works.
- Relative seek works.
- Slider seek works.
- Resume position is correct.
- Audio/subtitle selection works.
- Fullscreen enter/exit works.
- Playback diagnostics show the expected route.
- Closing the player updates Emby progress.
- Continue Watching reflects the new position.

For Transcode also verify that a failed transcode produces a clear error instead of silently stopping.

## 5. Error handling

Exercise where practical:

- Server unreachable.
- Request timeout.
- Expired/invalid token.
- Deleted or inaccessible media.
- PlaybackInfo failure.
- Missing/failed transcode.
- Missing libmpv runtime.

Pass criteria: the user sees an actionable message and the failure is written to `AsterPlay.log` without crashing unrelated UI.

## 6. Danmaku regression

With a real LogVar source:

- Automatic match works.
- Manual series/episode match works.
- Seek suppression/resync works.
- Pause/resume and playback-speed sync work.
- Density, filter, overlap protection, and active cap still work.
- Fullscreen/resize/DPI changes do not cause visible drift.

## 7. Performance observation

Use a large library and a long playback session.

Check:

- Home initial load is acceptable.
- Library scrolling remains responsive.
- Reopening pages produces image-cache hits.
- Working set does not grow continuously during normal page switching.
- Long playback does not show sustained Overlay frame-time spikes.
- Playback progress reports continue at the expected cadence.
- Closing PlayerView releases mpv/composition resources.

`AsterPlay.log` includes `[Performance]` entries for home, library page loads, and details loads, including elapsed time and memory figures.

Log growth is bounded:

- `AsterPlay.log` is the only runtime log file
- when it reaches approximately 8 MB, older entries are trimmed in place and the newest entries are retained

## 8. Release decision

Step 14 can be marked fully accepted only after all applicable sections above pass on a real Windows x64 desktop with the intended Emby server/media set.

## 9. Acceptance record

- Date: 2026-10-05
- Environment: real Windows x64 desktop with the intended Emby server/media set
- Result: PASS
- Step 14 release acceptance: complete
