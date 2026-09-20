# Windows Port TODO

Legend: `[ ]` not started, `[-]` implemented/in progress but not fully accepted, `[x]` verified complete for its stated scope, `[!]` blocked.

## 1. Project skeleton and Windows architecture
- [x] Create .NET 10 WPF/Core/CLI/Test projects and worker layout. Validation: Release solution build succeeds with 0 warnings/errors.
- [-] Define newline-delimited JSON worker protocol and process supervision. Validation: installed GUI/CLI lifecycle works; fake-worker protocol/process-death coverage and the published WPF startup/restart/stop/reconnect smoke pass, while real network interruption remains incomplete.
- [!] Remove obsolete Linux structure only after replacement paths and physical-iPhone acceptance are verified.

## 2. Core worker
- [-] Port CoreDevice session orchestration to private Python worker. Physical USB CoreDevice/DisplayService startup and cable-disconnected direct-IP Wi-Fi startup reach `running` on a paired test iPhone.
- [x] Add sanitized error mapping and diagnostic-only stderr. Validation: worker and .NET privacy/error tests pass and stderr intentionally discards payload text in the frontend.
- [-] Add worker shutdown ordering and crash recovery tests. The UI now clears local touch/key state and disposes MPV when the worker exits; fake-worker protocol and published WPF tests cover normal response, process exit, stream error cleanup, same-worker reconnect, exit notification, and pending-request failure. Real network interruption coverage remains pending.

## 3. USB/Wi-Fi connection
- [x] Implement USB-first `auto`, explicit `usb`, explicit `wifi`, serial selection and ambiguity handling. Validation: worker selection tests cover USB preference, Wi-Fi fallback, explicit-USB fail-closed behavior, and multiple-device rejection.
- [x] Implement Windows route/interface filtering for Wi-Fi discovery. Validation: tests cover LAN acceptance plus loopback, VPN, and Apple Mobile Device Ethernet rejection.
- [-] Apple device-stack detection with actionable installer/GUI errors. Local usbmux endpoint detection/error mapping exists, and the published WPF smoke now verifies the sanitized `apple_device_support_missing` GUI error; disconnected physical-stack behavior and clean-machine validation remain pending.

## 4. Video streaming
- [-] Preserve DisplayService + RTP/HEVC receiver flow and VPS/SPS/PPS initialization. Physical USB and cable-disconnected direct-IP Wi-Fi video are proven; a 30-second Wi-Fi steady-state playback interval is also proven, while failure recovery remains pending.
- [-] Stream encoded HEVC from worker to app without persistence. Physical USB and Wi-Fi mirror refresh are proven after fixing partial named-pipe writes; the live sink now keeps a 30-chunk bounded buffer and preserves the startup codec header to avoid seconds of visible input lag.
- [-] Handle stream timeout, disconnects, and decode failures. Both media tasks are now supervised and completed-task exceptions are consumed safely; the WPF viewer now disposes its embedded player and overlay state on worker `error`/`stopped` events; decoder fallback is physically proven, while network interruption recovery remains unverified.

## 5. Embedded player
- [-] Embed bundled MPV HWND inside WPF. Live physical USB iPhone video, a 30-second Wi-Fi steady-state playback interval, and decoder-failure recovery are proven; network disconnect/reconnect remains pending.
- [x] Hardware HEVC decode preference with software fallback. Validation: normal launch used bundled MPV `--hwdec=auto-safe`; after terminating that player, the app automatically relaunched it with `--hwdec=no`, restored live video, and kept the Wi-Fi session running.
- [x] Keep MPV embedded without independent player chrome. Validation: live physical USB mirroring runs inside the WPF viewer using the embedded MPV HWND.

