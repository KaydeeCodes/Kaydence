using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Kaydence.Services;

// I zip up the last few days of logs and a summary of the settings so a bug report has everything I need, and never anything from the diary
public static class BugReport
{
    private const int Days = 3;

    public static string SuggestedName => $"Kaydence-bug-report-{DateTime.Now:yyyy-MM-dd-HHmm}.zip";

    public static void Create(string zipPath, AppSettings settings, string diaryRoot)
    {
        Log.Info("BugReport", "Making a bug report zip");
        var temp = zipPath + ".tmp";
        try
        {
            using var file = new FileStream(temp, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(file, ZipArchiveMode.Create);
            AddText(zip, "about.txt", About(settings, diaryRoot));
            var logs = Directory.Exists(Log.Folder)
                ? Directory.GetFiles(Log.Folder, "kaydence-*.log").OrderByDescending(f => f).Take(Days).ToList()
                : new List<string>();
            foreach (var log in logs) AddFile(zip, log);
            Log.Info("BugReport", $"Added {logs.Count} log files");
        }
        catch
        {
            // I don't leave a half made zip lying around
            File.Delete(temp);
            throw;
        }
        File.Move(temp, zipPath, true);
        Log.Info("BugReport", $"Bug report saved, {new FileInfo(zipPath).Length:N0} bytes");
    }

    private static string About(AppSettings settings, string diaryRoot)
    {
        var text = new StringBuilder();
        text.AppendLine("Kaydence bug report");
        text.AppendLine("This file and the logs beside it never contain anything written, felt or recorded in the diary.");
        text.AppendLine();
        text.AppendLine($"Made           {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        text.AppendLine($"Kaydence       {Assembly.GetExecutingAssembly().GetName().Version}");
        text.AppendLine($"Windows        {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine($"Runtime        {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"Culture        {CultureInfo.CurrentCulture.Name}");
        text.AppendLine($"Encrypted      {KeyStore.Exists(diaryRoot)}");
        try
        {
            text.AppendLine();
            text.AppendLine("Space used");
            text.AppendLine(DiskUsage.Describe(DiskUsage.Measure(diaryRoot, settings.BackupFolderOrDefault)));
        }
        catch (Exception ex)
        {
            text.AppendLine($"Couldn't measure the space used: {ex.GetType().Name}");
        }
        text.AppendLine();
        text.AppendLine("Settings (passwords, keys and hashes are only shown as set or none)");
        foreach (var line in SettingsService.Lines(settings)) text.AppendLine("  " + line);
        return text.ToString();
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    // I read logs with sharing switched on because today's log is still being written to
    private static void AddFile(ZipArchive zip, string path)
    {
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = zip.CreateEntry(Path.GetFileName(path)).Open();
            source.CopyTo(target);
        }
        catch (Exception ex)
        {
            Log.Warn("BugReport", $"Couldn't add {Path.GetFileName(path)}", ex);
        }
    }
}
