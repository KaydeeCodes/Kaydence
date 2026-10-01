using System.IO;
using System.Text.Json;

namespace Kaydence.Services;

// I keep every choice from my settings window in here
public sealed class AppSettings
{
    public string DataFolder { get; set; } = Path.Combine(SettingsService.AppFolder, "Diary");

    public string Theme { get; set; } = "System";
    public string Accent { get; set; } = "Purple";
    public string PageStyle { get; set; } = "Plain";
    public double TextSize { get; set; } = 15;
    public bool SpellCheck { get; set; } = true;
    public bool WeekStartsMonday { get; set; } = true;
    public bool ShowMoodKey { get; set; } = true;
    public bool ShowTooltips { get; set; } = true;

    public bool CheckInOpen { get; set; } = true;
    public List<string> HiddenSections { get; set; } = new() { "Transition", "Water", "Caffeine", "Smoking", "Alcohol", "Cycle" };
    public List<string> SectionOrder { get; set; } = new();
    public int SettingsVersion { get; set; }
    public string WeightUnit { get; set; } = "kg";

    public bool StartWithWindows { get; set; }
    public bool StartHidden { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public bool CloseToTray { get; set; }
    public bool MinimiseToTray { get; set; }

    public bool PasswordEnabled { get; set; }
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public string LockMode { get; set; } = "Always";
    public int LockAfterDays { get; set; } = 3;
    public int IdleLockMinutes { get; set; }
    public bool LockWhenHidden { get; set; }
    public DateTime? LastUsed { get; set; }
    public string? RecoveryHash { get; set; }
    public string? RecoverySalt { get; set; }
    public DateTime? RecoveryCreated { get; set; }
    public string? DeviceKey { get; set; }
    public DateOnly? EncryptionOfferDeclined { get; set; }

    public bool ShowCounters { get; set; } = true;

    public bool ReminderEnabled { get; set; }
    public string ReminderTime { get; set; } = "20:00";
    public bool ReminderOnlyIfEmpty { get; set; } = true;
    public DateOnly? LastReminder { get; set; }

    public string WritingFont { get; set; } = "Segoe UI";
    public bool RoomySpacing { get; set; }
    public string Hotkey { get; set; } = "WinShiftK";

    public bool FirstRunDone { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string? SkippedVersion { get; set; }

    public bool AutoBackup { get; set; } = true;
    public int BackupKeepDays { get; set; } = 14;
    public string? BackupFolder { get; set; }

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Maximized { get; set; } = true;

    // I group the optional health sections so they can be switched on or off together
    public static readonly string[] HealthSections = { "Medications", "Symptoms", "BloodPressure", "Weight" };

    public bool ShowsAny(params string[] sections) => sections.Any(s => !HiddenSections.Contains(s));

    public void ShowSections(bool show, params string[] sections)
    {
        foreach (var section in sections)
        {
            HiddenSections.Remove(section);
            if (!show) HiddenSections.Add(section);
        }
    }

    public string BackupFolderOrDefault => string.IsNullOrWhiteSpace(BackupFolder) ? BackupService.DefaultFolder : BackupFolder;
}

// I keep settings in AppData\Local so nothing ends up synced to OneDrive by accident
public static class SettingsService
{
    public static string AppFolder { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kaydence");

    // I can run from a different folder, like a demo diary, so the real one is never touched
    public static void UseFolder(string folder) => AppFolder = Path.GetFullPath(folder);

    private static string FilePath => Path.Combine(AppFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = Upgrade(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings());
                _last = Snapshot(loaded);
                return loaded;
            }
            Log.Info("Settings", "No settings file yet, starting with the defaults");
        }
        catch (Exception ex)
        {
            // I fall back to defaults if the settings file ever gets damaged
            Log.Error("Settings", "The settings file couldn't be read, using the defaults", ex);
        }
        var fresh = Upgrade(new AppSettings());
        _last = Snapshot(fresh);
        return fresh;
    }

    // I switch new sections off for people who already had settings, so nothing appears they didn't ask for
    private static AppSettings Upgrade(AppSettings settings)
    {
        if (settings.SettingsVersion < 2) Log.Info("Settings", $"Upgrading settings from version {settings.SettingsVersion} to 2");
        if (settings.SettingsVersion < 2 && !settings.HiddenSections.Contains("Cycle")) settings.HiddenSections.Add("Cycle");
        settings.SettingsVersion = 2;
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
            LogChanges(settings);
        }
        catch (Exception ex)
        {
            // I never want a settings problem to stop the app working
            Log.Error("Settings", "Settings couldn't be saved", ex);
        }
    }

    // I never write secrets into the log, and I skip values that change constantly like the window position
    private static readonly HashSet<string> Secret = new() { "PasswordHash", "PasswordSalt", "RecoveryHash", "RecoverySalt", "DeviceKey" };
    private static readonly HashSet<string> Noisy = new() { "Left", "Top", "Width", "Height", "LastUsed", "LastUpdateCheck", "LastReminder" };
    private static Dictionary<string, string> _last = new();

    private static Dictionary<string, string> Snapshot(AppSettings settings)
    {
        var map = new Dictionary<string, string>();
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
            var value = property.GetValue(settings);
            var text = value switch
            {
                null => "(none)",
                System.Collections.IEnumerable list and not string => "[" + string.Join(", ", list.Cast<object>()) + "]",
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            map[property.Name] = Secret.Contains(property.Name) ? (value == null ? "(none)" : "(set)") : Log.Scrub(text);
        }
        return map;
    }

    // I describe the settings in one line for the start of each log, without anything secret
    public static string Summary(AppSettings settings) => string.Join(", ", Lines(settings));

    public static IEnumerable<string> Lines(AppSettings settings) =>
        Snapshot(settings).Where(p => !Noisy.Contains(p.Key)).Select(p => $"{p.Key}={p.Value}");

    private static void LogChanges(AppSettings settings)
    {
        var now = Snapshot(settings);
        var changes = now.Where(p => !Noisy.Contains(p.Key) && (!_last.TryGetValue(p.Key, out var old) || old != p.Value))
            .Select(p => $"{p.Key}: {(_last.TryGetValue(p.Key, out var old) ? old : "(new)")} to {p.Value}")
            .ToList();
        if (changes.Count > 0) Log.Info("Settings", "Changed " + string.Join("; ", changes));
        _last = now;
    }
}
