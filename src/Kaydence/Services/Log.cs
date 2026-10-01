using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Kaydence.Services;

// I keep a detailed record of what the app does so a bug report points me to the right place, but never anything I wrote, felt or recorded
public static class Log
{
    private const int KeepDays = 14;
    private const long MaxBytesPerDay = 20 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static DateOnly? _fullOn;

    public static string Folder => Path.Combine(SettingsService.AppFolder, "Logs");

    public static string TodayFile => Path.Combine(Folder, $"kaydence-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Debug(string area, string message) => Write("DEBUG", area, message, null);

    public static void Info(string area, string message) => Write("INFO", area, message, null);

    public static void Warn(string area, string message, Exception? ex = null) => Write("WARN", area, message, ex);

    public static void Error(string area, string message, Exception? ex = null) => Write("ERROR", area, message, ex);

    // I time a block of work and note how long it took when it finishes
    public static IDisposable Time(string area, string what) => new Timer(area, what);

    private sealed class Timer : IDisposable
    {
        private readonly string _area;
        private readonly string _what;
        private readonly long _start = Clock.ElapsedMilliseconds;

        public Timer(string area, string what)
        {
            _area = area;
            _what = what;
        }

        public void Dispose() => Debug(_area, $"{_what} took {Clock.ElapsedMilliseconds - _start} ms");
    }

    // I write a header each time Kaydence starts with everything about the PC that could matter for a bug
    public static void StartSession(string[] args)
    {
        try
        {
            WriteHeader(args);
        }
        catch (Exception ex)
        {
            // I never let the header stop Kaydence starting
            Error("App", "Couldn't write the session header", ex);
        }
    }

    private static void WriteHeader(string[] args)
    {
        Tidy();
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var info = new StringBuilder();
        info.AppendLine();
        info.AppendLine("==================================================================");
        info.AppendLine($"Kaydence session started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        info.AppendLine($"  App version        {version}");
        info.AppendLine($"  Build              {BuildKind()}");
        info.AppendLine($"  Runtime            {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
        info.AppendLine($"  Windows            {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {Environment.OSVersion.VersionString}");
        info.AppendLine($"  64 bit             OS {Environment.Is64BitOperatingSystem}, process {Environment.Is64BitProcess}");
        info.AppendLine($"  Processors         {Environment.ProcessorCount}");
        info.AppendLine($"  Memory in use      {Environment.WorkingSet / 1024 / 1024} MB");
        info.AppendLine($"  Culture            {CultureInfo.CurrentCulture.Name}, UI {CultureInfo.CurrentUICulture.Name}");
        info.AppendLine($"  Time zone          {TimeZoneInfo.Local.Id} (UTC{TimeZoneInfo.Local.GetUtcOffset(DateTime.Now):hh\\:mm})");
        info.AppendLine($"  Screen             {SystemParameters.PrimaryScreenWidth} x {SystemParameters.PrimaryScreenHeight} (virtual {SystemParameters.VirtualScreenWidth} x {SystemParameters.VirtualScreenHeight})");
        info.AppendLine($"  Graphics tier      {RenderCapability.Tier >> 16}");
        info.AppendLine($"  High contrast      {SystemParameters.HighContrast}");
        info.AppendLine($"  Started with       {(args.Length == 0 ? "no arguments" : string.Join(" ", args))}");
        info.AppendLine("==================================================================");
        Append(info.ToString());
    }

    public static void EndSession(string reason) => Info("App", $"Session ended ({reason}), ran for {Clock.Elapsed:hh\\:mm\\:ss}");

    private static string BuildKind()
    {
#if DEBUG
        const string config = "Debug";
#else
        const string config = "Release";
#endif
        var singleFile = string.IsNullOrEmpty(Assembly.GetExecutingAssembly().Location) ? ", single file" : "";
        return config + singleFile;
    }

    private static void Write(string level, string area, string message, Exception? ex)
    {
        var thread = Environment.CurrentManagedThreadId;
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(' ').Append(level.PadRight(5))
            .Append(" [").Append(thread.ToString(CultureInfo.InvariantCulture).PadLeft(2)).Append("] ")
            .Append('[').Append(area).Append("] ")
            .Append(Scrub(message));
        if (ex != null) line.AppendLine().Append("    ").Append(Scrub(ex.ToString()).Replace(Environment.NewLine, Environment.NewLine + "    "));
        line.AppendLine();
        Append(line.ToString());
    }

    // I swap my own user folder paths for placeholders so my Windows user name never ends up in a log
    public static string Scrub(string text)
    {
        foreach (var (folder, name) in Placeholders)
        {
            if (!string.IsNullOrEmpty(folder)) text = text.Replace(folder, name, StringComparison.OrdinalIgnoreCase);
        }
        // I only hide the user name inside a Users path, so ordinary words that happen to contain it stay readable
        return UserPath.Replace(text, @"\Users\<user>");
    }

    private static readonly Regex UserPath = new(@"\\Users\\[^\\/:*?""<>|\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (string Folder, string Name)[] Placeholders =
    {
        (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LocalAppData%"),
        (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%AppData%"),
        (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%UserProfile%")
    };

    private static void Append(string text)
    {
        lock (Gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (_fullOn == today) return;
                Directory.CreateDirectory(Folder);
                var file = TodayFile;
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytesPerDay)
                {
                    _fullOn = today;
                    File.AppendAllText(file, $"{DateTimeOffset.Now:u} Log for today is full, I'll carry on tomorrow{Environment.NewLine}");
                    return;
                }
                File.AppendAllText(file, text);
            }
            catch (Exception)
            {
                // I never let logging cause a problem of its own
            }
        }
    }

    // I keep two weeks of logs and quietly remove older ones
    private static void Tidy()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder, "kaydence-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-KeepDays)) File.Delete(file);
            }
        }
        catch (Exception)
        {
            // I'll tidy up another day
        }
    }

    // I describe a file without its contents, just its name, size and whether it's encrypted
    public static string Describe(string path)
    {
        try
        {
            if (!File.Exists(path)) return $"{Path.GetFileName(path)} (missing)";
            var size = new FileInfo(path).Length;
            return $"{Path.GetFileName(path)} ({size:N0} bytes, {(CryptoService.IsSealedFile(path) ? "encrypted" : "plain")})";
        }
        catch (Exception ex)
        {
            return $"{Path.GetFileName(path)} (couldn't check: {ex.GetType().Name})";
        }
    }
}
