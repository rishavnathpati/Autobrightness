# AutoBrightness for Windows 2.3

A C# / .NET 10 WPF tray app. One webcam drives the laptop panel and supported external monitors, with a separate brightness preference for each screen.

## Everyday use

Extract the Windows x64 ZIP and open **AutoBrightness.exe**. Python, administrator rights and a separate .NET installation are not needed.

1. Leave the webcam uncovered in your normal room lighting.
2. Set each screen's slider to a comfortable level. Equal percentages do not mean equal physical brightness.
3. Click **Turn on automatic**. The app remembers those levels for this lighting and adjusts gently as the camera reading changes.
4. If a screen feels too bright or dim, move its slider. Its preference is updated immediately without stopping the other screen.
5. Close the window to keep running in the tray. Launch the EXE again or double-click the tray icon to reopen it. **Pause automatic** releases the camera and preserves screen brightness. **Quit** exits.

**Use these levels for this room** remembers the current brightness of both screens against a fresh camera reading. Use it after repositioning the camera or changing your lighting arrangement. This is a single reference per screen, not a learned multi-room history.

The first run starts paused. Later launches remember whether automatic mode was enabled. Windows sign-in startup is not installed automatically; the app must be launched. It is designed to resume after sleep, session unlock and display reconnection when automatic was enabled, but those lifecycle paths need longer hardware evaluation.

## Smoothness and exposure

- Automatic exposure is disabled and verified. The default is now 31.25 ms, accepted by this ACER webcam: four times its previous 7.81 ms exposure, or +2 stops. This increases captured light while keeping exposure fixed. A literal UVC value of +2 means four seconds, so the settings field uses milliseconds instead. Higher exposure cannot reveal a scene with no available light and reduces headroom in bright rooms.
- The image is split into 25 regions. Each region uses a trimmed average; their median represents the scene. This reduces sensitivity to localized lamps, shadows and foreground objects affecting fewer than half the regions. It does not identify faces or recover actual room illuminance.
- Three-sample median filtering rejects brief outliers. A small deadband ignores camera noise. A fast filter and frequent small brightness updates target a complete visible transition in roughly 1–3 seconds; brightening is slightly faster than dimming. The controller updates every 100 ms, with a bounded ramp and no catch-up jump after a stall. Actual display granularity and command latency can differ.
- A relative logarithmic curve shifts each display from its preferred level as camera light changes. Defaults allow 10–100% in automatic mode. The sliders allow the full 0–100% hardware range.
- Delayed monitor readback is compared with recent commands before being treated as a manual adjustment. An unfamiliar native brightness change needs two consistent readings before being learned; writes to that screen pause during confirmation. Very small changes or values matching recent commands may not be recognized immediately.
- Camera and display failures are reported. A failed monitor retries after ten seconds while the other continues; missing camera frames or a lost exposure lock stop automatic changes.

This approximates the desired quiet background behavior, but a webcam is not an ambient-light sensor. Camera readings are gamma-encoded image brightness, not lux. Faces, clothing, screen reflections, the background, automatic gain and other camera processing can still influence them. Locking exposure fixes one major source of drift; it cannot make a webcam equivalent to the sensors in a phone or Mac. No physical luminance matching in nits is performed.

## Settings and tests

Expand **Settings and test tools** to select a camera, change fixed exposure, choose participating displays, or set each display's automatic limits. Pause before changing these settings. Changing camera or exposure resets the room references. Dark or saturated frames do not shut down the camera. Darkness displays a neutral **Dark scene · Automatic is active** status. A mostly clipped image suggests lowering fixed exposure. If the camera was covered when you started, uncover it and use the room-reference button at comfortable brightness levels.

Upgrading from 2.2 resets the room references once because the spatial meter changes the measurement. Actual screen brightness at startup becomes the new reference, retaining display limits and participation. The old default of 10 requested milliseconds upgrades to 31.25 ms; custom exposures are preserved. Future launches retain the new references.

**Test both screens** briefly changes each supported display by five points, reads the actual level, and restores its original level. It reports PASS or FAIL with the numbers. This test makes visible brightness changes.

**Record a test log (numbers only)**, selected before starting automatic, writes timestamped camera readings, filtered light, dark/clipped pixel fractions, commands, hardware readback and learned preferences to a local JSONL file. A run is capped at approximately 2 MB. Hardware observations may lag commands. The exposure flag indicates operation in verified fixed-exposure mode; the lock is rechecked approximately every two seconds.

