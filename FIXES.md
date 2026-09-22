# Engineering Fix Log

Append-only. Record failed approaches so later work does not repeat them.

## 2026-09-20 10:32:00 +03:00 — Verify toolbar follows a moved mirror window
- Validation result: The published mirror window was dragged by 120×100 screen pixels while the top-edge toolbar was visible. The window capture origin changed from `(827, 298)` to `(947, 398)`, and the toolbar remained centered over the moved viewer; the session stayed `Live · WIFI`.
- Result: The earlier detached-toolbar regression is not reproduced in the current published build.

## 2026-09-20 10:31:00 +03:00 — Announce viewer error/status changes to assistive technology
- Symptom/error: The viewer's empty/error message had an automation name but was not declared as a live region, so screen readers could miss connection and failure text changes.
- Component: WPF viewer accessibility markup.
- Final fix: Added an explicit `Viewer message` automation name and polite live-region setting to `EmptyMessage`.
- Validation result: Release build succeeded with zero warnings/errors, .NET tests pass 16/16, the self-contained publish and installer were rebuilt, the published app returned to `running` over Wi-Fi, and `verify-publish.ps1 -RequireInstaller` passed.
- Remaining limitation: Runtime high-contrast, DPI, screen-reader, and keyboard-only review is still pending beyond this markup fix.

## 2026-09-20 10:24:00 +03:00 — Network interruption test requires adapter privileges
- Attempt: Tried a reversible six-second disable/re-enable of the active `Ethernet` adapter while the mirror was live.
- Result: Windows returned `Access is denied` for `Disable-NetAdapter`; the adapter remained Up and no interruption occurred. No network-recovery claim was made.
- Remaining limitation: A real network interruption still needs an authorized adapter/firewall test environment or an external cable/link change.

## 2026-09-20 10:22:00 +03:00 — Verify MPV hardware-to-software fallback recovery
- Validation result: A normal republished session launched bundled MPV with `--hwdec=auto-safe`. Terminating only that MPV process caused the app to create a replacement MPV with `--hwdec=no`; the same Wi-Fi session remained `running` and the viewer again rendered the iOS Version page without manual reconnect.
- Result: Hardware-preference launch and automatic software-decoder recovery are physically proven on the verified iPhone/session. Network interruption recovery remains separate.

## 2026-09-20 10:19:00 +03:00 — Verify sustained Wi-Fi playback
- Validation result: The published mirror was held live for 30 seconds over Wi-Fi. Bundled CLI status reported `running` before and after with the same Wi-Fi serial, and a fresh viewer capture still showed the iOS Version page with `Live · WIFI` and no reconnect.
- Remaining limitation: This proves a 30-second steady-state playback interval; deliberate network interruption and decoder-fallback recovery remain separate tests.

## 2026-09-20 10:17:00 +03:00 — Verify fresh input after repeated focus loss
- Validation result: The published Wi-Fi mirror was activated, focus was moved to ChatGPT for 180 ms, and focus was returned before each fresh tap. Three cycles dispatched the tap in 161, 144, and 158 ms; each opened the iPhone's iOS Version page after a 350 ms settling interval. The final viewer remained `Live · WIFI` with no reconnect or dropped input.
- Result: The remaining fresh-action-after-focus-loss acceptance item is complete for the verified device/session.

## 2026-09-20 10:20:00 +03:00 — Confirm App Switcher is a host-level Apple action
- Evidence: Apple's current iPhone Mirroring documentation explicitly maps Command+1/2/3/4 to Home, App Switcher, Spotlight, and Control Center. It describes these as Mirroring toolbar and keyboard actions rather than ordinary iPhone touch gestures.
- Conclusion: This matches the physical tests: ordinary bottom-edge touch, Command+2 forwarded through the phone HID keyboard, the telephony button surface, and the dedicated gesture surface did not open App Switcher. The Windows toolbar routing remains correct, but Apple's private host command is not exposed by the pinned CoreDevice library.

## 2026-09-20 10:15:00 +03:00 — Verify installer replacement over an existing install
- Validation result: The current installer was run twice into the same isolated directory. Both installs exited 0, the installed CLI remained usable with a restricted `PATH`, and it reported the live Wi-Fi session after replacement. The silent uninstaller exited 0 and removed the directory during asynchronous cleanup.
- Remaining limitation: Both passes used the same current installer; a true older-build upgrade and clean-VM test remain pending.

## 2026-09-20 10:08:00 +03:00 — Correct Wi-Fi device count wording
- Symptom/error: The device refresh status always said `USB iPhone`, even when the saved paired device was discovered over Wi-Fi.
- Component: WPF setup/device picker status text.
- Final fix: Changed the summary to use neutral `iPhone` wording while the picker continues to show the actual transport and trust state per device.
- Validation result: Release build succeeded with zero warnings/errors, .NET tests pass 16/16, the self-contained publish and installer were rebuilt, the published app reconnected to `Live · WIFI`, and `verify-publish.ps1 -RequireInstaller` passed.

## 2026-09-20 10:00:00 +03:00 — Smoke-test installer in an isolated directory
- Validation result: The current installer installed silently into a workspace-local directory with exit code 0. The installed `iphone-mirror.exe status` ran with only `C:\Windows\System32` on `PATH`, returned exit code 0, and reported the live Wi-Fi session. The generated uninstaller also exited 0; the installation directory was removed after asynchronous cleanup completed.
- Remaining limitation: This is a same-machine isolated install, not a clean VM or update-over-older-build test.

## 2026-09-20 09:54:00 +03:00 — Test the dedicated touchscreenGesture surface
- Attempt: Sent a bottom-to-middle swipe through the enumerated `touchscreenGesture` HID surface (`_ServiceID 1281`, `CoreDevice touchscreenGesture`, trackpad usage) over a second direct Wi-Fi tunnel while the published mirror remained live.
- Physical result: The gesture surface accepted the reports and moved the mirrored pointer, but the iPhone stayed on the Settings page; no App Switcher appeared and the main session remained `Live · WIFI`.
- Conclusion: The separate gesture surface is a pointer/trackpad route, not the missing App Switcher command. No production behavior was changed.

## 2026-09-20 09:50:32 +03:00 — Confirm repeated Wi-Fi input latency after idle
- Symptom/error: A single post-idle action still felt slower than the steady-state trials, so the latency fix needed another live sample.
- Component: Published WPF viewer and Wi-Fi HID input path.
- Validation result: One warm-up back action dispatched in 409 ms. Five subsequent open/back cycles dispatched in 125–170 ms per action; all ten actions reached the expected page after a 350 ms settling interval. The final published viewer remained `Live · WIFI` with no reconnect, dropped action, or frozen stream.
- Remaining limitation: This measures command dispatch and screen-state correctness; the screenshot API used for observation adds several seconds and is excluded from the latency values.

## 2026-09-20 10:18:00 +03:00 — Name the embedded viewer for accessibility
- Symptom/error: Interactive setup controls and status regions had automation names, but the custom embedded video surface itself was exposed without a clear accessible name.
- Component: WPF viewer markup.
- Final fix: Added automation names for the iPhone screen viewer, mirrored screen host, and empty viewer status overlay.
- Validation result: Release WPF build completed successfully, the worker suite remains green at 27/27, the self-contained publish and installer were rebuilt, and the republished app reconnects to `Live · WIFI` with video.
- Remaining limitation: Runtime screen-reader and high-contrast review on multiple DPI scales remains pending.