## 6. Touch/keyboard/input
- [-] WPF tap, drag, wheel gestures, keyboard, and modifiers. Physical tap, keyboard/modifier input, Wi-Fi drag, and a visible Wi-Fi wheel scroll are proven. Ctrl+1/Ctrl+2/Ctrl+3 now mirror Apple’s Home/App Switcher/Spotlight host shortcuts; all input controls now use non-blocking worker writes and a 250 ms live-session overlay keep-alive prevents the late MPV z-order race. An opt-in worker timing log shows sub-millisecond HID command handling across five live Wi-Fi open/back cycles; visible frame reaction and App Switcher device state remain pending.
- [x] Home and Spotlight actions. Validation: both actions were physically verified on the connected iPhone.
- [x] Coordinate mapping for letterboxing and DPI scaling. Validation: .NET viewport tests pass.
- [x] Input release and fresh-action reconnection semantics. Unit tests cover release and replay of the fresh click after touchscreen HID recovery; Wi-Fi disconnect/reconnect and focus-loss release are physically proven. Three additional focus-loss cycles returned focus to the mirror and each fresh tap reached the iOS Version page in 144–161 ms with the session remaining live.

## 7. Clipboard
- [x] Windows text clipboard only, explicit Ctrl+V, 1 MiB limit. Validation: limit is unit-tested and `MirrorPaste-20260920-Retry` was physically pasted into Safari over Wi-Fi.
- [x] Pasteboard confirmation followed by iPhone Command+V. Validation: the worker’s pasteboard async context was fixed; the app reported “Pasted to iPhone” and Safari displayed the pasted text.
- [x] Clipboard privacy guardrails. Validation: clipboard content is not logged and oversized payloads are rejected before sending.

## 8. Setup wizard
- [-] USB detection/trust guidance. Device enumeration now probes trust, device name, and Developer Mode without auto-pairing and shows actionable state summaries; locked/untrusted guidance still needs explicit physical state coverage.
- [-] Developer Mode reveal action with confirmation. Confirmation gate is tested; physical action pending.
- [-] Developer image preparation with confirmation. Confirmation gate exists; physical mount pending.
- [x] Explicit Wi-Fi pairing with confirmation. Validation: unapproved pairing returns `confirmation_required`; approved pairing succeeded and a saved record exists for the target device.
- [-] Display-service validation and final connection test. Physical USB DisplayService/video/input is proven; explicit Wi-Fi discovery currently returns `wifi_unreachable` while the cable is attached.

## 9. Windows lifecycle/single-instance support
- [x] Per-user single-instance lease and named-pipe commands. Validation: primary/secondary ownership and pipe round-trip .NET tests pass.
- [x] `%LOCALAPPDATA%` state/cache and `%APPDATA%` settings paths. Validation: RuntimePaths test passes.
- [x] `start`, `stop`, `restart`, `status` CLI plumbing. Validation: rebuilt installed copy passed `status` → `start` → `status` → `stop` and exited cleanly with no phone attached.
- [-] Focus-existing-instance and restart behavior with an active physical session. The bundled CLI restart was physically verified over Wi-Fi with a restricted PATH and returned to `running` with live video; USB and clean-machine coverage remain pending.

## 10. UI polish
- [-] Windows 11 WPF styling, Mica, and light/dark synchronization. Implemented; final visual/accessibility review pending.
- [-] Responsive viewer with overlay controls/status/setup/error UI. Apple Mirroring reference captured and documented; an owned top-edge Home/App Switcher toolbar is visibly accepted over live USB video. Home routing is proven; App Switcher click routing is proven but the iPhone-side state change is still unverified.
- [-] Complete DPI/accessibility review. Screen-reader names/live regions now include the custom embedded viewer surface, keyboard focus indication is implemented, and a published-build keyboard-only traversal through the setup panel was verified while Wi-Fi mirroring stayed live; representative runtime scaling, high-contrast, and screen-reader review remains pending.

