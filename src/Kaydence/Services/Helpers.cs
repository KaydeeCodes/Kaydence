using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kaydence.Models;
using Microsoft.Win32;

namespace Kaydence.Services;

// I open web links in my default browser
public static class Links
{
    public const string Website = "https://kaydee.codes";
    public const string GitHubOwner = "KaydeeCodes";
    public const string GitHubRepo = "Kaydence";
    public const string Releases = "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases";

    // I open the matching GitHub issue form with the version filled in, and never anything from the diary
    public static void NewIssue(bool bug)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        var url = $"https://github.com/{GitHubOwner}/{GitHubRepo}/issues/new?template={(bug ? "bug_report.yml" : "feature_request.yml")}";
        if (bug) url += $"&version={Uri.EscapeDataString(version)}&windows={Uri.EscapeDataString(Environment.OSVersion.VersionString)}";
        Open(url);
    }

    public static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            Log.Info("Links", $"Opened {(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(url).GetLeftPart(UriPartial.Path) : url)}");
        }
        catch (Exception ex)
        {
            Log.Warn("Links", "Windows couldn't open a link", ex);
            // I just do nothing if Windows can't open a browser
        }
    }
}

// I turn a day into plain text for snippets, search and printing
public static class DayText
{
    public static string Get(DayEntry entry)
    {
        if (entry.Text != null) return entry.Text;
        var parts = entry.Items
            .OrderBy(i => i.Y).ThenBy(i => i.X)
            .Select(ItemText)
            .Where(t => t.Length > 0);
        return string.Join("\n\n", parts);
    }

    // I turn one thing on my page into plain words, whether it's writing, a sticky note or a checklist
    public static string ItemText(PageItem item) => item.Kind switch
    {
        "text" or "sticky" when !string.IsNullOrEmpty(item.Xaml) => FromXaml(item.Xaml!),
        "checklist" when item.Checks != null => ChecklistText(item.Checks),
        "voice" => $"(Voice note, {TimeSpan.FromSeconds(item.Duration ?? 0):m\\:ss})",
        _ => ""
    };

    public static string ChecklistText(IEnumerable<CheckEntry> checks) => string.Join("\n", checks
        .Where(c => !string.IsNullOrWhiteSpace(c.Text))
        .Select(c => (c.Done ? "[x] " : "[ ] ") + c.Text.Trim()));