## 2026-09-20 10:05:00 +03:00 — Validate published device-state enumeration
- Validation result: The published worker was run read-only with its bundled Python 3.14.0 and `pymobiledevice3==11.13.1`, with no external runtime on `PATH`. `list_devices` returned the target iPhone over Wi-Fi with `trusted: true`, `wifi_paired: true`, and a privacy-safe state summary (`developer_mode: false`).
- Remaining limitation: USB trust, locked-device, and unprepared-device states still require physical state coverage.

## 2026-09-20 09:50:00 +03:00 — Validate active-session CLI restart
- Validation result: With the published mirror actively connected over Wi-Fi, the bundled CLI was run with a restricted `PATH` using `restart --connection wifi --serial a paired test iPhone`. It returned exit code 0; after reconnect, `status` reported `running` and the live viewer again rendered the iPhone iOS Version page.
- Remaining limitation: The same active-session restart path still needs a USB physical run and clean-machine coverage.

## 2026-09-20 09:45:00 +03:00 — Add self-contained publish verifier
- Symptom/error: Installer compilation alone did not verify that the final publish contained every runtime, worker, and notice file or that the bundled versions were usable.
- Component: Windows packaging validation.
- Final fix: Added `scripts/verify-publish.ps1`, which checks the published GUI/CLI/worker/runtime files, validates bundled `pymobiledevice3==11.13.1`, runs bundled MPV version reporting, and optionally requires the installer artifact.
- Validation result: `verify-publish.ps1 -RequireInstaller` passes against the current publish and installer.
- Remaining limitation: A clean VM and update-over-older-build run remain pending.

## 2026-09-20 09:36:00 +03:00 — Observe both media tasks during stream lifetime
- Symptom/error: The session loop only inspected the UDP receiver task. An RTCP feedback task could terminate while the UI continued to advertise a live session, and task exceptions could remain unobserved.
- Component: `worker/session.py` stream supervision.
- Final fix: Inspect every media task on each loop, retrieve completed-task exceptions without exposing their text, and stop with the sanitized `stream_ended` code when either task ends.
- Validation result: Worker suite passes 27/27, the updated worker is present in the self-contained publish, the installer was rebuilt, and the republished app reconnects to `Live · WIFI` with live video.
- Remaining limitation: Physical network interruption and decoder fallback coverage remain separate acceptance items.

## 2026-09-20 09:31:00 +03:00 — Tested telephony-style App Switcher candidates
- Attempt: The authenticated `mainScreenButtons` surface reports HID usage page `0x0B` (Telephony Device), usage `0x01` (Phone). Because the standard Consumer Control reports had no effect, the Indigo button path was tested with Consumer usage codes `0x41` and `0x42`.
- Physical result: Neither candidate changed the iPhone from the iOS Version page. The Wi-Fi mirror remained live.
- Conclusion: The service metadata identifies the surface but does not expose its report descriptor or Apple-specific button mapping. No speculative report was added to production code.

## 2026-09-20 09:24:00 +03:00 — Remove round-trip waits from host input controls
- Symptom/error: Direct screen touches used non-blocking worker writes, but toolbar actions and Ctrl+1/Ctrl+2/Ctrl+3 still awaited a response before the UI event completed.
- Component: WPF toolbar and host shortcut dispatch.
- Final fix: Route Home, App Switcher, Spotlight, and the toolbar action event through `SendCommandNoWaitAsync`, preserving stdin ordering while removing the Wi-Fi response wait from input handling.
- Validation result: Release WPF build succeeded, the self-contained publish completed, Inno Setup compiled the updated installer, and the republished app reconnected to `Live · WIFI` and rendered the iPhone iOS Version page.
- Remaining limitation: Physical App Switcher state change is still unverified because the iPhone-side command protocol remains unknown.

## 2026-09-20 09:12:00 +03:00 — Repeated Wi-Fi latency retest
- Symptom/error: The previous latency improvement was uncertain because an occasional delayed tap had been observed.
- Component: Wi-Fi touch dispatch and live HEVC viewer.
- Validation result: Six back/open navigation trials were run against the published app while connected to `a direct Wi-Fi endpoint`. Dispatch times were 379, 146, 174, 149, 162, and 155 ms. Every trial reached the requested destination after a 250 ms wait; the session stayed `Live · WIFI` with no reconnect or dropped action.
- Remaining limitation: One 379 ms host-dispatch outlier remains. The screenshot API itself adds several seconds, so this test measures command dispatch plus state correctness separately from capture time.

## 2026-09-20 09:05:00 +03:00 — Replace placeholder device status in the picker
- Symptom/error: Every USB device was displayed as untrusted with Developer Mode disabled because `list_usb_devices` returned hard-coded false values.
- Component: Setup device enumeration and WPF device selector.
- Final fix: Probe each USB device through Lockdown with `autopair=False`, read its name and Developer Mode status, preserve privacy-safe state error codes, and show the name plus trust/Developer Mode summary in the picker. The probe runs concurrently for multiple devices and never changes pairing state.
- Validation result: Worker tests pass 26/26, .NET tests pass 16/16, the published build and installer were rebuilt, and the live Wi-Fi picker shows the saved phone as `Wi-Fi · Trusted` while the mirror remains `Live · WIFI`.
- Remaining limitation: Physical locked, untrusted, and unprepared phone states still need acceptance evidence.

## 2026-09-20 08:54:00 +03:00 — Enumerated live HID surfaces for App Switcher investigation
- Validation result: A second direct Wi-Fi CoreDevice tunnel enumerated the active iPhone surfaces while mirroring remained live: `_ServiceID 257` `CoreDevice touchscreen`, `512` `CoreDevice keyboard`, `1026` `CoreDevice mainScreenButtons` (authenticated), `1280` `CoreDevice avpCustom`, and `1281` `CoreDevice touchscreenGesture`.
- Finding: The current implementation correctly uses the real touchscreen surface (`257`). The authenticated `mainScreenButtons` surface (`1026`) is a stronger candidate for Apple’s Home/App Switcher host controls than another ordinary bottom-edge touch. Its raw report layout is not exposed by the public library and remains to be captured or inferred.
- Remaining limitation: No App Switcher report has been sent speculatively; the current gesture path remains unchanged until the report format is known.

## 2026-09-20 08:58:00 +03:00 — Consumer Control report experiment did not trigger Home
- Attempt: Sent standard Consumer Control `AC Home` reports to the authenticated `mainScreenButtons` surface (`1026`), both as a two-byte usage and with a report-ID prefix, followed by release reports.
- Physical result: The iPhone stayed on the iOS Version page and the Wi-Fi mirror remained live.
- Conclusion: Surface `1026` is not a plain two-byte Consumer Control endpoint, or it requires an Apple-specific report/envelope. No production code was changed and the existing Indigo Home path remains intact.

## 2026-09-20 08:47:00 +03:00 — Preserve actionable device-state errors
- Symptom/error: Several pymobiledevice3 setup failures were reduced to the generic `connection_failed` message.
- Component: Worker exception mapping and WPF error catalog.
- Root cause: The worker recognized pairing and transport failures, but not the library's explicit locked-device, untrusted-device, developer-image, iOS-version, and display-feature exception types.
- Final fix: Map those exception names to stable privacy-safe error codes and add user-facing guidance for unlocking, trusting, mounting the image, and unsupported capabilities.
- Validation result: Worker suite passes 24/24, .NET suite passes 16/16, the published worker contains the new mappings, and the republished build is live over Wi-Fi.
- Remaining limitation: Physical locked/untrusted/unprepared phone states still need acceptance evidence.

