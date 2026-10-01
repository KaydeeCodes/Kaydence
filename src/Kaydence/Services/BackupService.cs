using System.IO;
using System.IO.Compression;

namespace Kaydence.Services;

// I zip my whole diary once a day and keep as many days of copies as my settings say
public static class BackupService
{
    public static string DefaultFolder => Path.Combine(SettingsService.AppFolder, "Backups");

    public static void RunDailyInBackground(string dataRoot, string folder, int keepDays) => Task.Run(() =>
    {
        try
        {
            Run(dataRoot, folder, keepDays, false);
        }
        catch (Exception ex)
        {
            // I just try again next time I open the app if a backup fails
            Log.Warn("Backup", "Today's backup didn't work, I'll try again next time", ex);
        }
    });

    // I make a backup straight away when I press Back up now, even if there's already one today
    public static string RunNow(string dataRoot, string folder, int keepDays) => Run(dataRoot, folder, keepDays, true);

    // I swap my diary for a backup, but always zip up what I had first so nothing can be lost
    public static void Restore(string zipPath, string dataRoot, string backupFolder)
    {
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var looksRight = archive.Entries.Any(e =>
                e.FullName.Replace('\\', '/').StartsWith("entries/", StringComparison.OrdinalIgnoreCase)
                || e.FullName.Equals("index.json", StringComparison.OrdinalIgnoreCase));
            if (!looksRight) throw new InvalidDataException("That zip doesn't look like a Kaydence backup.");
            Log.Info("Backup", $"Restoring {Path.GetFileName(zipPath)}: {archive.Entries.Count} files, encrypted backup: {archive.Entries.Any(e => e.FullName == "keys.json")}");
        }

        Directory.CreateDirectory(backupFolder);
        if (Directory.Exists(dataRoot))
        {
            var safety = Path.Combine(backupFolder, $"Kaydence-before-restore-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(dataRoot, safety, CompressionLevel.Optimal, false);
            Log.Info("Backup", $"Safety copy made first: {Path.GetFileName(safety)}");
        }

        var temp = dataRoot + ".restoring";
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        ZipFile.ExtractToDirectory(zipPath, temp);

        var old = dataRoot + $".replaced-{DateTime.Now:yyyyMMddHHmmss}";
        if (Directory.Exists(dataRoot)) Directory.Move(dataRoot, old);
        Directory.Move(temp, dataRoot);
        Log.Info("Backup", "Backup swapped in");

        try
        {
            if (Directory.Exists(old)) Directory.Delete(old, true);
        }
        catch (Exception ex)
        {
            Log.Warn("Backup", "The replaced folder couldn't be deleted yet", ex);
            // I leave the old folder behind if Windows won't let me delete it yet, the safety zip has it anyway
        }
    }

    private static string Run(string dataRoot, string folder, int keepDays, bool force)
    {
        if (!Directory.Exists(dataRoot)) return "";
        Directory.CreateDirectory(folder);

        var name = force ? $"Kaydence-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip" : $"Kaydence-{DateTime.Now:yyyy-MM-dd}.zip";
        var target = Path.Combine(folder, name);
        if (File.Exists(target))
        {
            Log.Debug("Backup", $"Today's backup is already there: {Path.GetFileName(target)}");
            return target;
        }

        var temp = target + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        ZipFile.CreateFromDirectory(dataRoot, temp, CompressionLevel.Optimal, false);
        File.Move(temp, target);
        Log.Info("Backup", $"Made {Path.GetFileName(target)}, {new FileInfo(target).Length:N0} bytes");

        var cutoff = DateTime.Now.AddDays(-Math.Max(1, keepDays));
        foreach (var old in Directory.GetFiles(folder, "Kaydence-*.zip"))
            if (File.GetCreationTime(old) < cutoff)
            {
                File.Delete(old);
                Log.Info("Backup", $"Removed old backup {Path.GetFileName(old)}");
            }
        return target;
    }
}
