# Implementation cleanup after v2.3.0

September 26, 2026. These changes apply to the current source, after the published v2.3.0 release.

## Removed

- The Python entry point, seven `src/` files, Python requirements and root configuration, legacy setup guide and two old screenshots: 13 obsolete tracked files in total. They remain in Git history.
- Python-specific ignore rules, replaced with a short .NET/IDE/local-data list. Text line endings and .NET 10 SDK selection are now explicit.
- The unused shared-camera mode, whole-frame meter, legacy absolute calibration curve and coordinator mode that paused all displays on a manual adjustment. The production app already used exclusive capture, spatial metering, a room-relative curve and independent manual learning.
- Retired settings fields are no longer written. Existing JSON files containing those fields still load; exposure, room references, limits and enabled displays are preserved. Older settings migrations remain available.

The original UVC exposure interface is deliberately retained: the tested ACER webcam needs it. DDC/CI fallbacks, display recovery, camera cleanup and release history are also retained.

## Runtime changes

- Reuse the managed pixel array and WinRT copy buffer across frames; resize only if frame dimensions change, and clear/release storage on stop. This avoids repeatedly allocating a 76,800-byte managed pixel array at 160×120 BGRA resolution (about 384 kB/s at five measured frames per second), as well as repeatedly creating the WinRT buffer.
- Gate callbacks during camera shutdown before clearing reusable buffers.
- Replace a queue/LINQ/sort-array median with a three-element ring and stack storage. Filter output, noise thresholds and transition timing are preserved.
- Remove unused calibration validation and exponentiation from each controller tick. An uninitialized room reference now seeds from actual display brightness instead of invoking a fallback curve.
- Send display updates to the UI only when the snapshot changes. Property notifications fire only for changed values, and a camera frame is formatted once per UI observation.
- Track diagnostic-log size in memory instead of querying file metadata every tick. Logging stops at the existing 2 MB limit.

## Measurements and checks

A local Release-build microbenchmark used identical deterministic input before and after the cleanup, with 20,000 warmup calls before each measured loop:

| Measurement | Before | After |
| --- | ---: | ---: |
| 100,000 ambient-filter updates: managed allocations | 38,400,000 bytes | 0 bytes |
| 100,000 ambient-filter updates: elapsed time | 110.06 ms | 23.40 ms |
| Filter output checksum | 5125697.422632367 | 5125697.422632367 |
| Spatial-meter output checksum | 3174512.9870133945 | 3174512.9870133945 |

This is a small local benchmark, not a claim about total app CPU use or battery life. Timings vary with machine load and JIT state. A regression test also verifies zero allocations across successive filter updates.

- Release build: zero compiler warnings/errors; **36/36 deterministic tests passed**. Tests cover the retained production behavior, invalid input before hardware access, saved-setting compatibility, first-run reference capture, golden filter output and allocations.
- Real ACER camera: ten fresh readings around **45.35–46.13/255**, with exposure verified at **31.25 ms**, no capture errors and advancing timestamps. This exercises reused buffers across frames; no images were recorded.
- Real monitor transition test with synthetic lighting: **PASS**, no errors. Both screens completed controller transitions within three seconds and returned to their original **100% laptop / 93% external** settings. This is not an optical lights-off/lights-on timing measurement.
- The saved user settings file was unchanged by the hardware diagnostics.
- WPF UI smoke render: both display cards and paused camera state rendered correctly. The self-contained Windows x64 publish completed successfully.

The previously documented limits of webcam-based metering, DDC/CI compatibility and unmeasured battery/long-term lifecycle behavior still apply.