## 2026-09-20 08:50:00 +03:00 — Repackaged device-state mappings
- Validation result: The published app and installer were rebuilt after the exception-mapping change. Inno Setup 6.7.3 compiled successfully in 135.625 seconds; the current installer is 144,797,793 bytes with SHA-256 `7074A5A3C6CD32E05D0F1E96564D618A8FF37BAF7371D7A8DD0FF3FA8066D3BA`. The published build is connected over Wi-Fi and rendering the iOS Version page.

## 2026-09-20 08:39:17 +03:00 — Rebuilt installer after latency changes
- Symptom/error: The first installer attempt could not replace `runtime\\mpv\\mpv.exe` because the live published mirror was still running.
- Component: Windows packaging.
- Root cause: The running viewer holds the bundled MPV executable open during packaging.
- Attempted fixes: The initial build was allowed to run while the live app was connected and failed with an access-denied file lock.
- Final fix: Stop the published app for the build, rerun the complete publish and Inno Setup pipeline, then relaunch the published app and restore its Wi-Fi session.
- Validation result: Inno Setup 6.7.3 compiled successfully in 120.594 seconds. Installer size is 144,785,011 bytes; SHA-256 is `2FD2C2CF29CA10D54966209332FA9CE2F9BB2DC6D6C2E8E19F4F0342C7DBCE0A`. The republished app is connected over Wi-Fi and showing the live iOS Version page.
- Remaining limitation: Clean-machine installer validation and update-over-older-build coverage remain pending.

## 2026-09-20 08:44:00 +03:00 — Fresh installer install/uninstall validation
- Validation result: The current installer installed silently into a fresh temporary per-user directory with the GUI executable, worker, MPV, and private Python runtime present. The bundled uninstaller exited 0, removed the directory, and removed the per-user uninstall registry entry.
- Remaining limitation: This is a same-machine clean-directory check; a separate clean VM and update-over-older-build run remain pending.

## 2026-09-19 22:40:18 +03:00 — Port initialized
- Symptom/error: Existing repository is Linux/Omarchy-only; no Windows application project exists.
- Component: Repository architecture.
- Root cause: Windows rewrite has not started.
- Attempted fixes: None before this entry.
- Final fix: Began a clean Windows architecture alongside the existing implementation so behavior can be migrated and verified before Linux cleanup.
- Validation result: Repository was clean on master before mutation; .NET 10 SDK and Python 3.14 are installed on the development machine.
- Remaining limitation: Feature implementation and real-iPhone acceptance remain pending.

## 2026-09-19 22:40:55 +03:00 — WPF template target argument rejected
- Symptom/error: dotnet new wpf -f net10.0-windows reported that
et10.0-windows is not an accepted template framework value.
- Component: .NET project scaffolding.
- Root cause: The installed .NET 10 WPF template accepts
et10.0 and emits the Windows target framework itself.
- Attempted fixes: Initial scaffold used -f net10.0-windows and failed before creating the WPF project.
- Final fix: Re-run the WPF template with -f net10.0, then add the generated project to the solution.
- Validation result: Pending build after corrected scaffold.
- Remaining limitation: None expected from this template argument issue.

## 2026-09-19 23:18:00 +03:00 — Inno version metadata rejected prerelease text
- Symptom/error: Inno Setup rejected `VersionInfoProductVersion=0.2.0-alpha` as invalid.
- Component: Windows installer metadata.
- Root cause: PE version-resource fields require numeric dotted versions even when the user-facing app version contains a prerelease suffix.
- Attempted fixes: Initial installer script reused the display version in the numeric version-resource field.
- Final fix: Keep `AppVersion=0.2.0-alpha` for display and use `VersionInfoProductVersion=0.2.0.0` for the PE resource.
- Validation result: Pending installer recompilation.
- Remaining limitation: Installer still needs install/uninstall validation after recompilation.

## 2026-09-19 23:19:00 +03:00 — Async close could terminate before CoreDevice cleanup
- Symptom/error: WPF `Closing` used an `async void` handler without cancelling the first close, so the application could exit before awaited worker/display cleanup completed.
- Component: Windows lifecycle / CoreDevice session cleanup.
- Root cause: WPF closing continues immediately after an `async void` handler yields.
- Attempted fixes: Initial implementation awaited cleanup directly in the `Closing` handler.
- Final fix: Cancel the first close, complete session/worker cleanup, then issue a second allowed close.
- Validation result: Pending rebuild and CLI stop/relaunch validation.
- Remaining limitation: Physical-iPhone stream cleanup still requires real-device proof.

## 2026-09-19 23:46:50 +03:00 — Installed CLI stop exposed WPF close re-entrancy
- Symptom/error: On the first real installed-copy lifecycle run, `iphone-mirror.exe stop` caused `InvalidOperationException: Cannot set Visibility to Visible or call Show, ShowDialog, Close, or WindowInteropHelper.EnsureHandle while a Window is closing`, and the GUI process remained alive.
- Component: Windows lifecycle / CLI control pipe / WPF shutdown.
- Root cause: The async `Closing` handler issued its second `Close()` before WPF had fully returned from the original closing event. The control-pipe stop path also duplicated session cleanup before calling `Close()`.
- Attempted fixes: The earlier cancel/await/direct-second-close pattern preserved async cleanup but was still re-entrant when cleanup completed quickly.
- Final fix: Make the control-pipe `stop` command dispatch only `Close()`, let the `Closing` handler exclusively own session/worker cleanup, and queue the final allowed close with `Dispatcher.BeginInvoke` so it runs after the first closing event has returned.
- Validation result: Release build succeeded with 0 warnings/errors; the rebuilt installer compiled successfully; the installed copy passed `status` → `start` → `status` → `stop`; the GUI process exited; final status returned stopped. Bundled Python 3.14.0, `pymobiledevice3==11.13.1`, and MPV `v0.41.0-1023-g69e63f425` were executed from the install directory.
- Remaining limitation: Ordered shutdown with an active physical DisplayService/HID session still requires real-iPhone proof.

## 2026-09-19 23:46:50 +03:00 — Worker test invocation assumed a package layout
- Symptom/error: Running the worker suite as `python -m unittest worker.test_worker` failed because the private embeddable Python runtime does not treat the script directory as a package in that invocation, and `test_worker.py` attempted to import `worker.worker` even though `worker.py` is a script module.
- Component: Python worker tests / private runtime.
- Root cause: The test import path assumed package semantics that the deployed worker layout intentionally does not use.
- Attempted fixes: Running `-m unittest` from both repository root and the worker directory still failed under the embedded runtime.
- Final fix: Import the worker entrypoint as the local `worker` module after placing the worker directory first on `sys.path`, and run the suite directly as `artifacts\runtime\python\python.exe worker\test_worker.py -v` (or the equivalent from the worker directory).
- Validation result: 18 worker tests passed using the exact bundled Python runtime.
- Remaining limitation: Full fake-worker WPF integration coverage remains separate work.