    public static string FromXaml(string xaml)
    {
        try
        {
            var document = new FlowDocument();
            var range = new TextRange(document.ContentStart, document.ContentEnd);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
            range.Load(stream, DataFormats.Xaml);
            return range.Text.Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static string Snippet(string text, int max)
    {
        var flat = Regex.Replace(text, @"\s+", " ").Trim();
        return flat.Length > max ? flat[..max].TrimEnd() + "..." : flat;
    }

    public static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    // I search everything I might have typed that day, not just the page
    public static string SearchText(DayEntry entry)
    {
        var c = entry.CheckIn;
        var bits = new List<string?>
        {
            Get(entry), c.MoodNote, c.Note, c.MedsOther, c.Transition.Hrt, c.Transition.InjectionSite,
            c.Transition.Milestone, c.Symptoms.Notes, c.Sleep.Notes, c.Fitness.Activity, c.Struggles, c.Wins
        };
        bits.AddRange(c.Tasks.Select(t => t.Text));
        bits.Add(Kaydence.Controls.BodyMap.Describe(c.Symptoms.Areas));
        bits.AddRange(c.Medications.Select(m => m.Name));
        return string.Join("\n", bits.Where(b => !string.IsNullOrWhiteSpace(b)));
    }

    public static IEnumerable<string> ImageNames(DayEntry entry) =>
        entry.Items.Where(i => i.Kind == "image" && !string.IsNullOrEmpty(i.Image))
            .OrderBy(i => i.Y).ThenBy(i => i.X)
            .Select(i => i.Image!);
}

// I convert weights so I can use kg, stone and pounds, or just pounds
public static class Units
{
    public const double PoundsPerKg = 2.20462262185;

    public static string FormatWeight(double kg, string unit)
    {
        switch (unit)
        {
            case "lb":
                return $"{kg * PoundsPerKg:0.#} lb";
            case "st":
                var (stone, pounds) = ToStone(kg);
                return $"{stone} st {pounds:0.#} lb";
            default:
                return $"{kg:0.#} kg";
        }
    }

    public static (int Stone, double Pounds) ToStone(double kg)
    {
        var totalPounds = kg * PoundsPerKg;
        var stone = (int)Math.Floor(totalPounds / 14);
        var pounds = Math.Round(totalPounds - stone * 14, 1);
        if (pounds >= 14)
        {
            stone++;
            pounds -= 14;
        }
        return (stone, pounds);
    }

    public static double FromStone(double stone, double pounds) => (stone * 14 + pounds) / PoundsPerKg;

    public static double FromPounds(double pounds) => pounds / PoundsPerKg;

    // I accept numbers typed either way, like 7.5 or 7,5
    public static bool TryParse(string text, out double value)
    {
        text = text.Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
               || double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

// I load pictures fully into memory so files never get locked, unsealing them first if my diary is encrypted
public static class ImageHelper
{
    // I open a picture at full size for editing, turned the right way up
    public static BitmapSource Frame(string path)
    {
        var bytes = CryptoService.ReadBytes(path);
        BitmapFrame frame;
        try
        {
            frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Log.Debug("Pictures", $"Opened {Path.GetFileName(path)} full size, {frame.PixelWidth} x {frame.PixelHeight}");
        }
        catch (Exception ex)
        {
            // I try again ignoring the colour profile, some cameras and apps write ones WPF can't read
            Log.Warn("Pictures", $"Couldn't open {Path.GetFileName(path)} full size, trying without its colour profile", ex);
            frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            Log.Info("Pictures", $"Opened {Path.GetFileName(path)} without its colour profile, {frame.PixelWidth} x {frame.PixelHeight}");
        }
        return Upright(frame, Orientation(frame), Path.GetFileName(path));
    }

    // I try the normal way first, then a plainer way if that fails, and note every step so a failure is easy to track down
    public static ImageSource? Load(string path, int decodeWidth)
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            var folder = Path.GetDirectoryName(path);
            var nearby = folder != null && Directory.Exists(folder)
                ? string.Join(", ", Directory.GetFiles(folder).Select(f => Path.GetFileName(f)))
                : "folder doesn't exist";
            Log.Warn("Pictures", $"Can't show {name}, the file isn't there. The day folder holds: {nearby}");
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = CryptoService.ReadBytes(path);
        }
        catch (Exception ex)
        {
            Log.Error("Pictures", $"Couldn't read {Log.Describe(path)}. Diary key loaded: {CryptoService.Key != null}", ex);
            return null;
        }
        Log.Debug("Pictures", $"Read {name}: {bytes.Length:N0} bytes after unsealing, starts {Signature(bytes)}, wanted width {(decodeWidth > 0 ? decodeWidth.ToString(CultureInfo.InvariantCulture) : "full")}");
        var orientation = Orientation(bytes);

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // I limit the height instead for a sideways photo, because its height becomes the width once it's turned
            if (decodeWidth > 0 && orientation >= 5) bitmap.DecodePixelHeight = decodeWidth;
            else if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            Log.Debug("Pictures", $"Loaded {name} at {bitmap.PixelWidth} x {bitmap.PixelHeight}");
            return Upright(bitmap, orientation, name);
        }
        catch (Exception ex)
        {
            Log.Warn("Pictures", $"The usual way couldn't load {name}, trying a plainer way", ex);
        }

        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(bytes),
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (decodeWidth > 0 && frame.PixelWidth > decodeWidth)
            {
                var scale = decodeWidth / (double)frame.PixelWidth;
                frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            }
            frame.Freeze();
            Log.Info("Pictures", $"Loaded {name} the plainer way with {decoder.GetType().Name}, {frame.PixelWidth} x {frame.PixelHeight}");
            return Upright(frame, orientation, name);
        }
        catch (Exception ex)
        {
            Log.Error("Pictures", $"Couldn't load {name} either way. {bytes.Length:N0} bytes starting {Signature(bytes)}", ex);
            return null;
        }
    }