Settings and errors are stored in `%LOCALAPPDATA%\AutoBrightness`. Numeric test logs are opt-in and are stored there too. Error logs rotate at 1 MB. No webcam images or audio are saved or uploaded. Frames stay in memory, and there is no application network client, analytics or updater.

## Hardware support

- Internal panels use WMI brightness methods where supported by their display driver.
- External displays use DDC/CI through the Windows physical monitor API, with VCP brightness code `0x10` as a fallback. Enable DDC/CI in the monitor menu if needed.
- HDMI and USB-C display connections can carry supported monitor commands. USB-C alone does not guarantee DDC support; docks, KVMs, adapters, drivers and some HDR/picture modes can block it. Only the currently connected path was tested here.
- Devices are controlled sequentially on a background worker, with brightness updates up to ten times per second and hardware reads about once per second. Both follow the same light reading, with independently chosen levels. Unsupported screens remain visible with a reason.
- Moving a monitor to another port can change its device identifier. Review its preferences after reconnecting. Mirrored and MST configurations need additional testing.
- The camera is held with exclusive control to lock exposure. Pause before video calls. Its indicator stays on while automatic mode is running. Image measurement is 160×120 at most five times per second, but the camera may stream faster. Battery impact has not been measured.
- The previous camera exposure value and auto-exposure mode are restored on pause/quit where the driver accepts them. Restoration failures are logged.

## Build and automated tests

Install the .NET 10 SDK on Windows. From the repository root:

```powershell
dotnet build windows/AutoBrightness.slnx -c Release
dotnet run --project windows/AutoBrightness.Tests -c Release
dotnet run --project windows/AutoBrightness.App -c Release
```

Publish the standalone package:

```powershell
dotnet publish windows/AutoBrightness.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o dist/AutoBrightness
```

The dependency-free test executable exits nonzero on failure. The current source has 36 deterministic tests covering spatial and pixel metering, localized scene changes, darkness, clipping, settings migration and compatibility, noise rejection, allocation-free filtering, bounded transitions, independent preferences, full-range mapping, manual learning, delayed readback, disconnection/recovery, disabled displays and hardware quantization. They use simulated hardware. The published v2.3.0 release had 33 tests; later source cleanup is documented separately in the main README. GitHub Actions builds and tests all changes. A push to master with a commit subject starting with `release: v` publishes the project's version after checks pass; other changes only upload a build artifact. Existing release tags are never overwritten.

- `AutoBrightness.Core`: image light measurement, filtering, curves, transitions and display coordination.
- `AutoBrightness.App`: WPF UI, WinRT camera capture, WMI/DDC backends, settings and tray integration.
- `AutoBrightness.Tests`: deterministic regression tests.

## Developer diagnostics

These explicit commands exit after completion. Wait for the process before reading the output file. Run write/camera diagnostics while the normal app is paused.

```powershell
# Read-only monitor inventory; no camera capture or brightness writes.
.\AutoBrightness.exe --diagnostics display-report.json
# Opens the first camera, saves numeric samples only, restores exposure and releases it.
.\AutoBrightness.exe --probe-camera camera-report.json
# Optional final argument overrides fixed exposure in milliseconds; default is 31.25.
.\AutoBrightness.exe --probe-camera camera-report.json 31.25
# Changes each supported display by one point, reads it back, and restores it.
.\AutoBrightness.exe --verify-displays display-check.json
# Replays a 15-second dark/light sequence against real monitors, then restores both.
# Synthetic light input; this does not exercise webcam capture. Keep the normal app paused.
.\AutoBrightness.exe --verify-transitions transition-check.json
# Renders the app's own view offscreen and exits; does not exercise real UI input.
.\AutoBrightness.exe --ui-smoke settings.png
```

Real EXE interaction and hardware results from September 25, 2026 are documented in the accompanying test report. These results do not establish battery life, long-term suspend/hotplug reliability, physical luminance equivalence or compatibility with every cable/dock.

## API references

- [Windows camera frame reader](https://learn.microsoft.com/en-us/windows/apps/develop/camera/process-media-frames-with-mediaframereader)
- [UVC exposure control](https://learn.microsoft.com/en-us/uwp/api/windows.media.devices.videodevicecontroller.exposure)
- [WMI brightness methods](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightnessmethods)
- [Physical monitor brightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness)
