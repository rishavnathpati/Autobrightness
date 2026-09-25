# AutoBrightness 2.3 — fixed exposure and spatial metering

September 25, 2026. Built and exercised on the user's Windows laptop, ACER HD User Facing webcam and MSI MAG 27CQ6F external monitor.

## Changes

- Default manual exposure is 31.25 ms, verified by the webcam driver. This is four times the old actual 7.8125 ms (+2 stops). Auto exposure stays off and the lock is rechecked while running. Windows UVC exposure uses log2(seconds); a literal +2 would mean four seconds. [Microsoft documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/ksproperty-cameracontrol-exposure).
- The scene is divided into 25 regions. Each gets a trimmed luma average; the median of the regions feeds the existing temporal median, deadband, logarithmic preference curve and smooth ramp. Localized scene changes affecting fewer than half the regions have less influence. Broad illumination changes still drive both screens with independent preferences.
- Dark frames remain valid. The main view uses the neutral status “Dark scene · Automatic is active.” Missing frames and lost exposure control remain actual failures.
- Numeric diagnostics include dark and clipped pixel fractions and advancing frame timestamps. No images or audio are saved.
- Settings migration discards the old metering reference once, retains limits and participation, and learns actual screen levels against the new meter. Existing custom exposure choices are retained; the previous default upgrades to 31.25 ms.

## Verification performed

- Release build and standalone Windows x64 publish: successful, zero compiler warnings/errors.
- Automated regression suite: **33/33 passed**. New cases include all 256 uniform brightness levels, uneven image dimensions, localized bright/dark objects covering 40% of a frame, room-wide changes through foreground clutter, near-black noise, clipping statistics, invalid image sizes and settings migration. Existing independent-display, delayed-readback and 1–3 second ramp tests pass.
- Real webcam probe: ten fresh samples at verified **31.25 ms**, no capture errors, advancing timestamps. The spatial readings were **1/255**, with all pixels at or below 3/255 and none clipped bright. A preceding whole-frame probe at the same exposure measured roughly 0.79–0.90/255.
- Actual EXE/UI: launched the new build, opened Settings, verified 31.25 ms, and invoked **Test both screens**. Laptop **61 → 66 → 61%: PASS**. MSI **40 → 45 → 40%: PASS**. These are native brightness readbacks, not measurements of physical light output.
- Started automatic mode through the UI. It stayed active with near-black frames and showed **Camera is ON**, fixed exposure, and the neutral dark-scene status. Startup retained actual brightness at **61% laptop / 40% external**, with new reference 1/255. The persisted settings are version 3, automatic enabled, exposure 31.25 ms.

## Limits of this verification

During later README screenshot preparation, the running app's reading changed from 1/255 to approximately 118.5/255 at the same locked 31.25 ms exposure, without a camera restart. Both displays reached 100% against their then-current room preferences. User-adjusted dark-scene levels of 48% laptop and 34% external are visible in the main screenshot; the Settings screenshot also retains the earlier 61/40% hardware test results. No display preferences were edited for the screenshots.

Physical room lighting was not controlled or confirmed. These observations do **not** establish improved real low-light sensitivity or timing for a complete lights-off/lights-on optical response. Fresh timestamps prove capture activity, not that the scene measurement is correct. Higher exposure cannot create light where none reaches the sensor. If the room is visibly lit, a near-black signal needs further camera/driver investigation.

The 1–3 second response remains verified by deterministic controller tests. Earlier 2.2 hardware timing tests used synthetic light with real monitors; they are not a new optical timing result for 2.3. The spatial meter's resistance to local objects is tested on constructed images and still needs varied real scenes. Large foreground changes, screen reflections and camera gain/processing can affect the measurement. This is camera image brightness, not calibrated lux or matching monitor luminance in nits.

Long-duration operation, battery impact, suspend/hotplug reliability and every cable/dock combination remain unverified.
