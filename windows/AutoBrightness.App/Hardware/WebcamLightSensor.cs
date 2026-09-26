using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace AutoBrightness.App.Hardware;

public sealed record CameraChoice(string Id, string Name);
public sealed record LightSample(double Level, long Timestamp, double DarkPixelFraction, double ClippedPixelFraction);

public sealed class WebcamLightSensor : IAsyncDisposable
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private long _lastFrame;
    private int _processing;
    private int _acceptFrames;
    private byte[] _pixels = [];
    private IBuffer? _pixelBuffer;
    private bool _exposureChanged;
    private bool _previousAuto;
    private TimeSpan _previousExposure;
    private bool _legacyExposureChanged;
    private double _previousLegacyExposure;
    private bool _previousLegacyAuto;
    private bool _legacyHasAuto;
    private double _lockedLegacyValue;
    private TimeSpan _lockedExposure;
    private LightSample? _latest;
    public LightSample? Latest => Volatile.Read(ref _latest);
    public string ModeDescription { get; private set; } = "Camera stopped";
    public event Action<string>? Failed;

    public static async Task<IReadOnlyList<CameraChoice>> FindAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        return groups.Where(g => g.SourceInfos.Any(s => s.SourceKind == MediaFrameSourceKind.Color))
            .Select(g => new CameraChoice(g.Id, g.DisplayName)).ToArray();
    }

    // Called from the WPF STA thread so Windows can display any camera consent UI.
    public async Task StartAsync(string cameraId, double exposureMilliseconds)
    {
        if (!double.IsFinite(exposureMilliseconds) || exposureMilliseconds is < 0.1 or > 1000)
            throw new ArgumentException("Exposure must be 0.1–1000 ms.");
        await StopAsync();
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var group = groups.FirstOrDefault(g => g.Id == cameraId)
                ?? throw new InvalidOperationException("The selected camera is disconnected. Refresh devices.");
            _capture = new MediaCapture();
            _capture.Failed += CaptureFailed;
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });
            var source = _capture.FrameSources.Values
                .Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderBy(s => s.Info.MediaStreamType == Windows.Media.Capture.MediaStreamType.VideoPreview ? 0 : 1)
                .FirstOrDefault() ?? throw new InvalidOperationException("No colour camera stream is available.");
            var format = source.SupportedFormats
                .Where(f => f.VideoFormat.Width >= 160 && f.VideoFormat.Height >= 120 && f.FrameRate.Denominator > 0)
                .OrderBy(f => (long)f.VideoFormat.Width * f.VideoFormat.Height)
                .ThenBy(f => (double)f.FrameRate.Numerator / f.FrameRate.Denominator)
                .FirstOrDefault();
            if (format is not null) await source.SetFormatAsync(format);
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8,
                new BitmapSize { Width = 160, Height = 120 });
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += FrameArrived;
            Volatile.Write(ref _acceptFrames, 1);
            var result = await _reader.StartAsync();
            if (result != MediaFrameReaderStartStatus.Success)
                throw new InvalidOperationException($"Camera could not start ({result}). Close other camera apps and try again.");

            await LockExposure(exposureMilliseconds);
            // Discard frames produced before camera controls finished settling.
            Volatile.Write(ref _latest, null);
            Interlocked.Exchange(ref _lastFrame, Environment.TickCount64);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    private async Task LockExposure(double exposureMilliseconds)
    {
        var exposure = _capture!.VideoDeviceController.ExposureControl;
        if (exposure.Supported)
        {
            _previousAuto = exposure.Auto;
            _previousExposure = exposure.Value;
            _exposureChanged = true;
            await exposure.SetAutoAsync(false);
            var ticks = Math.Clamp(TimeSpan.FromMilliseconds(exposureMilliseconds).Ticks, exposure.Min.Ticks, exposure.Max.Ticks);
            if (exposure.Step.Ticks > 0)
                ticks = exposure.Min.Ticks + (long)Math.Round((double)(ticks - exposure.Min.Ticks) / exposure.Step.Ticks) * exposure.Step.Ticks;
            ticks = Math.Clamp(ticks, exposure.Min.Ticks, exposure.Max.Ticks);
            await exposure.SetValueAsync(TimeSpan.FromTicks(ticks));
            if (exposure.Auto) throw new InvalidOperationException("The camera did not disable automatic exposure.");
            _lockedExposure = exposure.Value;
            ModeDescription = $"Exposure locked at {exposure.Value.TotalMilliseconds:0.##} ms";
        }
        else
        {
            // Many UVC webcams expose the older log2-seconds control only.
            var legacy = _capture.VideoDeviceController.Exposure;
            var caps = legacy.Capabilities;
            if (!caps.Supported || !legacy.TryGetValue(out _previousLegacyExposure))
                throw new InvalidOperationException("This camera cannot lock exposure through Windows. Choose a webcam that supports manual exposure.");
            _legacyHasAuto = caps.AutoModeSupported;
            if (_legacyHasAuto && !legacy.TryGetAuto(out _previousLegacyAuto))
                throw new InvalidOperationException("Cannot verify the camera exposure mode. Close other camera apps or choose another webcam.");
            _legacyExposureChanged = true;
            if (_legacyHasAuto && !legacy.TrySetAuto(false))
                throw new InvalidOperationException("Camera rejected manual exposure mode. Close other camera apps or choose another webcam.");
            var value = Math.Clamp(Math.Log2(exposureMilliseconds / 1000), caps.Min, caps.Max);
            if (caps.Step > 0) value = caps.Min + Math.Round((value - caps.Min) / caps.Step) * caps.Step;
            value = Math.Clamp(value, caps.Min, caps.Max);
            if (!legacy.TrySetValue(value) || !legacy.TryGetValue(out var actual) ||
                (_legacyHasAuto && (!legacy.TryGetAuto(out var automatic) || automatic)))
                throw new InvalidOperationException("Camera did not accept fixed exposure. Choose another exposure value or webcam.");
            ModeDescription = $"Exposure locked at {1000 * Math.Pow(2, actual):0.##} ms (UVC)";
            _lockedLegacyValue = actual;
        }
    }

    private void CaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args) => Failed?.Invoke($"Camera stopped: {args.Message}");

    public void VerifyExposureLock()
    {
        if (_capture is null) throw new InvalidOperationException("Camera is stopped.");
        if (_exposureChanged)
        {
            var control = _capture.VideoDeviceController.ExposureControl;
            if (control.Auto || control.Value != _lockedExposure)
                throw new InvalidOperationException("Camera exposure changed outside this app. Automatic brightness paused; restart to lock it again.");
        }
        else if (_legacyExposureChanged)
        {
            var control = _capture.VideoDeviceController.Exposure;
            if (!control.TryGetValue(out var actual) || Math.Abs(actual - _lockedLegacyValue) > 0.01 ||
                (_legacyHasAuto && (!control.TryGetAuto(out var automatic) || automatic)))
                throw new InvalidOperationException("Camera auto exposure is no longer locked. Automatic brightness paused; restart to lock it again.");
        }
        else throw new InvalidOperationException("Automatic brightness requires a verified exposure lock.");
    }

    private void FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        var now = Environment.TickCount64;
        if (Volatile.Read(ref _acceptFrames) == 0 || now - Interlocked.Read(ref _lastFrame) < 200 ||
            Interlocked.CompareExchange(ref _processing, 1, 0) != 0) return;
        try
        {
            if (Volatile.Read(ref _acceptFrames) == 0) return;
            using var frame = sender.TryAcquireLatestFrame();
            using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null) return;
            // Reader conversion produces a small tightly packed BGRA image; no images are saved.
            using var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
            var image = converted ?? bitmap;
            var length = checked(image.PixelWidth * image.PixelHeight * 4);
            // Only one callback owns these buffers. Reuse them until capture stops or size changes.
            if (_pixels.Length != length)
            {
                Array.Clear(_pixels);
                _pixels = new byte[length];
                _pixelBuffer = new Windows.Storage.Streams.Buffer((uint)length);
            }
            image.CopyToBuffer(_pixelBuffer!);
            using var reader = DataReader.FromBuffer(_pixelBuffer!);
            reader.ReadBytes(_pixels);
            var measurement = LightMeter.MeasureSceneBgra(_pixels, image.PixelWidth, image.PixelHeight);
            Volatile.Write(ref _latest, new LightSample(measurement.Level, now, measurement.DarkPixelFraction, measurement.ClippedPixelFraction));
            Interlocked.Exchange(ref _lastFrame, now);
        }
        catch (Exception ex) { Failed?.Invoke($"Camera frame error: {ex.Message}"); }
        finally { Volatile.Write(ref _processing, 0); }
    }

    public async Task StopAsync()
    {
        Volatile.Write(ref _acceptFrames, 0);
        var reader = _reader;
        _reader = null;
        if (reader is not null) reader.FrameArrived -= FrameArrived;
        // Restore the camera settings we owned so the next video call gets its old exposure.
        if (_capture is not null && _exposureChanged)
        {
            try
            {
                var exposure = _capture!.VideoDeviceController.ExposureControl;
                try { await exposure.SetValueAsync(_previousExposure); }
                finally { await exposure.SetAutoAsync(_previousAuto); }
            }
            catch (Exception ex) { AppLog.Write("Camera exposure restore", ex); }
        }
        _exposureChanged = false;
        if (_capture is not null && _legacyExposureChanged)
        {
            try
            {
                var legacy = _capture.VideoDeviceController.Exposure;
                var valueRestored = legacy.TrySetValue(_previousLegacyExposure);
                var autoRestored = !_legacyHasAuto || legacy.TrySetAuto(_previousLegacyAuto);
                if (!valueRestored || !autoRestored)
                    AppLog.Write("Camera exposure restore", new InvalidOperationException("The camera rejected restoring its previous exposure."));
            }
            catch (Exception ex) { AppLog.Write("Camera exposure restore", ex); }
        }
        _legacyExposureChanged = false;
        if (reader is not null)
        {
            try { await reader.StopAsync(); } catch (Exception ex) { AppLog.Write("Camera stop", ex); }
            // A callback already running must finish before disposing the source.
            while (Volatile.Read(ref _processing) != 0) await Task.Delay(10);
            reader.Dispose();
        }
        if (_capture is not null)
        {
            _capture.Failed -= CaptureFailed;
            _capture.Dispose();
            _capture = null;
        }
        Array.Clear(_pixels);
        _pixels = [];
        _pixelBuffer = null;
        Volatile.Write(ref _latest, null);
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
