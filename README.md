# iPhone Mirror for Windows

A native Windows 11 x64 port of the iPhone CoreDevice mirroring project originally created by Daniel Lemky.

The Windows application uses a .NET 10 WPF frontend, a private Python 3.14 CoreDevice worker pinned to `pymobiledevice3==11.13.1`, and a bundled MPV build for HEVC playback. Installed users do not need to install Python, .NET, MPV, Bash, WSL, or Linux tools.

> **Alpha status:** physical USB and cable-disconnected Wi-Fi mirroring have been exercised on a Windows 11 host with a paired test iPhone. Video, taps, drags, wheel scrolling, keyboard input, Home, Spotlight, clipboard paste, disconnect, reconnect, repeated Home-to-Safari taps, and focus-loss input release are verified. App Switcher device behavior, multiple-device selection, locked/untrusted/unprepared states, and clean-machine installer validation remain open.

## Requirements

- Windows 11 x64.
- Apple Devices for Windows installed and running so the Apple Mobile Device usbmux endpoint is available.
- A compatible iPhone connected by USB for initial setup.
- The iPhone must trust the PC.
- Developer Mode must be enabled when required by the iOS/CoreDevice version.
- A compatible developer image must be available/mounted for the CoreDevice display service.
- Wi-Fi mirroring additionally requires an existing CoreDevice Wi-Fi pairing record and a reachable local-network route to the phone.

The app does not require AssistiveTouch. It does not install a Windows service and does not add itself to startup.

## Install

For a locally built package, run:

```text
artifacts\installer\iPhoneMirror-Setup-x64.exe
```

The installer is per-user by default and installs to:

```text
%LOCALAPPDATA%\Programs\iPhoneMirror
```

It creates a Start Menu shortcut named **iPhone Mirror** and does not modify `PATH`. If the Apple device service cannot be reached after installation, the installer explains that Apple Devices for Windows is required.

The installer contains the WPF app, CLI, self-contained .NET runtime, private Python runtime, pinned Python dependencies, MPV, worker files, licenses, and third-party notices.

## First-time phone setup

Open **iPhone Mirror**, connect the iPhone by USB, and use the setup panel. The intended sequence is:

1. Select the intended USB device if more than one is present.
2. Check the current setup state.
3. Approve **Trust This Computer** on the iPhone if required.
4. Reveal/enable Developer Mode if required, following the prompts on the iPhone.
5. Prepare the developer image.
6. Validate that the CoreDevice display service is available.
7. Pair for Wi-Fi only if you want cable-disconnected mirroring.
8. Connect and test the mirror.

Phone-changing actions are individually confirmation-gated. The app never asks you to type an iPhone passcode into the PC and does not automatically reset pairing, enable Developer Mode, replace an image, or retry destructive setup operations.

## Connections

The connection selector supports:

- `auto`: prefer a matching USB phone; otherwise use an existing Wi-Fi pairing.
- `usb`: use USB only. A USB failure does not silently fall back to Wi-Fi.
- `wifi`: use an existing paired Wi-Fi connection only.

An already-running session is not silently moved between transports. When multiple eligible phones or saved Wi-Fi pairings are ambiguous, select a device explicitly.

Wi-Fi discovery rejects unsuitable routes such as loopback, common VPN/tunnel/TUN/TAP/Tailscale/WireGuard/ZeroTier adapters, and Apple Mobile Device Ethernet/iPhone tether-style interfaces. This is intended to keep Wi-Fi mode on an appropriate local-network path.

## Viewer controls

The native WPF window owns input and embeds MPV inside its viewer area.

The Windows UI follows the system light/dark preference, has a High Contrast resource path, exposes names/status live regions to UI Automation, and shows keyboard focus on custom buttons.

- Click/tap maps to the iPhone touchscreen.
- Click-and-drag sends touch movement.
- Mouse-wheel movement is translated into bounded touchscreen swipes.
- Physical keyboard input is translated to HID usages.
- **Home** sends the iPhone Home hardware-button event.
- **Spotlight** sends Command+Space.
- **Ctrl+1 / Ctrl+2 / Ctrl+3** send Home, App Switcher, and Spotlight commands while the viewer is focused.
- **Ctrl+V** sends plain text to the iPhone pasteboard, waits for confirmation, then sends Command+V.
- **Reconnect** starts a fresh session using the selected transport/device.
- **Disconnect** stops the current CoreDevice session.

