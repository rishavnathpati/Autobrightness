using System.IO;
using System.Text.Json;
using System.Windows;
using AutoBrightness.App.Hardware;

namespace AutoBrightness.App;

public partial class App : Application
{
    internal static string? SmokeOutput { get; private set; }
    private Mutex? _singleInstance;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--verify-transitions")
        {
            // Explicit integration test: synthetic light, real displays, always restore.
            var passed = false;
            try
            {
                var report = await Task.Run(async () =>
                {
                    using var inventory = DisplayInventory.Discover();
                    var devices = inventory.Displays.Where(d => d.Device is not null).Select(d => d.Device!).ToArray();
                    var originals = devices.ToDictionary(d => d.Id, d => d.Read());
                    var samples = new List<object>();
                    var restored = new Dictionary<string, int>();
                    var errors = new List<string>();
                    try
                    {
                        var coordinator = new BrightnessCoordinator(devices.Select(d => new DisplayPlan(d,
                            new DisplayProfile(true, 10, 100, originals[d.Id], 40))), new Calibration(), true, true);
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        var previous = new Dictionary<string, int>(originals);
                        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                        var lastCameraSample = -1L;
                        while (clock.Elapsed.TotalSeconds < 15 && await timer.WaitForNextTickAsync())
                        {
                            var elapsed = clock.Elapsed;
                            var frame = (long)(elapsed.TotalSeconds * 5);
                            var light = elapsed.TotalSeconds is >= 3 and < 9 ? 0d : 40d;
                            var cycle = coordinator.Tick(light, elapsed, frame != lastCameraSample);
                            lastCameraSample = frame;
                            samples.Add(new { Time = DateTimeOffset.UtcNow, Elapsed = elapsed.TotalSeconds, Light = light, coordinator.FilteredLight, cycle.Displays });
                            foreach (var reading in cycle.Displays)
                            {
                                if (reading.LearnedProfile is not null) errors.Add($"False manual learning on {reading.Id} at {elapsed.TotalSeconds:0.00}s");
                                if (reading.Brightness is not int brightness) { errors.Add($"Unavailable: {reading.Id}"); continue; }
                                if (Math.Abs(brightness - previous[reading.Id]) > 14) errors.Add($"Large command step on {reading.Id}");
                                if (elapsed.TotalSeconds is >= 6 and < 9 or >= 12)
                                {
                                    var target = new DisplayProfile(true, 10, 100, originals[reading.Id], 40).Target(light, 0);
                                    if (Math.Abs(brightness - target) > 1) errors.Add($"Transition exceeded 3s: {reading.Id}");
                                }
                                previous[reading.Id] = brightness;
                            }
                        }
                        foreach (var device in devices)
                            if (Math.Abs(device.Read() - originals[device.Id]) > 2) errors.Add($"Did not return near original: {device.Name}");
                    }
                    finally
                    {
                        foreach (var device in devices)
                        {
                            try
                            {
                                device.Write(originals[device.Id]); await Task.Delay(1200);
                                restored[device.Id] = device.Read();
                                if (restored[device.Id] != originals[device.Id]) errors.Add($"Restore mismatch: {device.Name}");
                            }
                            catch (Exception ex) { errors.Add($"Restore {device.Name}: {ex.Message}"); }
                        }
                    }
                    passed = devices.Length > 0 && errors.Count == 0;
                    return new { Passed = passed, Input = "Synthetic light; real monitor hardware; no camera", Originals = originals, Restored = restored, Errors = errors, Samples = samples };
                });
                await File.WriteAllTextAsync(e.Args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { await File.WriteAllTextAsync(e.Args[1], ex.ToString()); }
            Shutdown(passed ? 0 : 1); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--verify-displays")
        {
            // Explicit diagnostic: change one point, read back, always restore in finally.
            var report = await Task.Run(() =>
            {
                using var inventory = DisplayInventory.Discover();
                var checks = new List<object>();
                foreach (var entry in inventory.Displays.Where(d => d.Device is not null))
                {
                    var device = entry.Device!;
                    var original = device.Read();
                    int? requested = null, observed = null, restored = null;
                    string? error = null;
                    try
                    {
                        requested = device.Write(original < 100 ? original + 1 : original - 1);
                        Thread.Sleep(400);
                        observed = device.Read();
                        if (observed != requested) error = "Brightness readback did not match the requested level.";
                    }
                    catch (Exception ex) { error = ex.Message; }
                    finally
                    {
                        try { device.Write(original); Thread.Sleep(400); restored = device.Read(); }
                        catch (Exception ex) { error = $"Restore failed: {ex.Message}; original level was {original}."; }
                    }
                    checks.Add(new { entry.Name, entry.Connection, original, requested, observed, restored, error });
                }
                return checks;
            });
            await File.WriteAllTextAsync(e.Args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Shutdown(0);
            return;
        }
        if ((e.Args.Length == 2 || e.Args.Length == 3) && e.Args[0] == "--probe-camera")
        {
            // Developer diagnostic: captures only aggregate light readings, never image files.
            try
            {
                await using var sensor = new WebcamLightSensor();
                var camera = (await WebcamLightSensor.FindAsync()).FirstOrDefault()
                    ?? throw new InvalidOperationException("No camera found.");
                var exposure = e.Args.Length == 3 ? double.Parse(e.Args[2], System.Globalization.CultureInfo.InvariantCulture) : 31.25;
                if (!double.IsFinite(exposure) || exposure is < 0.1 or > 1000) throw new ArgumentException("Exposure must be 0.1–1000 ms.");
                string? error = null;
                sensor.Failed += message => error = message;
                await sensor.StartAsync(camera.Id, false, exposure);
                var readings = new List<double>();
                var samples = new List<LightSample>();
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(600);
                    sensor.VerifyExposureLock();
                    if (sensor.Latest is { } sample) { readings.Add(sample.Level); samples.Add(sample); }
                }
                var mode = sensor.ModeDescription;
                await sensor.StopAsync();
                var fresh = samples.Count > 1 && samples.Zip(samples.Skip(1)).All(pair => pair.Second.Timestamp > pair.First.Timestamp);
                await File.WriteAllTextAsync(e.Args[1], JsonSerializer.Serialize(new { camera.Name, mode, error, fresh, readings, samples }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(fresh && error is null ? 0 : 1);
            }
            catch (Exception ex) { await File.WriteAllTextAsync(e.Args[1], ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--diagnostics")
        {
            // Read-only: never opens the camera and never writes display brightness.
            try
            {
                var report = await Task.Run(() =>
                {
                    using var inventory = DisplayInventory.Discover();
                    return inventory.Displays.Select(d => new { d.Id, d.Name, d.Connection, d.Supported, d.InitialBrightness, d.Detail }).ToArray();
                });
                await File.WriteAllTextAsync(e.Args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex) { await File.WriteAllTextAsync(e.Args[1], ex.ToString()); Shutdown(1); }
            return;
        }
        _singleInstance = new Mutex(true, @"Local\AutoBrightness.Windows.v2", out var first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\AutoBrightness.Show.v2"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("An earlier AutoBrightness version is running. Quit it from its tray menu first.", "AutoBrightness"); }
            Shutdown();
            return;
        }
        MainWindow = new MainWindow();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AutoBrightness.Show.v2");
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => ((MainWindow)MainWindow).Reveal()), null, Timeout.Infinite, false);
        if (e.Args.Length == 2 && e.Args[0] == "--ui-smoke")
        {
            SmokeOutput = e.Args[1];
            MainWindow.ShowInTaskbar = false;
            MainWindow.ShowActivated = false;
            MainWindow.Left = MainWindow.Top = -20000;
        }
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        base.OnExit(e);
    }
}
