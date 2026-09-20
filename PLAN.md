# Live Status

- Current milestone: 12–14 — validation, documentation, and physical-device acceptance.
- Last verified completion: Physical USB mirroring on iPhone UDID `a paired test iPhone` reaches a running CoreDevice session with live video refresh and real input. With USB disconnected, direct Wi-Fi to `a direct Wi-Fi endpoint` is proven end to end, including video, tap, drag, wheel scroll, clipboard paste, disconnect/reconnect, 30-second steady-state playback, repeated focus-loss recovery, and repeated low-latency navigation. The published app also proves MPV hardware-preference launch (`--hwdec=auto-safe`) and automatic software fallback (`--hwdec=no`) after player termination. The moved-window toolbar follows its owner, setup status correctly describes Wi-Fi devices, viewer status messages are accessible live regions, published WPF fake-worker startup/restart/stop/reconnect/missing-stack/locked/untrusted/Developer Mode smoke passes, and the rebuilt installer passes first install, in-place update, bundled-runtime CLI, and uninstall verification. Touchscreen and release cleanup reports are bounded to 350 ms, stale MPV exit events are ignored during fast restarts, worker tests pass 28/28 and .NET tests pass 23/23, including bounded touchscreen HID recovery and a regression test proving no-wait input writes do not await worker responses.
- Current blocker: Bonjour discovery returns zero endpoints, so the persisted direct-IP fallback remains required on this network. Apple's App Switcher host command is private and the available CoreDevice HID surfaces did not reproduce it. A real network-link interruption requires adapter/firewall privileges; multi-device, locked/untrusted/unprepared states, clean-VM/update coverage, and full runtime accessibility review remain incomplete.
- Exact next action: Add or run the remaining clean-machine/device-state acceptance where environments permit, while keeping App Switcher documented as a protocol limitation rather than adding speculative HID reports.
- Last update timestamp: 2026-09-20 13:05:00 +03:00

---
# Windows 11 Native iPhone Mirror Port — Final Plan

## Summary

Completely replace the existing Linux/Omarchy implementation with a Windows 11 x64 application.

The finished app will use:

- Native .NET 10 WPF UI with Windows 11 Fluent styling, Mica, dark/light mode, proper DPI handling, rounded controls, and no terminal-looking interface.
- A privately bundled Python 3.14 + `pymobiledevice3==11.13.1` worker for the proven iPhone CoreDevice protocol layer.
- Bundled MPV embedded directly inside the Windows application for low-latency HEVC rendering.
- A self-contained Windows installer. Users will not need Python, MPV, Bash, WSL, Linux tools, or manual dependency installation.
- Windows 11 only. Linux/Omarchy support may be deleted completely.

The existing CoreDevice library has already been verified to install and import successfully on Windows 11/Python 3.14, including its display, HID, tunnel, and Windows usbmux paths.

## Persistent Project Tracking

The first implementation step is to create these four root-level files and keep them updated continuously throughout the port:

### `INSTRUCTIONS.md`

Permanent implementation rules and decisions:

- Windows 11 x64 only.
- No requirement to preserve Linux compatibility.
- Native WPF frontend.
- Bundled/private Python CoreDevice worker.
- No visible console windows.
- Never require system Python/MPV.
- Preserve privacy guarantees: never log screen contents, clipboard text, keystrokes, passcodes, or pairing secrets.
- Phone-changing setup actions require explicit user confirmation.
- Keep README credits intact.
- Credit original repository/author and `@TomerGamerTV`.
- Update `TODO.md`, `FIXES.md`, and `PLAN.md` as work progresses.
- Never mark a feature complete until its stated validation succeeds.

### `TODO.md`

Live implementation checklist with states such as:

- `[ ]` not started
- `[-]` in progress
- `[x]` verified complete
- `[!]` blocked

Organize it by milestones:

1. Project skeleton and Windows architecture.
2. Core worker.
3. USB/Wi-Fi connection.
4. Video streaming.
5. Embedded player.
6. Touch/keyboard/input.
7. Clipboard.
8. Setup wizard.
9. Windows lifecycle/single-instance support.
10. UI polish.
11. Packaging.
12. Automated tests.
13. Physical-iPhone validation.
14. Documentation and final cleanup.

Every completed item must include the validation used to prove it works.

### `FIXES.md`

Append-only engineering log for problems discovered during the port.

Each entry records:

- Date/time.
- Symptom/error.
- Component.
- Root cause.
- Attempted fixes.
- Final fix.
- Validation result.
- Any remaining limitation.

Failed approaches should remain documented so another agent does not repeat them.

### `PLAN.md`

Contains this complete implementation plan plus a short live status section at the top:

- Current milestone.
- Last verified completion.
- Current blocker, if any.
- Exact next action.
- Last update timestamp.

When architecture or implementation decisions legitimately change, update this file and note the change in `FIXES.md`.

Because this task is currently in planning mode, no repository files were mutated in this turn. Implementation begins by creating these four files and copying this finalized plan into `PLAN.md`.

## Architecture

Replace the current application with three layers:

1. `iPhoneMirror.App`

   - .NET 10 WPF.
   - Owns all visible UI, window lifecycle, device/setup screens, keyboard/mouse capture, settings, notifications, and embedded MPV host.
2. `iPhoneMirror.Core`

   - Shared .NET application logic.
   - Worker process protocol.
   - Coordinate conversion.
   - input state.
   - connection/session models.
   - settings.
   - error mapping.
   - process supervision.
   - named-pipe single-instance handling.
3. `worker/`

   - Python 3.14.
   - Pinned `pymobiledevice3==11.13.1`.
   - Owns Apple/CoreDevice-specific operations:

     - usbmux.
     - CoreDevice tunnel.
     - DisplayService.
     - media receiver.
     - HID.
     - pasteboard.
     - trust/setup commands.
     - developer image operations.
     - Wi-Fi pairing.

Communication between WPF and Python uses newline-delimited JSON over redirected stdin/stdout.

stderr is diagnostic-only and must never contain sensitive payloads.

## Windows UI

Build a proper Windows application rather than exposing MPV or Python directly.

Main experience:

- Native Windows 11 title bar.
- Mica backdrop where appropriate.
- Fluent controls.
- system accent color.
- light/dark theme synchronization.
- smooth transitions.
- clean typography.
- responsive DPI scaling.

Main viewer:

- iPhone screen centered at its correct aspect ratio.
- Dark neutral background around unused space.
- No visible MPV window chrome.
- Compact overlay controls rather than a permanent bulky toolbar.
- Home button.
- Spotlight button.
- Connection indicator.
- USB/Wi-Fi indicator.
- reconnect/disconnect.
- device name/status where useful.
- setup/error action when the phone is not ready.

First-run/setup UI:

- USB device detection.
- Trust-this-computer guidance.
- Developer Mode explanation.
- Reveal Developer Mode action.
- Developer image preparation.
- Wi-Fi pairing.
- Display-service validation.
- Final connection test.

Dangerous or phone-modifying operations remain individual user-confirmed steps.

## Video

Preserve the current low-latency CoreDevice DisplayService approach.

- Receive Apple's HEVC stream through `pymobiledevice3`.
- Preserve VPS/SPS/PPS initialization.
- Preserve RTP/media cleanup ordering.
- Feed encoded HEVC to bundled MPV.
- Embed MPV's native HWND inside the WPF viewer.
- MPV runs without independent controls/titlebar/taskbar clutter.
- Prefer Windows hardware HEVC decoding when stable.
- Automatically fall back to software decoding when needed.
- No video recording or persistence.

Player failure, stream timeout, phone calls, disconnects, and decode failure should produce useful GUI states instead of closing silently.

## Input

Move host-side event collection out of MPV and into the native WPF window.

Support:

- Tap.
- Click-and-drag.
- Long drag.
- Wheel scrolling translated to bounded touchscreen gestures.
- Physical keyboard input.
- modifier keys.
- Home.
- Spotlight via Command+Space.
- Clipboard paste.
- Input reconnection after CoreDevice HID failure.