## 2026-09-20 00:45:00 +03:00 — Embedded MPV child window intercepted viewer clicks
- Symptom/error: The physical iPhone responded to programmatic input, but normal clicks on the mirrored phone surface in the Windows app did not reach the iPhone.
- Component: WPF viewer / embedded MPV input routing.
- Root cause: MPV owns a native child HWND above the WPF surface, so the MPV child received mouse hit-testing before WPF mouse handlers could see those events.
- Attempted fixes: The existing WPF mouse handlers were correct when invoked directly, but they could not receive ordinary user input through the MPV child HWND.
- Final fix: Add a dedicated native `iPhoneMirror.InputOverlay` child HWND above the MPV child and route its mouse events into the existing WPF/worker touch pipeline.
- Validation result: Runtime child-window z-order showed `iPhoneMirror.InputOverlay` above `mpv`; sending a click to that overlay caused the real iPhone to open Settings, proving the overlay-to-CoreDevice input path. A separate foreground Unity window prevented a literal foreground desktop click automation check, so that exact variant is not claimed.
- Remaining limitation: Drag/wheel/focus-loss acceptance through normal interactive use still needs completion.

## 2026-09-20 00:50:00 +03:00 — HEVC named-pipe writer could truncate frames on partial writes
- Symptom/error: The Windows app could keep displaying the last valid iPhone frame while the real phone continued changing and accepting input.
- Component: Python worker / HEVC stream transport to embedded MPV.
- Root cause: The Windows named-pipe writer called `write(data)` once and assumed the whole HEVC chunk was consumed. Windows pipe writes can make partial progress, which truncated NAL data and left MPV alive on the previous decodable frame.
- Attempted fixes: Restarting the stream/player could temporarily refresh the frame but did not remove the transport corruption condition.
- Final fix: Write each HEVC chunk through a `memoryview` loop until every byte has been consumed, and fail if a write makes zero progress.
- Validation result: Added `test_hevc_pipe_write_retries_partial_writes`; the bundled-runtime worker suite passes 19/19. On the physical USB session, mirror and direct-device captures both changed from Settings/Home state to the same Home screen after a Home action, proving fresh HEVC frames continued reaching MPV after the fix.
- Remaining limitation: Cable-disconnected Wi-Fi streaming and longer sustained-playback acceptance are still pending.

## 2026-09-20 03:45:00 +03:00 — Bonjour returned no Wi-Fi endpoint on the active LAN
- Symptom/error: Wi-Fi discovery returned zero RemotePairingTunnelService endpoints even though the PC could ping `the test phone's local address` and TCP ports `62078` and `49152` were open.
- Component: Windows Wi-Fi connection selection.
- Root cause: The network did not expose the iPhone's pairing service through the expected Bonjour discovery path.
- Attempted fixes: Retained mDNS discovery for normal networks and verified the route/interface filters; direct pairing-service connection was then tested against the user-provided address.
- Final fix: Added persisted Wi-Fi address and port fields and a direct `RemotePairingTunnelService` fallback when an address is supplied.
- Validation result: With USB disconnected, `a direct Wi-Fi endpoint` reached `Live · WIFI`, rendered live video, accepted input, disconnected cleanly, and reconnected to the same live session. Worker tests pass 21/21, .NET tests pass 15/15.
- Remaining limitation: The direct endpoint must be entered or saved when Bonjour discovery is unavailable; automatic discovery is still unverified on this LAN.

## 2026-09-20 03:46:00 +03:00 — App Switcher gesture still lacks device-state proof
- Symptom/error: The Windows App Switcher toolbar button receives focus/highlight and sends the worker command, but the iPhone remains in the foreground app after the HID swipe.
- Component: HID App Switcher action.
- Root cause: The current bottom-edge swipe coordinates are accepted as touch input, but have not been shown to meet the iPhone's App Switcher gesture threshold.
- Attempted fixes: Tested the worker gesture and a normal mirrored drag on the physical Wi-Fi session; ordinary drag input works, while neither produced App Switcher.
- Final fix: None yet; retained the implementation and marked the device-state acceptance incomplete.
- Validation result: Toolbar routing and tooltip/highlight are proven; no App Switcher card view was observed.
- Remaining limitation: A more faithful edge gesture or another supported HID/App Switcher mechanism is required before this feature can be marked complete.

## 2026-09-20 04:15:00 +03:00 — Input round trips made Wi-Fi taps feel delayed
- Symptom/error: Touch and scroll events waited up to the command timeout for a worker response, causing visible 1–2 second delays when the Wi-Fi link or HID service was busy.
- Component: WPF input path / worker protocol.
- Root cause: High-frequency input was using the same request/response path as deliberate lifecycle commands; every move could queue behind the previous response.
- Final fix: Added an ordered no-wait protocol write for touch, scroll, and held-key updates. Responses remain available for lifecycle, setup, clipboard, and explicit toolbar actions.
- Validation result: Release build passes with 0 warnings/errors and the bundled worker suite passes 21/21.
- Remaining limitation: Physical latency measurement after republishing this change is still pending.

## 2026-09-20 04:18:00 +03:00 — Font glyph substitutions broke the hover toolbar icons
- Symptom/error: Segoe UI Symbol, emoji, and Segoe MDL2 font variants rendered unrelated or malformed glyphs on the user's Windows configuration, including right-to-left-looking labels.
- Component: WPF top-edge viewer toolbar.
- Root cause: Font glyph availability and fallback mapping varied across the installed Windows text environment.
- Final fix: Removed font-dependent artwork and built the Home and App Switcher icons from WPF vector shapes: a filled house and two outlined overlapping cards. Forced the toolbar layout to left-to-right and tracked owner location/size/state changes.
- Validation result: The live republished Wi-Fi window shows recognizable vector icons; the mirror remains `Live · WIFI`.
- Remaining limitation: App Switcher device-state behavior is still separately unverified.

## 2026-09-20 04:32:00 +03:00 — First Wi-Fi input still paid lazy HID setup cost
- Symptom/error: After the no-wait input change, normal taps were usually fast but the first interaction could still take several hundred milliseconds while the touchscreen HID service connected.
- Component: CoreDevice session startup / Universal HID service.
- Root cause: `ensure_hid()` was lazy and ran inside the first touch handler.
- Final fix: Preconnect the touchscreen HID service after the video stream is ready and before emitting the session's `running` state. Warm-up failure remains non-fatal and preserves lazy retry behavior.
- Validation result: Two five-tap physical Wi-Fi runs after republishing measured `[149,553,161,167,154]` ms and `[172,160,178,115,173]` ms; the 553 ms value was the only outlier.
- Remaining limitation: A larger sample under normal user input is still desirable; App Switcher state and other physical acceptance items remain incomplete.
## 2026-09-20 05:44 +03:00 — Reassert native input overlay after MPV startup

- Symptom: the published Wi-Fi viewer rendered live video, but a targeted acceptance tap could fail to reach the phone after MPV playback initialization.
- Component: `src/iPhoneMirror.App/MainWindow.xaml.cs` and the embedded MPV/WPF child-window stack.
- Root cause: MPV can create or raise its video child after the initial overlay placement, allowing it to cover the native input overlay.
- Fix: reassert the overlay z-order immediately after MPV starts and after both `video_format` and `running` events, with short settling delays.
- Validation: rebuilt and republished the app; the live Wi-Fi session accepted a scroll and the Safari page visibly moved.
- Remaining limitation: App Switcher device state, clipboard, focus-loss release, and locked/untrusted setup states still need physical acceptance.
## 2026-09-20 05:51 +03:00 — Pasteboard context cleanup

