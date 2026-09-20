# Apple iPhone Mirroring UI Notes

Observed on the connected Mac using Apple's iPhone Mirroring app on 2026-09-20.

## Default viewer

- The viewer is a small floating portrait window with the iPhone centered inside it.
- The phone has rounded physical-device corners, a visible Dynamic Island, live status-bar time/icons, and the real iPhone wallpaper/app state.
- The surrounding Mac desktop is visible around the phone; there is no permanent bulky toolbar over the mirrored screen.
- The default visual treatment is dark and quiet: black/dark neutral space around the device, subtle shadow, and minimal chrome.
- The mirrored image is live and pointer input is sent directly to iOS. A click on the Settings icon opened the real Settings app.
- The phone can show full iOS UI states including Home Screen, Settings, Spotlight, and App Switcher.

## Top-edge hover behavior

- Moving the pointer to the top edge reveals the native Mac window chrome temporarily.
- The revealed chrome has standard macOS traffic-light window buttons on the left.
- Two compact action buttons appear on the right:
  - Home Screen: grid-like icon; returns the mirrored iPhone to the Home Screen.
  - App Switcher: phone/window-like icon; opens the iOS App Switcher.
- The toolbar is overlaid at the top of the portrait window and disappears when the pointer leaves the edge area.
- This is the interaction to reproduce on Windows: keep the viewer clean by default, then reveal a compact overlay when the pointer enters a small top-edge hit zone.

## Application menu behavior

The Mac app's View menu exposes these commands:

- Home Screen (`Command+1`)
- App Switcher (`Command+2`)
- Spotlight (`Command+3`)
- Control Center (`Command+4`)
- iPhone Size (`Command+9`, unavailable in the observed state)
- Actual Size (`Command+0`, unavailable in the observed state)
- Zoom In (`Command++`)
- Zoom Out (`Command+-`)

The Edit menu is standard Mac editing behavior: Undo, Redo, Cut, Copy, Paste, Delete, Select All, Start Dictation, and Emoji & Symbols. This suggests clipboard and keyboard handling are integrated into the normal application input path rather than exposed as a separate permanent control bar.

Apple's current support documentation independently confirms the same host-level mapping: Command+1 opens Home, Command+2 opens App Switcher, Command+3 opens Spotlight, and Command+4 opens Control Center on supported macOS versions. It documents these as iPhone Mirroring toolbar/keyboard actions, not as ordinary iPhone touch gestures. See [iPhone Mirroring: Use your iPhone from your Mac](https://support.apple.com/en-au/120421).

## Windows translation target

- Use a dark viewer surface with the phone aspect ratio preserved and the device centered.
- Keep the normal state free of permanent controls.
- Add a small transparent top-edge hit zone over the viewer.
- On pointer entry, fade in a compact top overlay containing Home and App Switcher, plus connection/device status only when useful.
- Hide the overlay after pointer exit or a short idle delay, while keeping it visible while the pointer is over the controls.
- Match the Mac behavior by routing Home and App Switcher through the existing worker input commands, not by simulating clicks at fixed phone coordinates.
- Preserve the live iPhone status bar and rounded device silhouette inside the video surface.