Coordinate conversion must account for:

- letterboxing.
- actual embedded-video bounds.
- DPI scaling.
- phone aspect ratio.
- resize/fullscreen changes.

On focus loss, disconnect, worker failure, or shutdown:

- release touch contact;
- release all keyboard usages;
- clear modifier state;
- cancel scrolling/gesture tasks.

Never replay the event that caused an input reconnection.

## Clipboard

Replace `wl-paste` entirely.

For explicit `Ctrl+V` while the mirror is focused:

1. Read Windows text clipboard.
2. Accept plain text only.
3. Maximum 1 MiB.
4. Send it using CoreDevice PasteboardService.
5. Wait for confirmation.
6. Send iPhone Command+V.

Clipboard contents must never be written to logs or disk.

Unicode and multiline text remain supported.

## Connections

Preserve connection behavior:

- `auto`: USB first; otherwise Wi-Fi.
- `usb`: USB only.
- `wifi`: paired Wi-Fi only.
- Never silently switch an already-running session.
- Explicit USB failure does not silently fall back to Wi-Fi.
- Multiple devices require selecting one.

### USB

Use Apple's Windows usbmux service through `pymobiledevice3`.

Detect the installed Apple device stack.

If missing, show an actionable GUI requirement rather than a Python traceback.

### Wi-Fi

Reuse CoreDevice pairing records.

Replace Linux:

- `ip route`
- `/sys/class/net`
- `ipheth`

with Windows route/interface inspection.

Reject inappropriate routes such as:

- loopback;
- VPN/tunnel adapters when unsuitable;
- iPhone USB tethering;
- invalid/unreachable interfaces.

Bonjour discovery continues through the protocol layer.

## Application Lifecycle

Delete systemd/XDG/Hyprland assumptions.

Use Windows-native equivalents:

- Named mutex: single running instance.
- Named pipe: commands sent to existing instance.
- `%LOCALAPPDATA%\\iPhoneMirror\\` for application state/cache.
- `%APPDATA%\\iPhoneMirror\\` for user settings where appropriate.
- Native process checks instead of systemd.
- Standard Windows clipboard.
- Native foreground-window activation.
- graceful process shutdown.

Secondary launches should focus the existing viewer rather than opening another stream.

No Windows Service is required.

## CLI

Keep a small Windows CLI interface for diagnostics/automation:

- `start`
- `stop`
- `restart`
- `status`
- `--connection auto|usb|wifi`
- `--serial <UDID>`

`status` remains compact machine-readable JSON.

GUI is the primary interface.

Remove Linux-specific `reload-ui`; Windows UI settings update live.

## Setup and Phone Preparation

Port the useful safety model from the existing scripts into the GUI.

The setup flow must preserve:

- exactly one selected device for device-changing operations;
- no implicit trust;
- no implicit Developer Mode enablement;
- no implicit image replacement;
- no automatic pairing reset;
- no blind retry loops;
- explicit Wi-Fi pairing;
- explicit developer-image action.

Passcodes stay on the iPhone.

Errors shown to the user should be sanitized categories rather than raw potentially-sensitive CoreDevice responses.

## Packaging

Replace all shell/Linux packaging.

Produce a proper Windows installer containing:

- WPF app.
- .NET runtime as needed for self-contained distribution.
- Python 3.14 runtime.
- pinned Python packages.
- bundled MPV.
- assets/icons.
- licenses.
- third-party notices.
- Start Menu shortcut.
- uninstall support.

Preferred output:

- signed-ready x64 installer executable.
- self-contained app installation.
- no PATH modification.
- no administrator privilege unless genuinely required by a prerequisite.
- no Linux artifacts.

The installer detects Apple device support and explains how to install Apple Devices when it is missing.

## Repository Cleanup

Once equivalent Windows functionality exists, remove obsolete Linux-only material including:

