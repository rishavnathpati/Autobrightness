using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using AutoBrightness.App.Hardware;
using Forms = System.Windows.Forms;

namespace AutoBrightness.App;

public partial class MainWindow : Window
{
    private readonly WebcamLightSensor _sensor = new();
    private readonly ObservableCollection<DisplayRow> _rows = [];
    private readonly object _controlGate = new();
    private readonly DispatcherTimer _timer;
    private readonly Forms.NotifyIcon _tray;
    private Settings _settings;
    private DisplayInventory? _inventory;
    private BrightnessCoordinator? _coordinator;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private bool _busy, _cameraRunning, _exit, _wantAutomatic, _suspended, _refreshPending, _syncSliders;
    private string? _pendingStop;
    private long _lastExposureCheck;
    private bool _record;
    private string? _tracePath;
    private readonly string? _loadWarning;

    public MainWindow()
    {
        InitializeComponent();
        _settings = Settings.Load(out _loadWarning);
        _wantAutomatic = _settings.AutomaticEnabled;
        ExposureBox.Text = _settings.ExposureMilliseconds.ToString(CultureInfo.CurrentCulture);
        DisplayCards.ItemsSource = LimitsList.ItemsSource = _rows;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += UiTick;
        _sensor.Failed += reason => Dispatcher.BeginInvoke(() => RequestStop(reason, false));
        _tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "AutoBrightness · paused", Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
        _tray.ContextMenuStrip.Items.Add("Open AutoBrightness", null, (_, _) => Reveal());
        _tray.ContextMenuStrip.Items.Add("Pause", null, async (_, _) => await Guard(Pause));
        _tray.ContextMenuStrip.Items.Add("Quit", null, async (_, _) => await Quit());
        _tray.DoubleClick += (_, _) => Reveal();
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await Guard(async () => { await RefreshCore(); if (_wantAutomatic && App.SmokeOutput is null) await StartAutomatic(); });
        if (_loadWarning is not null) StatusText.Text = _loadWarning;
        _timer.Start();
        if (App.SmokeOutput is string path)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            var render = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            render.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(render));
            using (var output = File.Create(path)) encoder.Save(output);
            await Quit();
        }
    }

    private void Status(string title, string description)
    {
        StatusTitle.Text = title; StatusText.Text = description;
        System.Windows.Automation.AutomationProperties.SetName(StatusText, description);
    }

    private async Task Guard(Func<Task> action)
    {
        if (_busy || _exit) return;
        _busy = true; Controls();
        try { await action(); }
        catch (Exception ex)
        {
            AppLog.Write("Application operation", ex);
            _wantAutomatic = _settings.AutomaticEnabled = false;
            await StopCore();
            try { _settings.Save(); } catch (Exception saveError) { AppLog.Write("Save paused state", saveError); }
            Status("Paused", ex.Message);
        }
        finally { _busy = false; Controls(); }
    }

    private async Task RefreshCore()
    {
        await StopCore();
        Status("Checking your screens…", "Reading the connected displays and camera.");
        var old = _inventory; _inventory = null;
        _inventory = await Task.Run(() => { old?.Dispose(); return DisplayInventory.Discover(); });
        _syncSliders = true;
        _rows.Clear();
        foreach (var entry in _inventory.Displays)
            _rows.Add(new DisplayRow(entry, _settings.Displays.GetValueOrDefault(entry.Id) ?? new DisplayProfile()));
        _syncSliders = false;
        var cameras = await WebcamLightSensor.FindAsync();
        CameraBox.ItemsSource = cameras;
        CameraBox.SelectedItem = cameras.FirstOrDefault(c => c.Id == _settings.CameraId) ?? cameras.FirstOrDefault();
        Status("Ready when you are", cameras.Count == 0 ? "No webcam found. Connect a camera and refresh." :
            "Choose comfortable brightness below, then turn on automatic. I'll learn this room as your starting point.");
    }

    private void ReadOptions()
    {
        CommitAndValidateInputs(LimitsList);
        if (!double.TryParse(ExposureBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var exposure) || !double.IsFinite(exposure) || exposure is < 0.1 or > 1000)
            throw new ArgumentException("Exposure must be a number between 0.1 and 1000 ms.");
        var cameraId = (CameraBox.SelectedItem as CameraChoice)?.Id ?? throw new InvalidOperationException("Choose a webcam first.");
        var changed = cameraId != _settings.CameraId || exposure != _settings.ExposureMilliseconds;
        _settings.CameraId = cameraId; _settings.ExposureMilliseconds = exposure; _settings.SharedCamera = false;
        foreach (var row in _rows)
        {
            var old = _settings.Displays.GetValueOrDefault(row.Entry.Id) ?? new DisplayProfile();
            var profile = old with { Enabled = row.Enabled, Minimum = row.Minimum, Maximum = row.Maximum };
            if (changed) profile = profile with { ReferenceLight = null, PreferredBrightness = null };
            profile.Validate(); _settings.Displays[row.Entry.Id] = profile;
        }
        _settings.Validate();
    }

    private async Task StartCamera()
    {
        if (_cameraRunning) return;
        Status("Learning the room…", "Turning off auto exposure and waiting for the camera to settle.");
        ExposureText.Text = "Starting camera…";
        CameraHint.Text = "";
        LightText.Text = "Waiting for camera frames…";
        await _sensor.StartAsync(_settings.CameraId!, false, _settings.ExposureMilliseconds);
        _cameraRunning = true;
        await Task.Delay(1800);
        _sensor.VerifyExposureLock();
        if (_sensor.Latest is not { } sample || Environment.TickCount64 - sample.Timestamp > 2500)
            throw new InvalidOperationException("No recent camera frames. Check that the webcam is available.");
        UpdateCameraStatus();
        _lastExposureCheck = Environment.TickCount64;
    }

    private async Task StartAutomatic(bool recapture = false)
    {
        ReadOptions();
        if (!_rows.Any(r => r.Enabled && r.Supported)) throw new InvalidOperationException("Select at least one supported screen in Settings.");
        await StartCamera();
        var light = _sensor.Latest!.Level;
        var plans = new List<DisplayPlan>();
        foreach (var row in _rows.Where(r => r.Enabled && r.Supported))
        {
            var profile = _settings.Displays[row.Entry.Id];
            if (recapture || profile.ReferenceLight is null || profile.PreferredBrightness is null)
            {
                var current = await Task.Run(() => row.Entry.Device!.Read());
                profile = profile.WithRoomReference(current, light);
                _settings.Displays[row.Entry.Id] = profile;
            }
            plans.Add(new DisplayPlan(row.Entry.Device!, profile));
        }
        _coordinator = new BrightnessCoordinator(plans, new Calibration(), learnManualChanges: true, smoothLight: true);
        _wantAutomatic = _settings.AutomaticEnabled = true;
        _settings.Save();
        _record = RecordCheck.IsChecked == true;
        if (_record)
        {
            Directory.CreateDirectory(Settings.DirectoryPath);
            _tracePath = Path.Combine(Settings.DirectoryPath, $"test-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            DiagnosticText.Text = "Recording numeric test data: " + _tracePath;
        }
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _worker = Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            long lastSampleTimestamp = -1;
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    var sample = _sensor.Latest;
                    if (sample is null || Environment.TickCount64 - sample.Timestamp > 5000)
                        throw new InvalidOperationException("The camera stopped sending frames. Restart automatic when it is available.");
                    CycleResult cycle; double filtered;
                    lock (_controlGate)
                    {
                        token.ThrowIfCancellationRequested();
                        cycle = _coordinator!.Tick(sample.Level, clock.Elapsed, sample.Timestamp != lastSampleTimestamp);
                        lastSampleTimestamp = sample.Timestamp;
                        filtered = _coordinator.FilteredLight;
                    }
                    if (_record && _tracePath is not null)
                    {
                        var entry = new { Time = DateTimeOffset.UtcNow, Light = sample.Level, FilteredLight = filtered,
                            ExposureLocked = true, sample.DarkPixelFraction, sample.ClippedPixelFraction, Displays = cycle.Displays };
                        if (!File.Exists(_tracePath) || new FileInfo(_tracePath).Length < 2_000_000)
                            await File.AppendAllTextAsync(_tracePath, JsonSerializer.Serialize(entry) + Environment.NewLine, token);
                    }
                    await Dispatcher.InvokeAsync(() => ApplyCycle(cycle));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { AppLog.Write("Brightness loop", ex); _ = Dispatcher.BeginInvoke(() => RequestStop(ex.Message, false)); }
        }, token);
        _tray.Text = "AutoBrightness · automatic";
        Status("Automatic is on", "Both screens follow the room gently. Drag a slider whenever you want a different level; I'll remember your preference.");
    }

    private void ApplyCycle(CycleResult cycle)
    {
        var save = false;
        _syncSliders = true;
        foreach (var reading in cycle.Displays)
        {
            var row = _rows.FirstOrDefault(r => r.Entry.Id == reading.Id);
            if (row is null) continue;
            row.Current = reading.Observed ?? reading.Brightness;
            row.Target = reading.Target;
            row.Status = reading.Status == "Following camera" ? "Adjusting gently" : reading.Status;
            if (!row.Pending && row.Current is int actual) row.SliderValue = actual;
            if (reading.LearnedProfile is { } learned) { _settings.Displays[reading.Id] = learned; save = true; }
        }
        _syncSliders = false;
        if (save) _settings.Save();
    }

    private async Task StopCore()
    {
        _cancellation?.Cancel();
        if (_worker is not null) { try { await _worker; } catch (OperationCanceledException) { } _worker = null; }
        _cancellation?.Dispose(); _cancellation = null; _coordinator = null;
        await _sensor.StopAsync(); _cameraRunning = false;
        ExposureText.Text = "Camera is OFF · Automatic is paused. Current brightness is preserved.";
        CameraHint.Text = "";
        LightText.Text = "Camera is off";
        _tray.Text = "AutoBrightness · paused";
        foreach (var row in _rows) { row.Target = null; if (row.Supported) row.Status = "Manual control"; }
    }

    private async Task Pause()
    {
        _wantAutomatic = _settings.AutomaticEnabled = false;
        await StopCore(); _settings.Save();
        Status("Automatic is paused", "Your screens keep their current brightness. Turn on automatic to resume.");
    }

    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncSliders || !_timer.IsEnabled || sender is not Slider { DataContext: DisplayRow row } || !row.Supported) return;
        row.Pending = true; row.EditedAt = Environment.TickCount64;
    }

    private async Task ApplySlider(DisplayRow row)
    {
        row.Pending = false;
        var desired = (int)Math.Round(row.SliderValue);
        DisplayProfile profile;
        if (row.Enabled && _coordinator is not null && _sensor.Latest is { } sample)
            profile = await Task.Run(() => { lock (_controlGate) return _coordinator.SetPreference(row.Entry.Id, desired, sample.Level); });
        else
        {
            var actual = await Task.Run(() => row.Entry.Device!.Write(desired));
            profile = (_settings.Displays.GetValueOrDefault(row.Entry.Id) ?? new DisplayProfile()) with { PreferredBrightness = actual, ReferenceLight = null };
        }
        _settings.Displays[row.Entry.Id] = profile; _settings.Save();
        row.Current = await Task.Run(() => { lock (_controlGate) return row.Entry.Device!.Read(); });
        row.Status = _worker is null ? "Manual control" : "Preference saved for this light";
    }

    private async void UiTick(object? sender, EventArgs e)
    {
        if (_exit) return;
        UpdateCameraStatus();
        if (_busy) return;
        if (_cameraRunning && Environment.TickCount64 - _lastExposureCheck > 2000)
        {
            _lastExposureCheck = Environment.TickCount64;
            try { _sensor.VerifyExposureLock(); } catch (Exception ex) { RequestStop(ex.Message, false); }
        }
        if (_pendingStop is string reason)
        {
            _pendingStop = null;
            await Guard(async () => { await StopCore(); _settings.Save(); Status(_wantAutomatic ? "Waiting to resume" : "Paused", reason); });
        }
        if (_refreshPending && !_suspended && !_busy)
        {
            _refreshPending = false;
            await Guard(async () => { await RefreshCore(); if (_wantAutomatic) await StartAutomatic(); });
        }
        var pending = _rows.FirstOrDefault(r => r.Pending && Environment.TickCount64 - r.EditedAt > 500);
        if (pending is not null && !_busy) await Guard(() => ApplySlider(pending));
        Controls();
    }

    private void RequestStop(string reason, bool keepIntent)
    {
        _cancellation?.Cancel(); _pendingStop = reason;
        if (!keepIntent) _wantAutomatic = _settings.AutomaticEnabled = false;
    }
    private void UpdateCameraStatus()
    {
        if (!_cameraRunning) return;
        if (_sensor.Latest is not { } sample || Environment.TickCount64 - sample.Timestamp > 2500)
        {
            ExposureText.Text = "Camera is ON · Waiting for fresh frames…";
            LightText.Text = "Waiting for fresh camera frames…";
            CameraHint.Text = "";
            return;
        }
        ExposureText.Text = $"Camera is ON · {_sensor.ModeDescription.Split(". ")[0]} · Light {sample.Level:0.0} / 255";
        LightText.Text = $"Live camera light: {sample.Level:0.0} / 255 · auto exposure is OFF";
        CameraHint.Text = sample.Level < 3
            ? "Dark scene · Automatic is active."
            : sample.ClippedPixelFraction > 0.5
                ? "Very bright scene · For more sensitivity in bright rooms, pause and lower the fixed exposure in Settings."
                : "";
    }
    private static void CommitAndValidateInputs(DependencyObject parent)
    {
        if (parent is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (Validation.GetHasError(box)) throw new ArgumentException("Use whole numbers from 0 to 100 for screen limits.");
        }
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            CommitAndValidateInputs(System.Windows.Media.VisualTreeHelper.GetChild(parent, i));
    }
    private void Controls()
    {
        StartButton.Content = _worker is null ? "Turn on automatic" : "Pause automatic";
        StartButton.IsEnabled = !_busy && CameraBox.SelectedItem is not null && _rows.Any(r => r.Supported);
        RememberButton.IsEnabled = !_busy && _worker is not null;
        CameraOptions.IsEnabled = !_busy && !_cameraRunning;
        ScreenTestButton.IsEnabled = !_busy;
        RecordCheck.IsEnabled = !_busy && _worker is null;
        foreach (var row in _rows) row.CanEditLimits = !_busy && _worker is null && row.Supported;
    }
    private async void StartClicked(object sender, RoutedEventArgs e) => await Guard(async () => { if (_worker is null) await StartAutomatic(); else await Pause(); });
    private async void RememberClicked(object sender, RoutedEventArgs e) => await Guard(async () => { await StopCore(); await StartAutomatic(true); });
    private async void RefreshClicked(object sender, RoutedEventArgs e) => await Guard(RefreshCore);
    private async void QuitClicked(object sender, RoutedEventArgs e) => await Quit();

    private async void ScreenTestClicked(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var resume = _wantAutomatic;
        await StopCore();
        Status("Testing your screens…", "Each screen changes by five points briefly, then returns to its original level.");
        var reports = await Task.Run(() =>
        {
            var results = new List<string>();
            foreach (var row in _rows.Where(r => r.Supported).ToArray())
            {
                var device = row.Entry.Device!; var original = device.Read();
                int observed = -1; var requested = original; var restored = original;
                try { requested = device.Write(original <= 95 ? original + 5 : original - 5); Thread.Sleep(700); observed = device.Read(); }
                finally { device.Write(original); Thread.Sleep(400); restored = device.Read(); }
                results.Add($"{row.Name}: {original}% → {observed}% → {restored}%. {(observed == requested && restored == original ? "PASS" : "FAIL")}");
            }
            return results;
        });
        DiagnosticText.Text = string.Join(Environment.NewLine, reports);
        if (resume) await StartAutomatic(); else Status("Screen test complete", string.Join(" ", reports));
    });

    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { RequestStop("Displays changed. Reconnecting…", true); _refreshPending = true; });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Mode == PowerModes.Suspend) { _suspended = true; RequestStop("Sleeping. The camera is released.", true); }
        if (e.Mode == PowerModes.Resume) { _suspended = false; _refreshPending = true; }
    });
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        { _suspended = true; RequestStop("Session is inactive. The camera is released.", true); }
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        { _suspended = false; _refreshPending = true; }
    });
    public void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void WindowStateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized) Hide(); }
    private void WindowClosing(object? sender, CancelEventArgs e) { if (!_exit) { e.Cancel = true; Hide(); } }
    private async Task Quit()
    {
        if (_busy || _exit) return;
        await Guard(StopCore); _exit = true; _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        _tray.Dispose(); await Task.Run(() => _inventory?.Dispose());
        Application.Current.Shutdown();
    }
}

