using System.IO;
using System.Text.Json;

namespace AutoBrightness.App;

public sealed class Settings
{
    public int Version { get; set; } = 3;
    public bool AutomaticEnabled { get; set; }
    public string? CameraId { get; set; }
    public double ExposureMilliseconds { get; set; } = 31.25;
    public Dictionary<string, DisplayProfile> Displays { get; set; } = [];
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoBrightness");
    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static Settings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Empty settings.");
            settings.Upgrade();
            settings.Validate();
            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Write("Load settings", ex);
            warning = "Saved settings could not be loaded. Defaults are shown; the original file is retained until you save.";
            return new Settings();
        }
    }

    public void Validate()
    {
        if (Version != 3 || Displays is null || !double.IsFinite(ExposureMilliseconds) || ExposureMilliseconds < 0.1 || ExposureMilliseconds > 1000)
            throw new ArgumentException("Invalid settings or exposure (valid range: 0.1–1000 ms).");
        foreach (var profile in Displays.Values) profile.Validate();
    }

    public void Upgrade()
    {
        if (Displays is null) throw new ArgumentException("Missing display preferences.");
        if (Version == 1)
        {
            Version = 2;
            AutomaticEnabled = false;
            Displays = Displays.ToDictionary(p => p.Key, p => p.Value.Minimum == 15 && p.Value.Maximum == 85
                ? new DisplayProfile(p.Value.Enabled) : p.Value);
        }
        if (Version == 2)
        {
            // Spatial metering changes the reference scale. Start from real display readback,
            // preserving comfort and bounds rather than moving to an obsolete reference.
            Displays = Displays.ToDictionary(p => p.Key, p => p.Value with { ReferenceLight = null });
            if (ExposureMilliseconds == 10) ExposureMilliseconds = 31.25;
            Version = 3;
        }
    }

    public void Save()
    {
        Validate();
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}

public static class AppLog
{
    private static readonly object Gate = new();
    public static void Write(string operation, Exception exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.DirectoryPath);
                var path = Path.Combine(Settings.DirectoryPath, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {operation}: {exception}\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