Input is released on focus loss, disconnect, worker failure, and shutdown. If the HID service fails, the next fresh click reconnects the touchscreen service and is delivered after recovery.

MPV prefers Windows hardware HEVC decoding and can retry with software decoding when startup fails. Video is streamed through a private named pipe and is not recorded by the application.

## Clipboard and privacy

Clipboard paste is explicit and accepts plain text only, up to 1 MiB. Unicode and multiline text are supported.

The application and worker are designed not to persist or log:

- mirrored screen/video contents;
- clipboard text;
- keystrokes or typed text;
- iPhone passcodes;
- pairing secrets;
- raw sensitive CoreDevice payloads.

Worker stderr is diagnostic-only and reports sanitized categories/types rather than user payloads. Unknown worker errors are mapped to safe error codes before they reach the UI.

## CLI

The installed CLI is:

```text
%LOCALAPPDATA%\Programs\iPhoneMirror\iphone-mirror.exe
```

Commands:

```powershell
iphone-mirror.exe start
iphone-mirror.exe start --connection usb
iphone-mirror.exe start --connection wifi --serial <UDID>
iphone-mirror.exe stop
iphone-mirror.exe restart
iphone-mirror.exe status
```

`status` returns compact machine-readable JSON and does not itself open a phone connection. Starting again while the app is already running communicates with the existing per-user instance through a private named pipe.

## Troubleshooting

### Apple Devices is missing

Install **Apple Devices** from Microsoft and reconnect the iPhone. The Windows port expects Apple's local usbmux service. If that service is unavailable, the app reports an Apple device-support error instead of exposing a Python traceback.

### The phone does not appear

- Unlock the phone.
- Reconnect USB.
- Confirm **Trust This Computer** on the iPhone if prompted.
- Check that Apple Devices can see the phone.
- If more than one phone is connected, select the intended device explicitly.

### Developer/display setup fails

Use the setup panel to check Developer Mode and the mounted developer image. A mounted image and an advertised display service are prerequisites; they are not by themselves proof that the device/iOS combination supports usable mirroring.

### Wi-Fi pairing exists but the phone is unreachable

- Keep the PC and iPhone on the same local network.
- Disconnect USB when specifically validating Wi-Fi behavior.
- Check VPN/tunnel software. iPhone Mirror intentionally refuses several tunnel/tether routes.
- Use an explicit serial when several saved pairings exist.

### Video fails with hardware decoding

The viewer prefers hardware HEVC decoding. Its recovery path can retry using software decoding. A real-device Windows validation run is still required before this fallback is considered physically accepted.

### Calls, lock state, and reconnect behavior

The original Linux implementation observed that phone calls could interrupt mirroring on some configurations. Windows behavior for calls, locked phones, focus-loss release, and repeated display-service open/close cycles remains part of the physical-iPhone acceptance checklist. One Wi-Fi disconnect/reconnect cycle has been verified.

## Uninstall

Use **Installed apps** in Windows Settings or run the generated uninstaller from the install directory.

The installer owns the application files and Start Menu shortcut. User settings/state live separately under:

```text
%APPDATA%\iPhoneMirror
%LOCALAPPDATA%\iPhoneMirror
```

Uninstalling the application does not intentionally remove Apple pairing records or developer images from the device stack.

## Development

Required build tools:

- .NET 10 SDK.
- PowerShell.
- Inno Setup 6 only when building the installer.
- Network access when preparing the private runtime for the first time.

Prepare the pinned private runtime:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\prepare-runtime.ps1
```

Build and test .NET code:

```powershell
dotnet build iPhoneMirror.slnx -c Release
dotnet test tests\iPhoneMirror.Core.Tests\iPhoneMirror.Core.Tests.csproj -c Release
```

Run the published WPF lifecycle smoke test with a deterministic fake worker. Stop any running `iPhoneMirror.exe` first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode normal
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode error
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode crash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode reconnect
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode network-retry
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode missing-stack
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode locked
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode untrusted
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-wpf-fake-worker.ps1 -Mode developer-mode
```

