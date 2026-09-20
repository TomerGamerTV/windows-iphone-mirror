# Windows iPhone Mirror — Resume Handoff

Last updated: 2026-09-20

## Current repository state

- Branch: `master`
- Latest pushed commit before this handoff: `a8d3409`
- The worktree has one intentional uncommitted change in `src/iPhoneMirror.App/ViewerToolbarWindow.cs`; it replaces the active toolbar's broken Unicode glyphs with WPF vector geometry.
- Generated output under `artifacts/` is ignored and must not be committed.

## Most recent UI issue

The visible toolbar is the separate WPF `ViewerToolbarWindow` popup created by `MainWindow`. `MpvHostControl` also contains an older native GDI toolbar implementation, but `MainWindow` never enables or subscribes to that path. Earlier fixes changed the inactive implementation, which is why the visible icons did not change.

The active popup originally used `⌂` and `▥`, which rendered as broken or unrelated symbols. The current uncommitted change uses:

- a stroked house outline with an explicit door for Home;
- four fixed `Rectangle` cells on a `Canvas` for App Switcher;
- explicit dynamic `TextBrush`, `SurfaceBrush`, and `SurfaceAltBrush` resources for theme/high-contrast updates;
- existing automation names: `Home Screen` and `App Switcher`.

## What has been verified

- Before the final stroked-house adjustment, a local Windows screenshot showed the four App Switcher squares rendering correctly, while the filled house still looked like a narrow vertical mark.
- The final stroked-house source compiled with 0 warnings/errors and was published successfully.
- A final screenshot after the stroked-house adjustment is still required. Launch the published app with the fake worker, move the pointer into the viewer's top edge, and capture the toolbar.

Example local visual test setup:

```powershell
$env:IPHONE_MIRROR_TEST_MODE = '1'
$env:IPHONE_MIRROR_TEST_PYTHON = (Resolve-Path .\artifacts\publish\win-x64\runtime\python\python.exe).Path
$env:IPHONE_MIRROR_TEST_WORKER = (Resolve-Path .\scripts\fake-worker-wpf-smoke.py).Path
$env:IPHONE_MIRROR_FAKE_MODE = 'normal'
Start-Process .\artifacts\publish\win-x64\iPhoneMirror.exe -ArgumentList 'start --connection usb --serial fake-wpf-device'
```

Use a local Windows screenshot or UI automation to hover near the viewer top edge. Confirm that the Home icon is visibly a house outline and the App Switcher icon is a clean 2×2 grid. Stop the test app and any bundled `mpv.exe` child before republishing.

## Tests already passing

- .NET tests: 23/23.
- Worker tests: 28/28.
- Full nine-mode WPF fake-worker matrix under Windows PowerShell 5: normal, terminal error, crash, manual reconnect, automatic network retry, missing Apple support, locked, untrusted, and Developer Mode guidance.
- Installer verifier under Windows PowerShell 5: first install, in-place update, restricted-PATH bundled CLI status, silent uninstall, and install-directory removal.
- Sensitive-data audit before previous commits found no user device identifier, local device address, personal workspace path, or obvious inline secret in staged changes.

## Remaining acceptance gaps

- Physical network-link interruption and automatic retry.
- Physical App Switcher device state; Apple's host command is not exposed by the pinned public CoreDevice surfaces, so do not add speculative HID gestures.
- Physical two-device selection and locked/untrusted/unprepared phone states.
- Clean VM and true upgrade from an older release.
- Runtime DPI, high-contrast, and screen-reader review.

## Commit procedure

After the visual check, run `git diff --check`, repeat the staged sensitive-data audit, run the .NET and worker tests, commit the toolbar change, and push `master`. Do not stage `artifacts/`, `.tmp-*`, runtime files, screenshots, pairing records, or diagnostic logs.