- shell launch/setup/install scripts;
- systemd templates;
- `.desktop` templates;
- Hyprland integration;
- XDG runtime logic;
- Omarchy plugin;
- Linux-specific route logic;
- Linux installer tests;
- Linux-only documentation.

Do not retain dead compatibility layers simply to preserve old structure.

## Testing

### Python worker

Rewrite existing useful behavioral tests for Windows:

- USB-first automatic selection.
- explicit mode behavior.
- multi-device handling.
- saved Wi-Fi pairing.
- retry boundaries.
- display-service lifecycle.
- cleanup ordering.
- HID keyboard mapping.
- modifier ordering.
- touch release.
- scroll gestures.
- Home.
- Spotlight.
- clipboard limits.
- clipboard privacy.
- input reconnection.
- setup confirmations.
- sanitized failures.

### .NET

Add tests for:

- JSON worker protocol.
- worker supervision.
- process death.
- malformed worker events.
- app state transitions.
- coordinate translation.
- letterboxing.
- DPI scaling.
- keyboard translation.
- named mutex.
- named-pipe commands.
- settings.
- clipboard gating.
- device selection.
- error presentation.

### Integration

Use fake-worker integration tests to validate the whole WPF application without requiring a phone.

Test:

- startup.
- reconnect.
- disconnect.
- worker crash.
- stream-ready transition.
- errors.
- second-instance focus.
- shutdown cleanup.

### Installer

Validate on a clean Windows 11 environment with:

- no Python installed;
- no MPV installed;
- paths containing spaces;
- normal non-admin user;
- update over older Windows build;
- uninstall;
- reinstall.

## Real-iPhone Acceptance

The port is not considered fully complete until tested with a physical compatible iPhone.

Required evidence:

- Windows detects the phone.
- USB trust works.
- Developer setup works.
- compatible image can be prepared.
- DisplayService opens.
- live HEVC image appears.
- sustained video playback.
- taps work.
- drags work.
- wheel scrolling works.
- keyboard input works.
- Unicode/multiline paste works.
- Home works.
- Spotlight works.
- focus loss releases input.
- disconnect releases input.
- reconnect works.
- repeated open/close does not leave DisplayService unusable.
- Wi-Fi pairing succeeds.
- Wi-Fi mirroring works with the USB cable disconnected.
- multi-device selection behaves correctly.
- locked/untrusted/unprepared phones receive useful error guidance.

Compilation or mocked tests alone do not count as proof of real-device compatibility.

## README and Credits

Rewrite the README around the Windows project.

Include:

- screenshots when the UI is complete;
- Windows 11 requirement;
- installer instructions;
- Apple Devices prerequisite;
- first-time phone setup;
- USB/Wi-Fi usage;
- privacy behavior;
- troubleshooting;
- development instructions;
- build/test instructions;
- license information;
- known device/iOS compatibility findings.

Add a clear Credits section containing:

- Original project: `daniellemky/omarchy-iphone-mirror`
- Original author: Daniel Lemky
- Link to the original repository.
- Explanation that this project is a Windows port/rewrite derived from that work.
- `@TomerGamerTV` credited for the Windows port/project.
- Preserve applicable original copyright and license notices.

Do not imply the original author created or endorsed the Windows rewrite.

## Completion Rules

A milestone is marked `[x]` in `TODO.md` only after validation.

Whenever work stops unexpectedly:

1. Update `PLAN.md` with exact current state and next action.
2. Record errors/failed attempts in `FIXES.md`.
3. Update `TODO.md` statuses.
4. Keep permanent discoveries/constraints in `INSTRUCTIONS.md`.

This ensures another agent can resume without reconstructing the entire history.

## Assumptions

- Windows 11 x64 is the only supported host.
- Native Windows UI + bundled Python protocol worker is the chosen architecture.
- `pymobiledevice3==11.13.1` stays pinned initially.
- Python is an internal runtime only, never a user dependency.
- MPV is bundled and visually embedded.
- No Linux compatibility is required.
- Existing privacy/safety guarantees around passcodes, input, pairing data, and clipboard are preserved.
- Real-device validation will be performed when an iPhone is connected.