    // I read which way up a camera says its photo should be shown, 1 means it's already upright
    public static int Orientation(byte[] bytes)
    {
        // I only look inside JPEG and TIFF files, those are the ones cameras and phones tag
        var head = Convert.ToHexString(bytes, 0, Math.Min(4, bytes.Length));
        if (!head.StartsWith("FFD8FF", StringComparison.Ordinal) && head != "49492A00" && head != "4D4D002A") return 1;
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            return Orientation(decoder.Frames[0]);
        }
        catch (Exception ex)
        {
            Log.Debug("Pictures", $"Couldn't read which way up the picture goes: {ex.GetType().Name}");
            return 1;
        }
    }

    private static int Orientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value and >= 1 and <= 8 ? value : 1;
        }
        catch (Exception)
        {
            // I treat a picture with no readable tag as already upright
            return 1;
        }
    }

    // I turn and flip a picture the way its camera tag asks, the same as the Photos app does
    public static BitmapSource Upright(BitmapSource source, int orientation, string name)
    {
        if (orientation <= 1 || orientation > 8) return source;
        var steps = new TransformGroup();
        switch (orientation)
        {
            case 2:
                steps.Children.Add(new ScaleTransform(-1, 1));
                break;
            case 3:
                steps.Children.Add(new RotateTransform(180));
                break;
            case 4:
                steps.Children.Add(new ScaleTransform(1, -1));
                break;
            case 5:
                steps.Children.Add(new RotateTransform(90));
                steps.Children.Add(new ScaleTransform(-1, 1));
                break;
            case 6:
                steps.Children.Add(new RotateTransform(90));
                break;
            case 7:
                steps.Children.Add(new RotateTransform(90));
                steps.Children.Add(new ScaleTransform(1, -1));
                break;
            case 8:
                steps.Children.Add(new RotateTransform(270));
                break;
        }
        try
        {
            var turned = new TransformedBitmap(source, steps);
            turned.Freeze();
            Log.Debug("Pictures", $"Turned {name} upright from camera orientation {orientation}, now {turned.PixelWidth} x {turned.PixelHeight}");
            return turned;
        }
        catch (Exception ex)
        {
            Log.Warn("Pictures", $"Couldn't turn {name} upright, showing it as it is", ex);
            return source;
        }
    }

    // I make a clean copy of a picture for my diary: turned upright, with location, camera and date details left behind
    public static (byte[] Bytes, string Extension) CleanForDiary(byte[] original, string extension)
    {
        var kind = Signature(original);
        // I keep GIFs as they are so animations still move, and BMPs never carry any details
        if (kind.EndsWith("(GIF)", StringComparison.Ordinal) || kind.EndsWith("(BMP)", StringComparison.Ordinal))
        {
            Log.Debug("Pictures", $"Keeping the {extension} picture as it is, it has nothing hidden inside");
            return (original, extension);
        }

        var jpeg = kind.EndsWith("(JPEG)", StringComparison.Ordinal);
        foreach (var options in new[] { BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCreateOptions.IgnoreColorProfile })
        {
            try
            {
                var decoder = BitmapDecoder.Create(new MemoryStream(original), options, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                var orientation = Orientation(frame);
                // I copy the pixels into a plain bitmap first so nothing from the original file can come along with them
                var pixels = new CachedBitmap(Upright(frame, orientation, "the new picture"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var cleaned = Encode(pixels, ColoursOf(frame), jpeg) ?? Encode(pixels, null, jpeg);
                if (cleaned == null) continue;
                Log.Info("Pictures", $"Made a clean copy: {original.Length:N0} bytes in, {cleaned.Length:N0} bytes out as {(jpeg ? "JPEG" : "PNG")}, " +
                                     $"camera orientation {orientation}, any location and camera details left behind");
                return (cleaned, jpeg ? ".jpg" : ".png");
            }
            catch (Exception ex)
            {
                Log.Warn("Pictures", $"Couldn't make a clean copy of the {extension} picture with {options}", ex);
            }
        }

        // I'd rather keep the picture than lose it, so I fall back to the file exactly as it came
        Log.Warn("Pictures", $"Keeping the {extension} picture exactly as it came, any hidden details are still inside it");
        return (original, extension);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<ColorContext>? ColoursOf(BitmapFrame frame)
    {
        try
        {
            return frame.ColorContexts;
        }
        catch (Exception)
        {
            // I just go without the colour profile if it can't be read
            return null;
        }
    }

    // I write the pixels into a brand new file with no metadata, only the colour profile so colours stay true
    private static byte[]? Encode(BitmapSource source, System.Collections.ObjectModel.ReadOnlyCollection<ColorContext>? colours, bool jpeg)
    {
        try
        {
            BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source, null, null, colours));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug("Pictures", $"Encoding {(colours == null ? "without" : "with")} the colour profile didn't work: {ex.GetType().Name}");
            return null;
        }
    }

    // I describe the first few bytes so I can tell a PNG from a JPEG from something broken, without looking at the picture
    public static string Signature(byte[] bytes)
    {
        var head = Convert.ToHexString(bytes, 0, Math.Min(8, bytes.Length));
        var kind = head.StartsWith("89504E47", StringComparison.Ordinal) ? "PNG"
            : head.StartsWith("FFD8FF", StringComparison.Ordinal) ? "JPEG"
            : head.StartsWith("47494638", StringComparison.Ordinal) ? "GIF"
            : head.StartsWith("424D", StringComparison.Ordinal) ? "BMP"
            : head.StartsWith("4B444531", StringComparison.Ordinal) ? "still encrypted"
            : "unknown";
        return $"{head} ({kind})";
    }
}

// I never store my password, only a salted hash of it
public static class PasswordService
{
    private const int Iterations = 200_000;

    public static (string Hash, string Salt) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string? hash, string? salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;
        try
        {
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

// I add or remove myself from the Windows sign in list
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string StartupArgument = "--startup";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } path) key.SetValue("Kaydence", $"\"{path}\" {StartupArgument}");
            else key.DeleteValue("Kaydence", false);
            Log.Info("Startup", $"Start with Windows is now {(enabled ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "Couldn't change the start with Windows entry", ex);
            // I just carry on if Windows won't let me change the sign in list
        }
    }
}
