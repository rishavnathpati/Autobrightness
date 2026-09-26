using AutoBrightness.Core;

var tests = new (string Name, Action Test)[]
{
    ("An uncalibrated display learns its actual level without a startup write", () =>
    {
        using var screen = new FakeDisplay("new", 42);
        var coordinator = new BrightnessCoordinator([new(screen, new())]);
        var first = coordinator.Tick(30, TimeSpan.Zero);
        Require(first.Displays[0].LearnedProfile == new DisplayProfile(true, 10, 100, 42, 30));
        for (var i = 1; i < 20; i++) coordinator.Tick(30, TimeSpan.FromSeconds(i * .1));
        Near(42, screen.Level); Require(screen.Writes == 0);
    }),
    ("Invalid camera readings and display limits cannot reach hardware", () =>
    {
        using var screen = new FakeDisplay("invalid", 50);
        var coordinator = Make(screen);
        foreach (var light in new[] { -1d, 256, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws(() => coordinator.Tick(light, TimeSpan.Zero));
        Require(screen.Reads == 0 && screen.Writes == 0);
        Throws(() => new DisplayProfile(true, 80, 20).Validate());
    }),
    ("BGRA red and blue receive the correct weights", () =>
    {
        foreach (var (b, g, r, expected) in new[] { (0, 0, 255, 77), (255, 0, 0, 29), (255, 255, 255, 255) })
        {
            var pixels = new byte[5 * 5 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            { pixels[i] = (byte)b; pixels[i + 1] = (byte)g; pixels[i + 2] = (byte)r; pixels[i + 3] = 255; }
            Near(expected, LightMeter.MeasureSceneBgra(pixels, 5, 5).Level);
        }
    }),
    ("Small bright/dark objects do not dominate the camera reading", () =>
    {
        var image = SceneImage((x, y) =>
        {
            var position = y % 24 * 32 + x % 32;
            return position < 70 ? (byte)0 : position >= 698 ? (byte)255 : (byte)100;
        });
        Near(100, LightMeter.MeasureSceneBgra(image, 160, 120).Level);
    }),
    ("Ramping is limited even after a long stall", () =>
    {
        var ramp = new BrightnessRamp(50);
        Require(ramp.Advance(100, 0.1) <= 57);
        Require(ramp.Advance(100, 3600) <= 71);
        var fast = new BrightnessRamp(50); var slow = new BrightnessRamp(50);
        for (var i = 0; i < 2; i++) fast.Advance(60, 0.1);
        slow.Advance(60, 0.2);
        Near(fast.Value, slow.Value, 0.001);
    }),
    ("Spatial meter preserves every uniform level including black and white", () =>
    {
        for (var level = 0; level <= 255; level++)
            Near(level, LightMeter.MeasureSceneBgra(SceneImage((_, _) => (byte)level, 13, 17), 13, 17).Level);
    }),
    ("Bright or dark objects across two fifths of the frame cannot dominate the room", () =>
    {
        foreach (var patch in new byte[] { 0, 255 })
        {
            var frame = SceneImage((x, _) => x < 64 ? patch : (byte)80);
            Near(80, LightMeter.MeasureSceneBgra(frame, 160, 120).Level);
        }
    }),
    ("Spatial meter follows room-wide changes through foreground clutter within three seconds", () =>
    {
        using var screen = new FakeDisplay("scene", 70);
        var profile = new DisplayProfile(true, 10, 100, 70, 80);
        var coordinator = new BrightnessCoordinator([new(screen, profile)]);
        foreach (var light in new byte[] { 80, 0, 80 })
        {
            // A bright foreground object covers 40% while the rest of the room changes.
            var frame = SceneImage((x, _) => x < 64 ? (byte)255 : light);
            var measured = LightMeter.MeasureSceneBgra(frame, 160, 120).Level;
            Near(light, measured);
            var start = light == 0 ? 3 : screen.Writes == 0 ? 0 : 6;
            for (var tick = 0; tick < 30; tick++)
            {
                var before = screen.Level;
                coordinator.Tick(measured, TimeSpan.FromSeconds(start + tick * .1), tick % 2 == 0);
                Require(Math.Abs(screen.Level - before) <= 7);
            }
            Near(profile.Target(light), screen.Level, 1);
        }
    }),
    ("Dark and clipped pixel fractions include uneven edge regions", () =>
    {
        var black = LightMeter.MeasureSceneBgra(SceneImage((_, _) => 0, 13, 17), 13, 17);
        Near(0, black.Level); Near(1, black.DarkPixelFraction); Near(0, black.ClippedPixelFraction);
        var white = LightMeter.MeasureSceneBgra(SceneImage((_, _) => 255, 13, 17), 13, 17);
        Near(255, white.Level); Near(0, white.DarkPixelFraction); Near(1, white.ClippedPixelFraction);
        var mixed = LightMeter.MeasureSceneBgra(SceneImage((x, _) => x < 3 ? (byte)0 : x >= 10 ? (byte)255 : (byte)80, 13, 17), 13, 17);
        Near(3d / 13, mixed.DarkPixelFraction); Near(3d / 13, mixed.ClippedPixelFraction);
    }),
    ("Near-black sensor noise stays finite and cannot cause a large brightness swing", () =>
    {
        using var screen = new FakeDisplay("dark", 30);
        var coordinator = new BrightnessCoordinator([new(screen, new(true, 10, 100, 30, 1))]);
        for (var tick = 0; tick < 100; tick++)
        {
            var frame = SceneImage((x, y) => (byte)((x + y + tick) % 3));
            var measured = LightMeter.MeasureSceneBgra(frame, 160, 120).Level;
            Require(double.IsFinite(measured) && measured is >= 0 and <= 2);
            coordinator.Tick(measured, TimeSpan.FromSeconds(tick * .2));
            Require(Math.Abs(screen.Level - 30) <= 4);
        }
        Require(screen.Writes == 0);
    }),
    ("Malformed spatial frames are rejected", () =>
    {
        Throws(() => LightMeter.MeasureSceneBgra([], 0, 0));
        Throws(() => LightMeter.MeasureSceneBgra(new byte[100], 4, 6));
        Throws(() => LightMeter.MeasureSceneBgra(new byte[101], 5, 5));
        Throws(() => LightMeter.MeasureSceneBgra(new byte[100], int.MaxValue, int.MaxValue));
    }),
    ("Old settings upgrade exposure and invalidate only the obsolete room reference", () =>
    {
        var settings = new AutoBrightness.App.Settings { Version = 2, AutomaticEnabled = true, ExposureMilliseconds = 10,
            Displays = new() { ["laptop"] = new(true, 12, 95, 77, 8.02), ["external"] = new(false, 20, 100, 56, 8.02) } };
        settings.Upgrade(); settings.Validate();
        Require(settings.Version == 3 && settings.AutomaticEnabled); Near(31.25, settings.ExposureMilliseconds);
        Require(settings.Displays["laptop"] == new DisplayProfile(true, 12, 95, 77, null));
        Require(settings.Displays["external"] == new DisplayProfile(false, 20, 100, 56, null));
    }),
    ("Upgrade preserves custom exposure and never discards current meter references", () =>
    {
        var settings = new AutoBrightness.App.Settings { Version = 2, ExposureMilliseconds = 125 };
        settings.Upgrade(); Near(125, settings.ExposureMilliseconds);
        settings.Displays["screen"] = new(true, 10, 100, 50, 30);
        settings.Upgrade(); settings.Validate(); Near(30, settings.Displays["screen"].ReferenceLight!.Value);
        settings.Version = 999; Throws(settings.Validate);
    }),
    ("One light source drives both displays with independent ranges", () =>
    {
        using var laptop = new FakeDisplay("laptop", 50); using var external = new FakeDisplay("external", 50);
        var coordinator = new BrightnessCoordinator([
            new(laptop, new DisplayProfile(true, 20, 80, 50, 40)), new(external, new DisplayProfile(true, 10, 60, 50, 40))]);
        for (var i = 0; i <= 120; i++) coordinator.Tick(200, TimeSpan.FromSeconds(i));
        Near(80, laptop.Level, 2); Near(60, external.Level, 2);
        Require(laptop.Writes > 0 && external.Writes > 0);
    }),
    ("Stable lighting produces no redundant brightness writes", () =>
    {
        using var display = new FakeDisplay("stable", 50);
        var coordinator = Make(display);
        for (var i = 0; i < 20; i++) coordinator.Tick(100, TimeSpan.FromSeconds(i));
        Require(display.Writes == 0);
    }),
    ("One unfamiliar readback does not overwrite the display preference", () =>
    {
        using var a = new FakeDisplay("a", 50); using var b = new FakeDisplay("b", 50);
        var coordinator = Make(a, b);
        coordinator.Tick(100, TimeSpan.Zero);
        b.Level = 30;
        var result = coordinator.Tick(100, TimeSpan.FromSeconds(1));
        Require(result.Displays.Single(d => d.Id == "b").Status == "Checking manual change");
        Require(a.Writes == 0 && b.Writes == 0);
        b.Level = 50;
        result = coordinator.Tick(100, TimeSpan.FromSeconds(2));
        Require(result.Displays.All(d => d.LearnedProfile is null));
        Near(50, b.Level);
    }),
    ("A failing external monitor does not block the laptop", () =>
    {
        using var a = new FakeDisplay("a", 50); using var b = new FakeDisplay("b", 50) { FailWrites = true };
        var coordinator = Make(a, b);
        coordinator.Tick(200, TimeSpan.Zero);
        var result = coordinator.Tick(200, TimeSpan.FromSeconds(1));
        Require(a.Level > 50); Require(result.Displays.Single(d => d.Id == "b").Brightness is null);
        Require(result.Displays.Single(d => d.Id == "b").Status.Contains("Retry"));
        var attempts = b.WriteAttempts;
        coordinator.Tick(200, TimeSpan.FromSeconds(2));
        Require(b.WriteAttempts == attempts);
    }),
    ("A recovered monitor reseeds from actual brightness", () =>
    {
        using var display = new FakeDisplay("recover", 50) { FailWrites = true };
        var coordinator = Make(display);
        coordinator.Tick(200, TimeSpan.Zero); coordinator.Tick(200, TimeSpan.FromSeconds(1));
        display.FailWrites = false; display.Level = 30;
        var result = coordinator.Tick(200, TimeSpan.FromSeconds(11));
        Require(display.Level == 30);
        coordinator.Tick(200, TimeSpan.FromSeconds(12)); Require(display.Level > 30 && display.Level <= 44);
    }),
    ("Disabled monitors receive no reads or writes", () =>
    {
        using var a = new FakeDisplay("a", 50); using var b = new FakeDisplay("b", 50);
        var coordinator = new BrightnessCoordinator([new(a, new()), new(b, new(false))]);
        for (var i = 0; i < 10; i++) coordinator.Tick(200, TimeSpan.FromSeconds(i));
        Require(b.Reads == 0 && b.Writes == 0);
    }),
    ("Room preferences preserve independent levels without a startup jump", () =>
    {
        using var a = new FakeDisplay("a", 50); using var b = new FakeDisplay("b", 70);
        var coordinator = new BrightnessCoordinator([
            new(a, new(true, 10, 100, 50, 30)), new(b, new(true, 10, 100, 70, 30))]);
        for (var i = 0; i < 60; i++) coordinator.Tick(30 + (i % 3 - 1) * .3, TimeSpan.FromSeconds(i));
        Near(50, a.Level); Near(70, b.Level); Require(a.Writes == 0 && b.Writes == 0);
    }),
    ("A fresh room reference starts in darkness or saturation without changing preferences", () =>
    {
        foreach (var light in new[] { 0d, 0.7916666667, 2.9, 248, 255 })
        {
            using var a = new FakeDisplay("a", 45); using var b = new FakeDisplay("b", 100);
            var aProfile = new DisplayProfile().WithRoomReference(a.Level, light);
            var bProfile = new DisplayProfile().WithRoomReference(b.Level, light);
            var coordinator = new BrightnessCoordinator([new(a, aProfile), new(b, bProfile)]);
            for (var i = 0; i <= 50; i++) coordinator.Tick(light, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
            Near(45, a.Level); Near(100, b.Level); Require(a.Writes == 0 && b.Writes == 0);
        }
    }),
    ("A dark starting reference still follows a later increase in light", () =>
    {
        var profile = new DisplayProfile().WithRoomReference(45, .7916666667);
        using var a = new FakeDisplay("a", 45);
        var coordinator = new BrightnessCoordinator([new(a, profile)]);
        for (var i = 0; i <= 10; i++) coordinator.Tick(.7916666667, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
        for (var i = 11; i <= 40; i++) coordinator.Tick(20, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
        Near(profile.Target(20), a.Level, 1); Require(a.Level > 70);
        foreach (var invalid in new[] { -1d, 256d, double.NaN, double.PositiveInfinity })
            Throws(() => profile.WithRoomReference(45, invalid));
    }),
    ("Relative room mapping is bounded and reaches full external brightness", () =>
    {
        var profile = new DisplayProfile(true, 10, 100, 70, 20);
        Near(70, profile.Target(20)); Near(88, profile.Target(48)); Near(100, profile.Target(255));
        Near(10, new DisplayProfile(true, 10, 100, 20, 100).Target(0));
        for (var n = 1; n <= 255; n++) Require(profile.Target(n) >= profile.Target(n - 1));
        Throws(() => (profile with { ReferenceLight = double.NaN }).Validate());
    }),
    ("One-frame flashes and small camera noise do not move filtered light", () =>
    {
        var filter = new AmbientFilter();
        filter.Update(50, 0); filter.Update(50, 1); filter.Update(50, 1);
        Near(50, filter.Update(255, 1)); Near(50, filter.Update(50, 1));
        for (var i = 0; i < 100; i++) Near(50, filter.Update(50 + (i % 3 - 1), 1));
    }),
    ("Fast transitions stay incremental and brighten slightly faster than they dim", () =>
    {
        var ramp = new BrightnessRamp(50);
        for (var i = 0; i < 180; i++)
        {
            var before = ramp.Value;
            ramp.Advance(i < 60 ? 0 : 100, .1);
            Require(Math.Abs(ramp.Value - before) <= 7.000001);
        }
        var up = new BrightnessRamp(50); var down = new BrightnessRamp(50);
        up.Advance(55, 1); down.Advance(45, 1);
        Require(up.Value - 50 > 50 - down.Value);
        var beforeStall = ramp.Value; ramp.Advance(0, 3600);
        Require(Math.Abs(ramp.Value - beforeStall) <= 14.000001);
    }),
    ("A sustained dark scene dims both screens gently and light restores them", () =>
    {
        using var a = new FakeDisplay("a", 50); using var b = new FakeDisplay("b", 70);
        var coordinator = new BrightnessCoordinator([
            new(a, new(true, 10, 100, 50, 40)), new(b, new(true, 10, 100, 70, 40))]);
        coordinator.Tick(40, TimeSpan.Zero);
        for (var i = 1; i <= 90; i++)
        {
            var beforeA = a.Level; var beforeB = b.Level;
            coordinator.Tick(i < 30 ? 0 : 40, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
            Require(Math.Abs(a.Level - beforeA) <= 7 && Math.Abs(b.Level - beforeB) <= 7);
            if (i == 29) Require(a.Level < 25 && b.Level < 40);
        }
        Near(50, a.Level, 1); Near(70, b.Level, 1);
    }),
    ("Native manual changes teach one screen without stopping the other", () =>
    {
        using var a = new FakeDisplay("a", 50); using var b = new FakeDisplay("b", 70);
        var coordinator = new BrightnessCoordinator([
            new(a, new(true, 10, 100, 50, 20)), new(b, new(true, 10, 100, 70, 20))]);
        coordinator.Tick(48, TimeSpan.Zero); a.Level = 35;
        var cycle = coordinator.Tick(48, TimeSpan.FromSeconds(1));
        Near(35, a.Level); Require(b.Level > 70);
        Require(cycle.Displays.Single(d => d.Id == "a").LearnedProfile is null);
        cycle = coordinator.Tick(48, TimeSpan.FromSeconds(2));
        var learned = cycle.Displays.Single(d => d.Id == "a").LearnedProfile!;
        Near(35, learned.PreferredBrightness!.Value); Near(48, learned.ReferenceLight!.Value);
        for (var i = 3; i < 20; i++) coordinator.Tick(48, TimeSpan.FromSeconds(i));
        Near(35, a.Level); Require(b.Level > 80);
    }),
    ("Slider preferences are immediate and are not fought by automatic mode", () =>
    {
        using var a = new FakeDisplay("a", 50);
        var coordinator = new BrightnessCoordinator([new(a, new(true, 10, 85, 50, 40))]);
        coordinator.Tick(40, TimeSpan.Zero);
        var profile = coordinator.SetPreference("a", 100, 40);
        Near(100, a.Level); Near(100, profile.Maximum); Near(40, profile.ReferenceLight!.Value);
        for (var i = 1; i < 60; i++) coordinator.Tick(40, TimeSpan.FromSeconds(i));
        Near(100, a.Level); Require(a.Writes == 1);
    }),
    ("Invalid preference samples cannot write hardware", () =>
    {
        using var a = new FakeDisplay("a", 50); var coordinator = Make(a);
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -1d, 256d })
            Throws(() => coordinator.SetPreference("a", 90, bad));
        Require(a.Writes == 0 && a.Reads == 0);
    }),
    ("Delayed monitor readback cannot silently recalibrate room preferences", () =>
    {
        using var a = new FakeDisplay("a", 50) { ReadLag = 2 };
        using var b = new FakeDisplay("b", 70) { ReadLag = 3 };
        var coordinator = new BrightnessCoordinator([
            new(a, new(true, 10, 100, 50, 40)), new(b, new(true, 10, 100, 70, 40))]);
        for (var i = 0; i < 1200; i++)
        {
            var cycle = coordinator.Tick(i is > 100 and < 200 ? 0 : 40, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
            Require(cycle.Displays.All(d => d.LearnedProfile is null));
            Require(cycle.Displays.All(d => d.Status != "Checking manual change"));
        }
        Near(50, a.Level, 1); Near(70, b.Level, 1);
    }),
    ("Dark and bright camera steps finish within three seconds without a jump", () =>
    {
        foreach (var (start, reference, nextLight) in new[] { (95, 180d, 0d), (10, 3d, 255d), (50, 40d, 0d), (30, 0d, 40d) })
        {
            using var screen = new FakeDisplay("timing", start);
            var profile = new DisplayProfile(true, 0, 100, start, reference);
            var coordinator = new BrightnessCoordinator([new(screen, profile)]);
            for (var i = 0; i <= 10; i++) coordinator.Tick(reference, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
            var expected = (int)Math.Round(profile.Target(nextLight));
            var levels = new List<int>();
            for (var i = 1; i <= 30; i++)
            {
                var before = screen.Level;
                coordinator.Tick(nextLight, TimeSpan.FromSeconds(1 + i * .1), i % 2 == 0);
                Require(Math.Abs(screen.Level - before) <= 7);
                levels.Add(screen.Level);
            }
            Near(expected, screen.Level, 1);
            Require(levels.Distinct().Count() >= 5);
        }
    }),
    ("Reusing a camera frame cannot turn one flash into a lasting light change", () =>
    {
        using var screen = new FakeDisplay("flash", 50);
        var coordinator = new BrightnessCoordinator([new(screen, new(true, 10, 100, 50, 40))]);
        for (var i = 0; i <= 10; i++) coordinator.Tick(40, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
        for (var i = 11; i <= 30; i++)
            coordinator.Tick(i is 12 or 13 ? 255 : 40, TimeSpan.FromSeconds(i * .1), i % 2 == 0);
        Require(screen.Writes == 0);
    }),
    ("Quantized hardware levels are not mistaken for manual changes", () =>
    {
        using var display = new FakeDisplay("steps", 50) { Step = 10 };
        var coordinator = Make(display);
        for (var i = 0; i < 30; i++)
            Require(coordinator.Tick(200, TimeSpan.FromSeconds(i)).Displays.All(d => d.LearnedProfile is null));
        Require(display.Level > 50);
    }),
    ("Current settings ignore retired fields without losing user preferences", () =>
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AutoBrightness.App.Settings>("""
            {"Version":3,"AutomaticEnabled":true,"CameraId":"camera","SharedCamera":true,
             "ExposureMilliseconds":31.25,"Calibration":{"Dark":15,"Bright":180,"Curve":0.75},
             "Displays":{"screen":{"Enabled":true,"Minimum":12,"Maximum":98,"PreferredBrightness":56,"ReferenceLight":42}}}
            """)!;
        settings.Upgrade(); settings.Validate();
        Require(settings.AutomaticEnabled && settings.CameraId == "camera");
        Require(settings.Displays["screen"] == new DisplayProfile(true, 12, 98, 56, 42));
        var saved = System.Text.Json.JsonSerializer.Serialize(settings);
        Require(!saved.Contains("Calibration") && !saved.Contains("SharedCamera"));
    }),
    ("The three-sample median preserves its warmup and rolling-window behavior", () =>
    {
        var filter = new AmbientFilter();
        double[] input = [40, 41, 255, 42, 0, 43, 44, 45, 46, 0, 47, 48];
        // Golden output from the pre-cleanup filter: startup, a flash, darkness and a rising scene.
        double[] expected = [40, 40, 40, 41.6222487943249, 41.9286520133055, 42,
            42, 43.6222487943249, 43.9286520133055, 44, 45.6222487943249, 45.9286520133055];
        for (var i = 0; i < input.Length; i++) Near(expected[i], filter.Update(input[i], .2), 1e-10);
    }),
    ("Filtering successive frames does not allocate managed memory", () =>
    {
        var filter = new AmbientFilter();
        for (var i = 0; i < 1000; i++) filter.Update(i % 256, .2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) filter.Update(i % 256, .2);
        Require(GC.GetAllocatedBytesForCurrentThread() == before);
    })
};
var failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static BrightnessCoordinator Make(params FakeDisplay[] displays) => new(displays.Select(d => new DisplayPlan(d, new DisplayProfile(true, 0, 100, 50, 100))));
static byte[] SceneImage(Func<int, int, byte> luminance, int width = 160, int height = 120)
{
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var offset = (y * width + x) * 4;
        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = luminance(x, y);
        pixels[offset + 3] = 255;
    }
    return pixels;
}
static void Require(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Near(double expected, double actual, double tolerance = 0.01) { if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected}, got {actual}"); }
static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected an argument error"); }

sealed class FakeDisplay(string id, int level) : IDisplayBrightness
{
    public string Id => id;
    public string Name => id;
    public string Connection => "Fake";
    public int Level = level;
    public int Reads, Writes, WriteAttempts;
    public bool FailWrites;
    public int Step = 1;
    public int ReadLag;
    private readonly Queue<int> _readback = new();
    private readonly int _initial = level;
    public int Read()
    {
        Reads++; _readback.Enqueue(Level);
        return _readback.Count > ReadLag ? _readback.Dequeue() : _initial;
    }
    public int Write(int percent)
    {
        WriteAttempts++;
        if (FailWrites) throw new InvalidOperationException("Disconnected");
        Writes++; Level = (int)Math.Round((double)percent / Step) * Step; return Level;
    }
    public void Dispose() { }
}