## 11. Packaging
- [x] Self-contained win-x64 publish. Validation: published GUI/CLI run without relying on system .NET.
- [x] Bundle private Python 3.14.0 + `pymobiledevice3==11.13.1`. Validation: installed runtime reports exact versions from the install directory.
- [x] Bundle MPV, licenses, and third-party notices. Validation: installed MPV reports `v0.41.0-1023-g69e63f425`; publish checks required notice files.
- [x] Installer, Start Menu shortcut, uninstall, and no PATH mutation. Validation: Inno Setup 6.7.3 compile succeeds; per-user install and uninstall remove app files/shortcut; a fresh installer was rebuilt after the physical-device fixes at `artifacts\installer\iPhoneMirror-Setup-x64.exe`.

## 12. Automated tests
- [x] Python worker behavioral tests for currently mockable safety/selection/input/stream behavior. Validation: 28 tests pass with bundled Python, including direct Wi-Fi endpoint, partial HEVC pipe-write, bounded HEVC buffering, touchscreen HID timeout recovery, input recovery, device listing/state probing, device-state error mapping, media-task supervision, and App Switcher gesture coverage.
- [x] .NET Core tests for current deterministic core behaviors. Validation: 23 tests pass, including fake-worker process-exit, in-flight request failure, non-blocking input writes, stale-MPV restart filtering, and malformed-event sanitization.
- [-] Fake-worker WPF integration tests for startup/reconnect/disconnect/crash/stream-ready/error/second-instance/shutdown. `scripts/verify-wpf-fake-worker.ps1` now covers published WPF startup, fake stream-ready/running events, named-pipe restart, clean shutdown, worker-reported `stream_timeout`, missing Apple device support, locked-device, USB trust, and Developer Mode guidance, worker crash cleanup, same-worker recovery after a stream error, and a second `start` command with exactly one GUI process; real network interruption and UI-level focus assertions remain pending.
- [-] Installer validation. Real install/reinstall/lifecycle/uninstall works on this Windows 11 machine; `scripts/verify-installer.ps1` now automates isolated install, in-place update over the existing installation, bundled CLI status under a restricted PATH, and silent uninstall with exit code 0. A clean VM with no system Python/MPV and update-over-an-older-release coverage remains pending.

## 13. Physical-iPhone validation
- [-] USB trust/setup/display/video/input/clipboard/Home/Spotlight/App Switcher/reconnect evidence. Physical USB DisplayService, live mirror refresh, tap routing, keyboard/modifier input, Home, Spotlight, toolbar visibility/click routing, Wi-Fi wheel scrolling, clipboard paste, Wi-Fi disconnect/reconnect, two clean Home-to-Safari Wi-Fi tap trials, three repeated Wi-Fi session cycles, focus-loss release, and repeated Wi-Fi navigation checks are proven on a paired test iPhone; the published bundled worker also reports the live paired Wi-Fi device state read-only. A warm-up action dispatched in 409 ms, followed by five open/back cycles with 125–170 ms dispatch per action; all ten actions reached the requested state after a 350 ms wait. Device selector guidance now distinguishes trust, Developer Mode, and Wi-Fi pairing states. App Switcher device state remains.
- [x] Wi-Fi pairing and cable-disconnected mirroring evidence. Pairing confirmation and saved pairing record are proven. Bonjour discovery returned zero endpoints, so a persisted direct-IP/port fallback was added. With USB physically disconnected, Wi-Fi explicitly selected, address `the test phone's local address`, and port `49152`, the app reached `Live · WIFI`, rendered video, and accepted a click that opened an iPhone app.
- [-] Multi-device and locked/untrusted/unprepared guidance evidence. The WPF selector no longer silently chooses the first phone when multiple devices are detected, and the worker rejects ambiguous requests; physical two-device and state-specific acceptance still requires compatible test phones.

## 14. Documentation and final cleanup
- [x] Rewrite README for Windows while retaining credits/license material.
- [!] Remove Linux/Omarchy-only code/docs/tests only after equivalent Windows physical-device validation passes.
- [-] Record final device/iOS compatibility findings and screenshots after physical acceptance. USB evidence captures exist under `artifacts\`; final Wi-Fi and remaining state coverage are still pending.
