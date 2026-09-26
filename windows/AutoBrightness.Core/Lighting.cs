namespace AutoBrightness.Core;

public sealed record DisplayProfile(bool Enabled = true, int Minimum = 10, int Maximum = 100,
    int? PreferredBrightness = null, double? ReferenceLight = null)
{
    public void Validate()
    {
        if (Minimum < 0 || Maximum > 100 || Minimum > Maximum)
            throw new ArgumentException("Each display needs 0 ≤ minimum ≤ maximum ≤ 100.");
        if (PreferredBrightness is < 0 or > 100 || (ReferenceLight is double reference && (!double.IsFinite(reference) || reference < 0 || reference > 255)))
            throw new ArgumentException("Invalid preferred brightness or room reference.");
    }

    public DisplayProfile WithRoomReference(int brightness, double light)
    {
        if (brightness is < 0 or > 100 || !double.IsFinite(light) || light is < 0 or > 255)
            throw new ArgumentException("Invalid brightness or camera reading.");
        // Darkness and saturation describe the scene, not a stopped camera.
        // The curve's noise floor keeps references near zero well behaved.
        var profile = this with { PreferredBrightness = brightness, ReferenceLight = light,
            Minimum = Math.Min(brightness, Minimum), Maximum = Math.Max(brightness, Maximum) };
        profile.Validate();
        return profile;
    }

    public double Target(double light)
    {
        if (!double.IsFinite(light) || light is < 0 or > 255) throw new ArgumentException("Invalid camera reading.");
        if (PreferredBrightness is not int preferred || ReferenceLight is not double reference)
            throw new InvalidOperationException("Set a room reference before calculating brightness.");
        // Changes in illumination are perceived roughly proportionally, rather than as raw pixel differences.
        // The noise floor avoids huge changes caused by a one-unit reading in a very dark scene.
        var change = 18 * Math.Log2((Math.Clamp(light, 0, 255) + 8) / (reference + 8));
        return Math.Clamp(preferred + change, Minimum, Maximum);
    }
}

public sealed class AmbientFilter
{
    private readonly double[] _samples = new double[3];
    private int _count, _next;
    private double? _value;
    private double? _target;
    public double Update(double light, double seconds)
    {
        if (!double.IsFinite(light) || !double.IsFinite(seconds) || seconds < 0) throw new ArgumentException("Invalid light sample.");
        _samples[_next] = Math.Clamp(light, 0, 255);
        _next = (_next + 1) % _samples.Length;
        _count = Math.Min(_count + 1, _samples.Length);
        Span<double> ordered = stackalloc double[3];
        _samples.AsSpan(0, _count).CopyTo(ordered);
        ordered = ordered[.._count];
        ordered.Sort();
        var median = ordered[_count / 2];
        _target ??= median;
        // Reject noise at the input, then finish moving to the accepted reading.
        // Applying the deadband to the smoothed value leaves a long, incomplete tail.
        if (Math.Abs(median - _target.Value) >= Math.Max(0.8, _target.Value * 0.03)) _target = median;
        _value ??= _target;
        _value += (_target.Value - _value.Value) * (1 - Math.Exp(-Math.Min(seconds, 0.5) / 0.12));
        if (Math.Abs(_target.Value - _value.Value) < 0.02) _value = _target;
        return _value.Value;
    }
}

public readonly record struct SceneLight(double Level, double DarkPixelFraction, double ClippedPixelFraction);

public static class LightMeter
{
    // Equal-sized regions give the room a vote independent of small lamps or foreground objects.
    // The median resists changes confined to fewer than half the regions. This is still
    // image luma, not illuminance: large scene changes and camera gain remain confounders.
    public static SceneLight MeasureSceneBgra(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (width < 5 || height < 5 || pixels.Length % 4 != 0 || (long)width * height != pixels.Length / 4)
            throw new ArgumentException("A tightly packed BGRA image at least 5 by 5 is required.");
        Span<double> regions = stackalloc double[25];
        Span<int> histogram = stackalloc int[256];
        var dark = 0; var clipped = 0;
        for (var row = 0; row < 5; row++)
        for (var column = 0; column < 5; column++)
        {
            histogram.Clear();
            var count = 0;
            for (var y = row * height / 5; y < (row + 1) * height / 5; y++)
            for (var x = column * width / 5; x < (column + 1) * width / 5; x++)
            {
                var offset = (y * width + x) * 4;
                var luma = Luma(pixels, offset);
                histogram[luma]++; count++;
                if (luma <= 3) dark++;
                if (luma >= 250) clipped++;
            }
            regions[row * 5 + column] = TrimmedMean(histogram, count);
        }
        regions.Sort();
        var pixelsCount = (double)width * height;
        return new SceneLight(regions[12], dark / pixelsCount, clipped / pixelsCount);
    }

    private static int Luma(ReadOnlySpan<byte> pixels, int offset) =>
        (29 * pixels[offset] + 150 * pixels[offset + 1] + 77 * pixels[offset + 2] + 128) >> 8;

    private static double TrimmedMean(ReadOnlySpan<int> histogram, int count)
    {
        var trim = count / 10;
        var lower = trim;
        var upper = count - trim;
        var seen = 0;
        long total = 0;
        var retained = 0;
        for (var value = 0; value < 256; value++)
        {
            var end = seen + histogram[value];
            var take = Math.Max(0, Math.Min(end, upper) - Math.Max(seen, lower));
            total += (long)value * take;
            retained += take;
            seen = end;
        }
        return (double)total / retained;
    }
}

public sealed class BrightnessRamp(double initial)
{
    public double Value { get; private set; } = initial;

    public int Advance(double target, double elapsedSeconds)
    {
        if (!double.IsFinite(target) || !double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentException("Invalid brightness target or elapsed time.");
        // Never jump after a stalled device call or suspended process.
        var dt = Math.Min(elapsedSeconds, 0.2);
        var tau = target > Value ? 0.22 : 0.28;
        var delta = (target - Value) * (1 - Math.Exp(-dt / tau));
        Value = Math.Clamp(Value + Math.Clamp(delta, -70 * dt, 70 * dt), 0, 100);
        return (int)Math.Round(Value);
    }
}
