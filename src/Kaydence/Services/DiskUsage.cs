using System.Globalization;
using System.IO;

namespace Kaydence.Services;

// I add up how much space my diary, backups and logs take, just so I know
public static class DiskUsage
{
    public sealed record Usage(
        long Pictures, int PictureCount,
        long Voice, int VoiceCount,
        long Pages, int Days,
        long Deleted, long Other,
        long Backups, int BackupCount,
        long Logs,
        string Drive, long? DriveFree)
    {
        public long Diary => Pictures + Voice + Pages + Deleted + Other;
    }

    public static Usage Measure(string root, string backupFolder)
    {
        long pictures = 0, voice = 0, pages = 0, deleted = 0, other = 0;
        int pictureCount = 0, voiceCount = 0;
        var days = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletedRoot = Path.Combine(root, "deleted") + Path.DirectorySeparatorChar;

        foreach (var file in Files(root))
        {
            var size = SizeOf(file);
            var name = Path.GetFileName(file);
            if (file.StartsWith(deletedRoot, StringComparison.OrdinalIgnoreCase)) deleted += size;
            else if (name.StartsWith("img-", StringComparison.OrdinalIgnoreCase))
            {
                pictures += size;
                pictureCount++;
            }
            else if (name.StartsWith("voice-", StringComparison.OrdinalIgnoreCase))
            {
                voice += size;
                voiceCount++;
            }
            else if (name.Equals("day.json", StringComparison.OrdinalIgnoreCase) || name.Equals("ink.isf", StringComparison.OrdinalIgnoreCase))
            {
                pages += size;
                days.Add(Path.GetDirectoryName(file) ?? "");
            }
            else other += size;
        }

        long backups = 0;
        var backupCount = 0;
        if (Directory.Exists(backupFolder))
        {
            foreach (var zip in Directory.EnumerateFiles(backupFolder, "Kaydence-*.zip"))
            {
                backups += SizeOf(zip);
                backupCount++;
            }
        }

        var logs = Files(Log.Folder).Sum(f => SizeOf(f));

        string drive = "";
        long? free = null;
        try
        {
            var info = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? root);
            drive = info.Name.TrimEnd('\\');
            free = info.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            Log.Warn("Space", "Couldn't check the free space on the drive", ex);
        }

        var usage = new Usage(pictures, pictureCount, voice, voiceCount, pages, days.Count, deleted, other, backups, backupCount, logs, drive, free);
        Log.Info("Space", $"Diary {Format(usage.Diary)} (pictures {Format(pictures)} in {pictureCount}, voice notes {Format(voice)} in {voiceCount}, " +
                          $"pages {Format(pages)} over {days.Count} days, deleted {Format(deleted)}, other {Format(other)}), " +
                          $"backups {Format(backups)} in {backupCount}, logs {Format(logs)}, free {(free is { } f ? Format(f) : "unknown")}");
        return usage;
    }

    // I describe it in a few short lines for the settings window
    public static string Describe(Usage u) =>
        $"Your diary: {Format(u.Diary)}\n" +
        $"Pictures {Format(u.Pictures)} ({u.PictureCount:N0}), voice notes {Format(u.Voice)} ({u.VoiceCount:N0}), " +
        $"pages and drawings {Format(u.Pages)} ({u.Days:N0} {(u.Days == 1 ? "day" : "days")})" +
        (u.Deleted > 0 ? $", deleted days {Format(u.Deleted)}" : "") + "\n" +
        $"Backups: {Format(u.Backups)} in {u.BackupCount:N0} {(u.BackupCount == 1 ? "zip" : "zips")}\n" +
        $"Logs: {Format(u.Logs)}" +
        (u.DriveFree is { } free ? $"\nFree space on {u.Drive}: {Format(free)}" : "");

    public static string Format(long bytes)
    {
        string[] units = { "bytes", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} bytes" : $"{value.ToString(value < 10 ? "0.#" : "0", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    private static IEnumerable<string> Files(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList()
                : Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            Log.Warn("Space", $"Couldn't list everything in {folder}", ex);
            return Enumerable.Empty<string>();
        }
    }

    private static long SizeOf(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception)
        {
            // I just count a file I can't read as nothing
            return 0;
        }
    }
}
