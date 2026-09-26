namespace AutoBrightness.Core;

public interface IDisplayBrightness : IDisposable
{
    string Id { get; }
    string Name { get; }
    string Connection { get; }
    int Read();
    // Returns the representable level actually requested after hardware quantization.
    int Write(int percent);
}

public sealed record DisplayPlan(IDisplayBrightness Device, DisplayProfile Profile);
public sealed record DisplayReading(string Id, int? Brightness, string Status, double? Target = null,
    int? Observed = null, DisplayProfile? LearnedProfile = null);
public sealed record CycleResult(IReadOnlyList<DisplayReading> Displays);

// Single-worker coordinator. Device discovery/lifetime and camera ownership are outside it.
public sealed class BrightnessCoordinator
{
    private sealed class State(DisplayPlan plan)
    {
        public DisplayPlan Plan { get; set; } = plan;
        public BrightnessRamp? Ramp;
        public int? Applied;
        public TimeSpan LastRead;
        public TimeSpan LastTick;
        public TimeSpan RetryAt;
        public string Status = "Waiting";
        public bool Available;
        public int? Observed;
        public double? Target;
        public DisplayProfile? Learned;
        public readonly Queue<(int Level, TimeSpan Time)> RecentCommands = new();
        public int? ManualCandidate;
    }

    private readonly State[] _states;
    private readonly AmbientFilter _filter = new();
    private TimeSpan? _lastSample;
    public double FilteredLight { get; private set; }

    public BrightnessCoordinator(IEnumerable<DisplayPlan> plans)
    {
        _states = plans.Where(p => p.Profile.Enabled).Select(p =>
        {
            p.Profile.Validate();
            return new State(p);
        }).ToArray();
        if (_states.Length == 0) throw new ArgumentException("Select at least one supported display.");
    }

    public CycleResult Tick(double light, TimeSpan now, bool newLightSample = true)
    {
        if (!double.IsFinite(light) || light is < 0 or > 255) throw new ArgumentException("Invalid camera reading.");
        // The ramp runs faster than the camera. Do not count a reused frame twice
        // in the median filter, or a single flash could pass as sustained light.
        if (newLightSample || _lastSample is null)
        {
            FilteredLight = _filter.Update(light, _lastSample is { } previous ? Math.Max(0, (now - previous).TotalSeconds) : 0);
            _lastSample = now;
        }
        // Read all displays first, confirming native manual changes before learning them.
        foreach (var state in _states)
        {
            state.Learned = null;
            if (now < state.RetryAt) continue;
            try
            {
                if (state.Ramp is null)
                {
                    state.Applied = state.Plan.Device.Read();
                    state.Observed = state.Applied;
                    if (state.Plan.Profile.ReferenceLight is null || state.Plan.Profile.PreferredBrightness is null)
                    {
                        state.Learned = state.Plan.Profile.WithRoomReference(state.Applied.Value, FilteredLight);
                        state.Plan = state.Plan with { Profile = state.Learned };
                    }
                    state.Ramp = new BrightnessRamp(state.Applied.Value);
                    state.LastRead = state.LastTick = now;
                    RememberCommand(state, state.Applied.Value, now);
                    state.Status = "Ready";
                }
                else if (now - state.LastRead >= TimeSpan.FromSeconds(1))
                {
                    var actual = state.Plan.Device.Read();
                    state.Observed = actual;
                    state.LastRead = now;
                    while (state.RecentCommands.TryPeek(out var command) && now - command.Time > TimeSpan.FromSeconds(8))
                        state.RecentCommands.Dequeue();
                    var delayedReadback = state.RecentCommands.Any(command => Math.Abs(command.Level - actual) <= 2);
                    if (Math.Abs(actual - state.Applied!.Value) > 2 && !delayedReadback)
                    {
                        // DDC readback may lag writes. Confirm a new value before teaching it,
                        // and stop writing this device meanwhile so we do not fight the user.
                        if (state.ManualCandidate is not int candidate || Math.Abs(candidate - actual) > 2)
                        {
                            state.ManualCandidate = actual;
                            state.LastTick = now;
                            state.Status = "Checking manual change";
                            continue;
                        }
                        state.ManualCandidate = null;
                        state.Applied = actual;
                        state.Learned = state.Plan.Profile.WithRoomReference(actual, FilteredLight);
                        state.Plan = state.Plan with { Profile = state.Learned };
                        state.Ramp = new BrightnessRamp(actual);
                        state.LastTick = now;
                        state.Status = "Preference learned";
                        continue;
                    }
                    state.ManualCandidate = null;
                }
                state.Available = true;
            }
            catch (Exception ex) { Failed(state, now, ex); }
        }

        foreach (var state in _states)
        {
            if (!state.Available || now < state.RetryAt || state.Ramp is null) continue;
            if (state.ManualCandidate is not null) { state.LastTick = now; continue; }
            try
            {
                var elapsed = (now - state.LastTick).TotalSeconds;
                state.LastTick = now;
                state.Target = state.Plan.Profile.Target(FilteredLight);
                var requested = state.Ramp.Advance(state.Target.Value, elapsed);
                if (Math.Abs(requested - state.Applied!.Value) >= 1)
                {
                    // A level held for a long time can still be reported just after
                    // the first fast write. Keep it for the whole readback grace period.
                    RememberCommand(state, state.Applied.Value, now);
                    state.Applied = state.Plan.Device.Write(requested);
                    RememberCommand(state, state.Applied.Value, now);
                    state.Status = "Following camera";
                }
                else state.Status = state.Learned is not null ? "Preference learned" : "Stable";
            }
            catch (Exception ex) { Failed(state, now, ex); }
        }
        return Snapshot();
    }

    private static void Failed(State state, TimeSpan now, Exception error)
    {
        state.Available = false;
        state.RetryAt = now + TimeSpan.FromSeconds(10);
        state.Ramp = null;
        state.Applied = null;
        state.Observed = null;
        state.ManualCandidate = null;
        state.RecentCommands.Clear();
        state.Status = $"Retry in 10s: {error.Message}";
    }

    private static void RememberCommand(State state, int level, TimeSpan now)
    {
        state.RecentCommands.Enqueue((level, now));
        while (state.RecentCommands.Count > 256) state.RecentCommands.Dequeue();
    }

    private CycleResult Snapshot() => new(
        _states.Select(s => new DisplayReading(s.Plan.Device.Id, s.Applied, s.Status, s.Target, s.Observed, s.Learned)).ToArray());

    // Serialized with Tick by the host. A user-selected preference takes effect immediately.
    public DisplayProfile SetPreference(string id, int percent, double light)
    {
        var state = _states.Single(s => s.Plan.Device.Id == id);
        if (!double.IsFinite(light) || light < 0 || light > 255) throw new ArgumentException("Invalid light reading.");
        var actual = state.Plan.Device.Write(Math.Clamp(percent, 0, 100));
        var profile = state.Plan.Profile.WithRoomReference(actual, _lastSample is null ? light : FilteredLight);
        state.Plan = state.Plan with { Profile = profile };
        state.Applied = state.Observed = actual;
        state.ManualCandidate = null;
        RememberCommand(state, actual, _lastSample ?? TimeSpan.Zero);
        state.Ramp = new BrightnessRamp(actual);
        state.Status = "Preference saved";
        return profile;
    }
}