- Symptom: physical Ctrl+V reached the focused iPhone Safari address field, but the worker returned the generic paste failure.
- Component: `worker/session.py` pasteboard path.
- Root cause: the pasteboard service was manually connected and closed around the operation instead of using the library’s async context manager, which owns the RemoteXPC lifecycle.
- Fix: use `async with PasteboardService(self.rsd)` for the confirmed `set_text` request, then send the iPhone Command+V HID sequence.
- Validation: with the phone connected over Wi-Fi, Safari displayed `MirrorPaste-20260920-Retry` and the app reported “Pasted to iPhone”.
- Remaining limitation: non-text clipboard formats remain unsupported by design.
## 2026-09-20 05:58 +03:00 — App Switcher remains unverified

- Validation attempt: from the live Wi-Fi session, returned to the Home Screen, exposed the toolbar, and invoked the App Switcher button; the phone remained on the Home Screen. A direct bottom-edge drag also did not show the App Switcher.
- Result: toolbar routing and visual selection are functional, but the iPhone-side App Switcher transition is still unverified and remains an explicit limitation.
## 2026-09-20 06:04 +03:00 — Revised App Switcher edge gesture retest

- Change: adjusted the gesture to stop at the screen midpoint, hold for 600 ms, and always release the contact in a `finally` block.
- Validation: worker tests pass 21/21; the live Wi-Fi toolbar action was retested from the Home Screen after republishing, but the phone still remained on the Home Screen.
- Result: the release safety improvement is retained, while device-side App Switcher behavior remains unverified.
## 2026-09-20 06:22 +03:00 — Worker crash left stale host input state

- Symptom: a worker process exit only changed the status text; the WPF host could retain an active touch flag, held keyboard usages, and a live MPV child until a later lifecycle action.
- Component: `MainWindow.Worker_WorkerExited`.
- Fix: clear local touch/modifier/paste state and dispose the embedded MPV session on the UI dispatcher before showing the crash error.
- Validation: Release build succeeds with zero warnings/errors; .NET tests pass 15/15 and worker tests pass 21/21.
- Remaining limitation: a full fake-worker WPF crash integration test is still pending.
## 2026-09-20 06:25 +03:00 — Added fake-worker crash coverage

- Coverage: a bundled-Python fake worker now exercises `WorkerProtocolClient` startup, normal response handling, process termination, `WorkerExited`, and failure of an in-flight request.
- Validation: the .NET suite passes 16/16, including the new crash test.
- Remaining limitation: this is protocol-level coverage; a full WPF/MPV fake-stream integration harness is still pending.

## 2026-09-20 — Repeated latency acceptance retest

- Live session: Wi-Fi endpoint `a direct Wi-Fi endpoint`, connected state remained live throughout the retest.
- Measurements: repeated desktop click delivery calls completed in 124–170 ms; one earlier automation call measured 370 ms and is treated as an automation outlier.
- Result: transport-side timing remains below the reported 1–2 second delay, but the current GUI probe did not produce a visible iPhone screen transition, so end-to-end interactive latency is still not accepted as proven.
- Tests: .NET suite 16/16; bundled worker suite 21/21.

## 2026-09-20 — Input overlay keep-alive and physical retest

- Root cause refinement: MPV can raise its native video child after the initial overlay settling passes, causing later clicks to be visually accepted by Windows but never reach the iPhone input path.
- Fix: keep the native input overlay at the top of the MPV host child stack every 250 ms while a session is live; the existing startup settling passes remain in place.
- Validation: republished and relaunched over Wi-Fi. Two clean Home-to-Safari trials both changed the phone screen successfully. Input delivery calls measured 143–368 ms; screenshot capture time was measured separately and excluded from those timings.
- Installer: SHA-256 `7E799E902D82E5E35D1DBBA04639990B3D7AF60A7522DCEF691B67F11F629B4C`.

## 2026-09-20 — Wi-Fi lifecycle retest after overlay fix

- Validation: disconnected the live Wi-Fi session from the running app, observed `Disconnected` with the viewer cleared, then reconnected to `a direct Wi-Fi endpoint`.
- Result: the app returned to `Connected` / `Live · WIFI` with the existing browser screen rendered again. Disconnect delivery measured 123 ms and reconnect delivery measured 155 ms; the reconnect stream was allowed to settle for 5.5 seconds before visual verification.

## 2026-09-20 — App Switcher protocol investigation

- Inspected the bundled `pymobiledevice3==11.13.1` CoreDevice HID and screen-stream implementations for a named App Switcher or multitasking command.
- Result: the available protocol exposes touchscreen reports and named hardware buttons, including Home, but no App Switcher primitive. The Windows toolbar and Ctrl+2 therefore still use the documented bottom-edge gesture path; the iPhone-side App Switcher transition remains unverified.

## 2026-09-20 — Command+2 App Switcher experiment reverted

- Attempt: changed the worker App Switcher command to send HID Command+2, matching the host shortcut documented for Apple iPhone Mirroring.
- Physical result: after a fresh Wi-Fi session and Home reset, the toolbar App Switcher action left the iPhone on the Home Screen.
- Final decision: reverted to the bottom-edge gesture implementation. Worker tests pass 21/21 and .NET tests pass 16/16 after the revert.
- Current installer SHA-256: `013542EE1FACE6838BF7920A53CF4FC863DA86D87B3FC072097755B1EF02648F`.

## 2026-09-20 — Repeated Wi-Fi session cleanup acceptance

- Validation: completed two additional disconnect/reconnect cycles from the live Wi-Fi viewer. Each disconnect cleared the session and each reconnect returned to `Live · WIFI` with the iPhone Home Screen rendered.
- Measurements: disconnect delivery 112 ms and 126 ms; reconnect delivery 135 ms and 137 ms. Each reconnect was allowed 5.2 seconds to settle before the next cycle.
- Result: repeated display-session open/close is accepted for this paired device; focus-loss release remains a separate acceptance item.

## 2026-09-20 — Repeated live latency retest

- Live session: Wi-Fi endpoint `a direct Wi-Fi endpoint`; the app stayed connected for all trials.
- Trials 1–3: repeated Safari launch taps dispatched in 134–146 ms. After a fixed 350 ms wait, each capture already showed Safari open; capture itself took about 3.1–3.2 seconds and was excluded from input timing.
- Trials 4–6: repeated taps on Safari's visible menu control dispatched in 105–165 ms. The menu visibly toggled on every trial after the same 350 ms wait.
- Result: six consecutive live end-to-end trials do not reproduce a 1–2 second input delay. The remaining perceptual delay, if present in normal use, is more likely tied to video-frame presentation or a specific interaction path than to command dispatch.

## 2026-09-20 — Physical focus-loss input release retest

- Test: started a live Wi-Fi drag in the mirror, activated the ChatGPT window 120 ms later, waited for the drag to finish, then reactivated the mirror.
- Result: the session remained `Live · WIFI`; the iPhone page stayed responsive, and a follow-up menu tap dispatched in 125 ms and changed the visible page state.
- Conclusion: the existing focus-loss release path is physically verified for this paired device. Fresh-action behavior after a focus-loss interruption remains a broader acceptance item.

## 2026-09-20 — App Switcher gesture retest and revert

- Tested the live toolbar action from a clean Home Screen after rebuilding with a gesture starting at `y=62000`, ending at `y=28000`, and using a 400 ms swipe plus 400 ms hold.
- Result: the iPhone remained on Home. A normal mirror drag from the bottom edge also remained on Home.
- Finding: Apple’s App Switcher control is a mirror-level command; the ordinary bottom-edge swipe path is not sufficient on this device. The speculative coordinate change was reverted to the previous implementation.
- Current status: toolbar hit testing and Home routing are proven; App Switcher device state remains unverified pending the CoreDevice command/protocol used by Apple.