For an opt-in physical input diagnostic, set `IPHONE_MIRROR_TIMING_LOG` before
launching the app. The worker records command durations and video sink queue
timings without coordinates or payloads, which separates HID/media processing
from native GUI probe overhead.

Validate the installer in an isolated per-user directory after stopping the application:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-installer.ps1
```

Run the worker tests with the prepared private Python runtime:

```powershell
Push-Location worker
..\artifacts\runtime\python\python.exe .\test_worker.py -v
Pop-Location
```

Publish the self-contained application after the runtime is prepared:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-windows.ps1 -SkipRuntimePreparation
```

Build the installer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\windows\build-installer.ps1
```

Outputs:

```text
artifacts\publish\win-x64
artifacts\installer\iPhoneMirror-Setup-x64.exe
```

The runtime preparation script pins and verifies the downloaded runtime artifacts before use. The publish step also verifies the expected private `pymobiledevice3` version.

## Validation status

As of 2026-09-20, the Windows port has local evidence for:

- Release build with no warnings/errors.
- .NET unit coverage for viewport/letterboxing, DPI mapping, CLI parsing, settings fallback, sanitized errors, and per-user single-instance/named-pipe behavior.
- Python worker coverage for setup confirmation, safe error mapping, USB/auto selection, Wi-Fi pairing ambiguity, route filtering, input release/reconnect, modifier ordering, bounded scroll, Spotlight, and clipboard size limits.
- Reproducible self-contained publish using the bundled Python 3.14.0 runtime, `pymobiledevice3==11.13.1`, and MPV `v0.41.0-1023-g69e63f425` (Sep 3 2026 build).
- Successful Inno Setup compilation.
- Real per-user install, Start Menu shortcut creation, installed-copy `status`/`start`/`stop`, clean GUI process exit, and uninstall removal of application files/shortcut.
- Physical USB mirroring and cable-disconnected direct-IP Wi-Fi mirroring on Windows 11, including live video, touch input, drag, wheel scrolling, keyboard/modifiers, Home, Spotlight, clipboard paste, disconnect/reconnect, and two Home-to-Safari tap trials.
- Worker behavioral tests passing 28/28 and .NET tests passing 23/23, including nonblocking input writes, bounded touchscreen HID recovery, bounded stale-HID release cleanup, malformed-event sanitization, media-task supervision, and worker process-exit cleanup coverage.
- Published WPF fake-worker smoke passing for normal startup/restart/second-instance/stop, terminal error cleanup, worker crash cleanup, manual reconnect, bounded automatic network retry, missing Apple device support, locked-device guidance, USB trust guidance, and Developer Mode guidance under Windows PowerShell 5. The isolated installer verifier passes first install, in-place update, bundled-runtime CLI status, silent uninstall, and install-directory removal.

Physical Windows evidence is still required for App Switcher device state, multiple-device behavior, locked/untrusted/unprepared guidance, network interruption recovery, and clean-machine/update installer validation. Those items must pass before the Windows port is called complete or the old Linux implementation is removed.

## Project tracking

- `PLAN.md` contains the architecture and acceptance plan plus the current milestone.
- `TODO.md` records verified, in-progress, and blocked work.
- `FIXES.md` is an append-only engineering issue/fix log.
- `INSTRUCTIONS.md` records permanent implementation constraints.

## Credits

- Original project: [daniellemky/omarchy-iphone-mirror](https://github.com/daniellemky/omarchy-iphone-mirror)
- Original author: Daniel Lemky
- Windows port/project: `@TomerGamerTV`

This repository's Windows application is a port/rewrite derived from the original project. This credit does not imply that the original author created or endorsed the Windows rewrite.

## License

Original project code is licensed under MIT, copyright 2026 Daniel Lemky. See `LICENSE`.

The application uses `pymobiledevice3`, licensed GPL-3.0-or-later. The combined distribution must comply with applicable third-party license obligations. See `THIRD_PARTY_NOTICES.md`, the bundled Python license, and MPV notices for additional details.