public sealed class DisplayRow : INotifyPropertyChanged
{
    public DisplayEntry Entry { get; }
    public string Name => Entry.Connection == "WMI" ? "Laptop screen" : Entry.Name;
    public string SliderName => Name + " brightness";
    public bool Supported => Entry.Supported;
    public bool Pending; public long EditedAt;
    public bool Enabled { get; set; }
    private bool _canEdit;
    public bool CanEditLimits { get => _canEdit; set { _canEdit = value; Changed(); } }
    private int _minimum, _maximum;
    public int Minimum { get => _minimum; set { if (value is < 0 or > 100) throw new ArgumentException("Use 0–100"); _minimum = value; Changed(); } }
    public int Maximum { get => _maximum; set { if (value is < 0 or > 100) throw new ArgumentException("Use 0–100"); _maximum = value; Changed(); } }
    private double _slider;
    public double SliderValue { get => _slider; set { _slider = value; Changed(); } }
    private int? _current;
    public int? Current { get => _current; set { _current = value; Changed(nameof(CurrentLabel)); } }
    public string CurrentLabel => Current is int level ? $"{level}%" : "Unavailable";
    private double? _target;
    public double? Target { get => _target; set { _target = value; Changed(nameof(TargetLabel)); } }
    public string TargetLabel => Target is double value ? $"Target {value:0}%" : "";
    private string _status;
    public string Status { get => _status; set { _status = value; Changed(); } }
    public DisplayRow(DisplayEntry entry, DisplayProfile profile)
    {
        Entry = entry; Enabled = profile.Enabled && entry.Supported; _minimum = profile.Minimum; _maximum = profile.Maximum;
        _current = entry.InitialBrightness; _slider = entry.InitialBrightness ?? 50; _status = entry.Detail;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
