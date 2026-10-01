using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Kaydence.Services;

// I swap the colour dictionary to switch between light and dark, and lay my accent colour on top
public static class ThemeService
{
    // I keep both text colours here so old text never gets stuck in the wrong theme colour
    public static readonly Color LightText = Color.FromRgb(0x2A, 0x24, 0x38);
    public static readonly Color DarkText = Color.FromRgb(0xEC, 0xE8, 0xF4);

    public static readonly IReadOnlyDictionary<string, Color> Accents = new Dictionary<string, Color>
    {
        ["Purple"] = Color.FromRgb(0x7B, 0x4F, 0xB8),
        ["Pink"] = Color.FromRgb(0xD6, 0x45, 0x7F),
        ["Blue"] = Color.FromRgb(0x2F, 0x6F, 0xE0),
        ["Teal"] = Color.FromRgb(0x12, 0x8C, 0x8C),
        ["Green"] = Color.FromRgb(0x2E, 0x8B, 0x57),
        ["Orange"] = Color.FromRgb(0xD9, 0x66, 0x1F)
    };

    private static string _accent = "Purple";

    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    public static void Apply(string mode)
    {
        IsDark = mode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => SystemPrefersDark()
        };

        var source = new Uri($"pack://application:,,,/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute);
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = source };
        ApplyAccentBrushes();

        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
        ThemeChanged?.Invoke();
        Log.Info("Theme", $"Theme {mode}, showing {(IsDark ? "dark" : "light")}");
    }

    public static void ApplyAccent(string name)
    {
        _accent = Accents.ContainsKey(name) ? name : "Purple";
        ApplyAccentBrushes();
        ThemeChanged?.Invoke();
    }

    // I leave purple to the theme files and only override the brushes for other accents
    private static void ApplyAccentBrushes()
    {
        var resources = Application.Current.Resources;
        if (_accent == "Purple")
        {
            resources.Remove("Brush.Accent");
            resources.Remove("Brush.AccentSoft");
            resources.Remove("Brush.Selection");
            return;
        }

        var colour = Accents[_accent];
        var accent = IsDark ? Mix(colour, Colors.White, 0.35) : colour;
        var background = IsDark ? Color.FromRgb(0x16, 0x14, 0x1C) : Colors.White;
        var soft = Mix(background, colour, IsDark ? 0.28 : 0.12);

        resources["Brush.Accent"] = Frozen(accent);
        resources["Brush.AccentSoft"] = Frozen(soft);
        resources["Brush.Selection"] = Frozen(accent);
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));

    private static SolidColorBrush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // I ask Windows to draw a dark title bar so dark mode looks right all the way up
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var value = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }
}