## 2026-09-20 — CoreDevice App Switcher protocol inventory

- Live Wi-Fi service inventory for the paired iPhone exposed `com.apple.coredevice.devicecontrol` with only `remote.devicecontrol.orientation`, and HID Indigo with button, scroll, digitizer, and vendor-defined features.
- Probe: sent the standard Consumer HID Menu button (`usagePage 0x0C`, `usageCode 0x40`) through the authenticated Indigo service. The phone remained on Home.
- Conclusion: no public named App Switcher action is exposed by the current CoreDevice service set; Apple’s mirror-level App Switcher command likely uses a private vendor-defined event or a host-side mirroring service unavailable in the pinned library.

## Latency retest — 2026-09-20 follow-up

- Repeated three Wi-Fi shortcut/touch cycles: dispatch timings were 165/151 ms, 339/122 ms, and 153/179 ms for open/home attempts.
- A clean published-app restart showed the mirror video live, but the input status briefly changed to `Input disconnected` and then `Input reconnected`.
- During the disconnected/reconnecting state, visible taps and drags did not change the iPhone screen even though the video remained displayed.
- After recovery, a touch dispatch measured 144 ms, but the selected app did not open; this needs a focused input-session investigation before latency can be considered fully verified.

## Input recovery fix — 2026-09-20

- Root cause: a failed Indigo-only Home command called the full input failure path, disabling the touchscreen HID channel; the first click used for recovery was then discarded.
- Fix: successful touchscreen recovery now delivers the original `down` event, and Indigo Home failures close only Indigo while preserving touchscreen input.
- Validation: rebuilt and relaunched the published Wi-Fi app. Live transitions succeeded for Settings -> General (161 ms dispatch), Home (172 ms), and Home -> Settings -> General (150 ms); all three resulting screens were visible and the session stayed `Live · WIFI`.
- Worker tests: 22/22. .NET tests: 16/16.

## Stream backlog recovery — 2026-09-20

- Root cause: the HEVC pipe queue stopped the session when MPV temporarily fell behind, leaving a stale or black viewer until reconnect.
- Fix: the sink now keeps a bounded queue and drains stale pending frames when full, preserving the newest frame without terminating the display session. The original startup codec-header path remains intact.
- Validation: final published Wi-Fi session started with a real Settings frame and stayed `Live · WIFI` through repeated interactions. Final dispatch measurements were 147 ms, 147 ms, and 148 ms; the screen remained live after the input recovery event.
- The separate Indigo Home/App Switcher behavior remains a protocol limitation; it no longer disables touchscreen recovery.

## Latency retest and frame-buffer correction — 2026-09-20

- Finding: transport dispatch stayed around 109–157 ms, but one direct navigation check was still visually stale at 200 ms and appeared only after another 1.2 seconds. The earlier dispatch-only measurements were therefore insufficient.
- Root cause: the HEVC sink could retain up to 120 queued chunks, allowing old video data to sit ahead of a fresh input result.
- Attempt: a three-chunk queue reduced buffering but could evict the codec header before MPV consumed it, producing a black viewer. That attempt was rejected.
- Final fix: use a 30-chunk bounded queue and preserve the startup header while draining stale live chunks under pressure.
- Validation: the rebuilt live Wi-Fi session started with a real frame; three repeated navigation cycles stayed live; separate back and open checks visibly changed the mirrored iPhone screen after 250 ms. Worker tests pass 23/23.

## Non-blocking input regression coverage — 2026-09-20

- Symptom: input latency could return if a future refactor changed high-frequency commands back to request/response writes.
- Component: .NET worker protocol tests.
- Fix: added a fake-worker test that intentionally gives no response to a touch command, verifies `SendCommandNoWaitAsync` returns within one second, and confirms the next normal command still succeeds. Added malformed-event coverage to ensure protocol errors remain sanitized.
- Validation: .NET tests pass 18/18; Python worker tests pass 27/27; `git diff --check` is clean.

## Session-error viewer cleanup — 2026-09-20

- Symptom: a worker-reported stream timeout or stream termination updated the error text but could leave the embedded MPV child and overlay timer alive until a manual reconnect.
- Component: WPF session lifecycle.
- Fix: state `stopped` and `error` events now clear touch/key state, stop the overlay keep-alive, dispose the embedded MPV session, and leave the viewer ready for reconnect. Worker-exit cleanup now uses the same path.
- Validation: Release solution build passes with 0 warnings/errors; the republished app passes `scripts/verify-publish.ps1 -RequireInstaller`, the rebuilt installer compiles with Inno Setup 6.7.3, and the republished app reaches `Live · WIFI` on the paired iPhone.

## Rebuilt-installer isolated acceptance — 2026-09-20

- Validation: the rebuilt installer installed into an isolated workspace directory, and its CLI returned live machine-readable status with `PATH` restricted to `C:\Windows\System32`, proving the bundled runtime was used. The uninstaller initially left a deferred-delete directory, then its log reported successful completion and the installed directory was removed.
- The first uninstaller invocation was run while another `iPhoneMirror.exe` from the published workspace was active; Inno's application-close/deferred-delete path returned nonzero while cleanup was pending. A clean rerun with no same-name process returned exit code 0, removed the install directory, and confirmed the behavior is tied to the active-process cleanup path rather than a missing-file uninstall failure.
- Added `scripts/verify-installer.ps1`, which enforces the no-active-process precondition and repeats install, bundled CLI status with a restricted PATH, and silent uninstall checks. Its first complete run passed with installer exit 0, CLI status `running:false`, uninstaller exit 0, and the install directory removed.
- Remaining limitation: this is still the current Windows host rather than a clean VM; update-over-older-build and clean-machine behavior remain open.

## Published WPF fake-worker smoke — 2026-09-20

- Component: WPF startup and lifecycle integration.
- Change: added an explicit test-mode runtime override, a deterministic fake worker, and `scripts/verify-wpf-fake-worker.ps1`. The smoke launches the published GUI, waits for fake `video_format`/`running` events, sends a secondary restart through the named pipe, sends stop, and verifies clean GUI exit.
- Validation: `InitialState=running`, `RestartExit=0`, `StopExit=0`, `GuiExit=0`. The normal worker suite remains 27/27, .NET tests 18/18, and publish verification passes.
- Remaining limitation: this is local lifecycle coverage; real network fault injection and full WPF crash/error/reconnect assertions remain pending.

## WPF worker-fault smoke coverage — 2026-09-20

- Extension: the fake worker now supports deterministic `normal`, `error`, and `crash` modes.
- Validation: published GUI runs returned `InitialState=running`, `FaultState=stopped`, `StopExit=0`, and `GuiExit=0` for both the `stream_timeout` event and abrupt worker exit. The normal mode returned `SecondStartProcessCount=1`, `RestartExit=0`, `StopExit=0`, and `GuiExit=0`.
- Remaining limitation: the smoke verifies state/resource cleanup through the real WPF process and named pipe; it does not replace physical network interruption or UI automation coverage.

## Published keyboard-only accessibility smoke — 2026-09-20

- Validation: opened the setup panel in the published app, traversed twelve controls with Tab, and closed the panel again. The viewer remained `Live · WIFI`, the setup controls stayed visible and usable, and no session or input failure occurred.
- Remaining limitation: high-contrast, DPI variants, and an actual screen-reader announcement pass remain pending.

## Native input capture cancellation — 2026-09-20

- Symptom: after a live tap, a lost native `WM_LBUTTONUP` could leave the touchscreen contact active and make later page controls appear unresponsive.
- Fix: the MPV input overlay now tracks capture ownership and emits a cancel release on `WM_CAPTURECHANGED` or `WM_CANCELMODE`; the WPF host clears its active touch and sends `release_input` without waiting for a worker response.
- Validation: rebuilt published Wi-Fi app returned Home from a coverage page, then accepted a second tap that opened Notes with 172 ms dispatch. Release build completed with 0 warnings/errors and the installer was rebuilt successfully.

## Bounded touchscreen HID sends — 2026-09-20

- Symptom: a stale Wi-Fi touchscreen service could leave `send_touchscreen` awaiting indefinitely, making later taps queue behind it even though the viewer still reported `Live · WIFI`.
- Fix: all contact, move, release, scroll, and App Switcher touchscreen reports now use a 750 ms timeout. A timeout follows the existing sanitized input recovery path and reconnects the HID service on the next fresh tap.
- Validation: added a hanging-HID regression test; worker tests pass 28/28. The standard published app and installer were rebuilt. On the live Wi-Fi session, Home restored the real Home screen, Notes opened in 171 ms, and back navigation returned to the Notes list in 168 ms; the previously observed black frame was the phone's actual dark page, not a frozen video pipeline.

## Published WPF lifecycle rerun — 2026-09-20

- Validation after the HID timeout change: normal fake-worker mode returned `InitialState=running`, `SecondStartProcessCount=1`, `RestartExit=0`, `StopExit=0`, and `GuiExit=0`. Error and crash modes both returned `InitialState=running`, `FaultState=stopped`, `StopExit=0`, and `GuiExit=0`.
- The physical Wi-Fi session was restored afterward and returned `running`.

## Final installer verification after HID timeout change — 2026-09-20

- Validation: the rebuilt installer completed isolated installation and bundled-runtime CLI verification with `InstallerExit=0`, `InstalledCliStatus={"running":false,"state":"stopped"}`, `UninstallerExit=0`, and `InstallDirectoryRemoved=true`.
- The physical Wi-Fi session was restored afterward and returned `running`; `git diff --check` remains clean.

## Publish cleanup retry — 2026-09-20

- Symptom: Windows could transiently hold a bundled runtime file after shutdown, causing `publish-windows.ps1` to fail immediately while removing the previous publish directory.
- Fix: standard publish cleanup now retries the bounded recursive removal with increasing short delays and reports the target path if all attempts fail.
- Validation: the standard publish completed after stopping the live app, produced `artifacts\publish\win-x64\iPhoneMirror.exe`, and the live Wi-Fi session was restored afterward.

## Same-worker WPF stream recovery smoke — 2026-09-20

- Symptom: the published WPF lifecycle smoke covered stream-timeout cleanup and worker crashes, but did not prove that a stopped session could reconnect through the existing worker process.
- Component: fake-worker WPF integration coverage.
- Fix: added a deterministic `reconnect` fake-worker mode. Its first `start_session` emits `stream_timeout`; the next `start_session` succeeds. The verifier now sends a second `start`, checks that the GUI returns to `running`, and asserts that only one GUI process exists.
- Validation: all four published modes passed: normal (`InitialState=running`, `SecondStartProcessCount=1`), error (`FaultState=stopped`), crash (`FaultState=stopped`), and reconnect (`FaultState=stopped`, `ReconnectExit=0`, `ReconnectState=running`).
- Remaining limitation: this proves WPF cleanup and same-worker recovery locally; physical network-link interruption and clean-machine acceptance remain pending.

## In-place installer update verification — 2026-09-20

- Gap: the installer verifier previously covered only a fresh install and uninstall, leaving replacement of an existing installation untested.
- Fix: `scripts/verify-installer.ps1` now runs the same silent installer a second time against the already-installed directory before checking the bundled CLI and uninstaller.
- Validation: `InstallerExit=0`, `UpdateInstallerExit=0`, installed CLI returned `{"running":false,"state":"stopped"}` with PATH restricted to `C:\Windows\System32`, `UninstallerExit=0`, and `InstallDirectoryRemoved=true`.
- Remaining limitation: the update used the same build on the current Windows host; a clean VM and upgrade from an older release remain pending.

## Published missing-stack error smoke — 2026-09-20

- Gap: Apple device-stack mapping existed in the worker, but the published WPF error surface had only been exercised indirectly.
- Fix: added a `missing-stack` fake-worker mode that emits `apple_device_support_missing`; the WPF instance status now retains the sanitized error code for lifecycle diagnostics and the verifier asserts it.
- Validation: published fake-worker mode returned `InitialState=running`, `FaultState=stopped`, `FaultErrorCode=apple_device_support_missing`, `StopExit=0`, and `GuiExit=0`. Normal, error, crash, and same-worker reconnect modes also passed after isolating a transient restart run.
- Remaining limitation: this is deterministic local coverage; physical absence of Apple Devices and clean-machine installer behavior remain pending.

## Final installer verification after missing-stack WPF change — 2026-09-20

- Validation: the rebuilt installer passed isolated first install and same-directory update with `InstallerExit=0` and `UpdateInstallerExit=0`; its bundled CLI returned `{"running":false,"state":"stopped"}` under restricted PATH; silent uninstall returned `UninstallerExit=0` and removed the installation directory.
- The live Wi-Fi iPhone session was restored afterward and returned `running` with no retained error code.

## Device selector setup guidance — 2026-09-20

- Symptom: a trusted iPhone with Developer Mode disabled was displayed only as `Trusted`, which did not tell the user why mirroring could still fail.
- Fix: `DeviceInfo.DisplayDetails` now distinguishes `Trust required`, `Developer Mode required`, `Wi-Fi paired`, and `Trusted`, while preserving actionable state errors such as `iphone_locked`.
- Validation: .NET Core tests pass 23/23, including the new device-state display cases; the release publish and installer were rebuilt, and the live Wi-Fi session returned `running` after restoration.

## Multi-device selection safety — 2026-09-20

- Symptom: when no saved serial was available, device refresh selected the first detected iPhone automatically, which could connect to the wrong phone in a multi-device setup.
- Fix: the WPF device selector now auto-selects only a saved matching serial or a single detected phone. With multiple unselected phones it leaves the selector empty and tells the user to choose one.
- Remaining limitation: physical two-device acceptance still requires two compatible iPhones; the worker already rejects ambiguous connection requests when no serial is supplied.

## Repeated live latency retest — 2026-09-20 13:18 +03:00

- Validation: against the published Wi-Fi session at `a direct Wi-Fi endpoint`, the first fresh click trial measured 9.07 s through the native GUI probe and visibly returned to the Notes list; this is treated as a probe/automation outlier because the next trials were normal.
- Four open/back cycles measured open `172, 1024, 154, 168 ms` and back `147, 147, 146, 152 ms`.
- Eight additional cycles measured opens `162, 130, 142, 145, 139, 147, 155, 170 ms` and backs `144, 151, 484, 148, 142, 131, 158, 243 ms`.
- Result: steady state is usually 130–170 ms, but a real ~1.0 s outlier and two moderate outliers remain. Every sampled navigation completed and the WPF session stayed `Live · WIFI`; latency is not yet consistently accepted.
- Limitation: the native screenshot API adds roughly 3 seconds and was excluded from dispatch timings. The next investigation should instrument the WPF-to-worker input path and frame presentation separately.

## Latency investigation rollback and restored-stream retest — 2026-09-20 13:37 +03:00

- Investigation: a bounded HEVC access-unit queue was tested to reduce possible stale-frame delay, but the published live stream remained black after startup. The experiment was reverted completely to the known-good 30-item queue and its existing startup behavior.
- Validation after republish: Wi-Fi video returned to a live Notes frame and the session stayed connected.
- Eight fresh open/back cycles measured open `369, 145, 168, 150, 158, 167, 178, 145 ms` and back `159, 115, 144, 124, 145, 138, 149, 136 ms`. Only the first open was a warm-up outlier; no 1-second action occurred in this run.
- Result: no unverified queue optimization was shipped. Current latency is usually below 180 ms after warm-up, but the earlier 1,024 ms outlier means the issue remains open for a safer frame-boundary investigation.

## Bounded stale-HID cleanup — 2026-09-20 13:48 +03:00

- Symptom: the touchscreen report timeout was bounded, but failure cleanup could still wait one additional second while sending the stale contact release, preserving the long-tail delay.
- Fix: touchscreen reports and stale-contact/keyboard cleanup now share a 350 ms timeout instead of the previous 750 ms report and 1 second cleanup waits.
- Validation: the hanging-HID regression completes under 850 ms and worker tests pass 28/28. The rebuilt published Wi-Fi app showed a live Notes frame; eight open/back cycles measured one 386 ms warm-up open followed by 116–159 ms actions, with no 1-second delay. The rebuilt installer passed first install, in-place update, bundled-runtime CLI status, silent uninstall, and install-directory removal.
- Limitation: the HEVC queue remains at the known-good 30-item startup-safe configuration; a smaller queue caused a black stream and was reverted.

## Restart guard and repeated live retest — 2026-09-20

- Fix: fast session replacement now ignores `Exited` events from a disposed MPV instance, so an old player cannot tear down the new live session. The fake worker also keeps its per-session video pipe open during WPF restart smoke tests.
- Validation: all eight fake-worker modes pass, including normal restart, stream error, crash, reconnect, missing Apple support, locked iPhone, USB trust, and Developer Mode guidance. Worker tests pass 28/28, .NET tests pass 23/23, and the current installer passes install, update, CLI status, uninstall, and directory removal.
- Physical retest: the restored Wi-Fi session at `a direct Wi-Fi endpoint` stayed connected with a live Notes frame. Four additional automated open/back attempts completed, but the native GUI dispatch probe measured 354–640 ms opens and 369–402 ms backs; those measurements include the probe's input scheduling and were less reliable than the earlier direct timing run. The phone was left on the live Notes list.

## Opt-in HID timing diagnostic — 2026-09-20

- Fix: the worker now supports `IPHONE_MIRROR_TIMING_LOG`, recording only command, phase, timestamp, and duration. The diagnostic is disabled unless the environment variable is set and file failures are ignored so it cannot affect normal input.
- Validation: five additional physical Wi-Fi open/back cycles completed on the live published app. The worker recorded 10 down reports at 0.196–0.311 ms (0.226 ms average), 11 move reports at 0.195–0.256 ms (0.221 ms average), and 10 up reports at 0.171–0.269 ms (0.215 ms average). The mirror remained `Live · WIFI` and returned to the Notes list.
- Interpretation: the earlier 350–640 ms native probe measurements are not HID processing time; the worker-side HID path was sub-millisecond in this run. Visible phone reaction and probe scheduling still need separate frame-level instrumentation before latency can be called fully resolved.

## Native toolbar icon rendering — 2026-09-20

- Symptom: the top-edge Home and App Switcher controls depended on fallback Unicode glyphs and could appear as broken or unrelated characters.
- Fix: the viewer now draws both symbols directly with native GDI geometry: a house outline for Home and a four-panel grid for App Switcher. The toolbar position, hover behavior, routing, and owner-window tracking are unchanged.
- Validation: the rebuilt published app rendered the toolbar over the live Wi-Fi Quick Notes frame, stayed connected, and the rebuilt installer passed install, update, CLI status, uninstall, and directory-removal verification. .NET tests remain 23/23.

## Video queue timing diagnostic — 2026-09-20

- Fix: the same opt-in timing log now records video frames entering and leaving the HEVC named-pipe sink, including queue depth and pipe-write duration. It remains disabled by default and all diagnostic file errors are ignored.
- Validation: five more live Wi-Fi open/back cycles completed. The worker-to-MPV sink stayed at queue depth 0–1 and each recorded pipe write completed in about 0.006–0.015 ms, with no accumulating backlog. Worker tests pass 28/28.
- Interpretation: the current worker video sink is not building a visible multi-frame queue during the tested interactions. The unresolved portion is MPV decode/presentation or the external observation path, so the 30-item startup-safe queue remains unchanged.

## 2026-09-20 — Bounded automatic reconnect after transient stream failure

- Change: transient `stream_timeout`, `stream_ended`, `player_disconnected`, and connection failures now trigger up to three delayed WPF reconnect attempts while preserving the selected transport and device.
- Validation: the published `network-retry` fake-worker smoke injected a first-attempt `stream_timeout`, observed the app return to `running` with exactly one GUI process, and then stopped cleanly. Release build completed with 0 warnings/errors; worker tests remain 28/28.
- Limitation: physical network-link interruption still requires a controlled adapter or link-change test; the retry path is bounded and does not retry setup-state errors.

## 2026-09-20 — MPV relay shutdown race and Windows PowerShell smoke compatibility

- Symptom: manual reconnect could reach `running` but terminate the WPF process during shutdown with a CLR `NullReferenceException`; the fake-worker verifier also required PowerShell 7 because it used newer `ProcessStartInfo` and `Process.Kill` overloads.
- Fix: capture stable MPV pipe/process references before awaiting, ignore expected pipe-disposal exceptions during relay cleanup, use the legacy `Arguments` property for the verifier, and terminate test process trees with Windows `taskkill`.
- Validation: release build passed with 0 warnings/errors. The complete nine-mode WPF matrix passed under Windows PowerShell 5, including manual reconnect and automatic network retry, with every GUI exit code equal to 0.

## 2026-09-20 — Installer verifier PowerShell 5 compatibility

- Fix: replaced the remaining `Process.Kill(bool)` call in the isolated installer verifier with Windows `taskkill` tree cleanup, so the documented verifier works with the default Windows PowerShell host.
- Validation: first install, in-place update, restricted-PATH bundled CLI status, silent uninstall, and install-directory removal all passed under Windows PowerShell 5.

## 2026-09-22 — Detect HEVC pipe stall and auto-reconnect

- Symptom: preview froze while inputs still worked; CLI reported `running` and mpv/worker sat at ~0% CPU; the phone status-bar clock stopped advancing for minutes. `stream_timeout` never fired because `transport.last_packet` kept refreshing.
- Fix: `HevcPipeSink` now tracks `last_write`/`write_started` and exposes `is_stalled()`. The session loop stops with transient `video_stall` when a pipe write is blocked >4s, frames are pending with no completed write for >4s, or packets arrive but nothing reaches the pipe for >8s. `video_stall` is listed in `ErrorCatalog` and treated as a transient reconnect code.
- Validation: Release build 0/0; .NET tests 24/24; worker tests 33/33 (new stall cases); installer rebuilt and silent reinstall exit 0.
