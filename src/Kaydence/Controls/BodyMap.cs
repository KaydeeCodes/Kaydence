using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kaydence.Controls;

// I draw a simple front and back body made of separate areas, so pain can be marked where it is and shown as a heatmap later
public sealed class BodyMap : UserControl
{
    public sealed record Region(string Key, string Label, bool Back, string Data);

    // I keep every area in a 120 by 300 box per figure, and the same key on the front and back means the same place
    public static readonly IReadOnlyList<Region> Regions = new Region[]
    {
        new("head", "Head", false, "M46,24 A14,18 0 1 1 74,24 A14,18 0 1 1 46,24 Z"),
        new("neck", "Neck", false, "M57,43 L63,43 Q66,43 66,46 L66,51 Q66,54 63,54 L57,54 Q54,54 54,51 L54,46 Q54,43 57,43 Z"),
        new("shoulder-right", "Right shoulder", false, "M22.5,63 A6.5,8.5 0 1 1 35.5,63 A6.5,8.5 0 1 1 22.5,63 Z"),
        new("shoulder-left", "Left shoulder", false, "M 97.5 63 A 6.5 8.5 0 1 0 84.5 63 A 6.5 8.5 0 1 0 97.5 63 Z"),
        new("upper-arm-right", "Right upper arm", false, "M29,75 L30,75 Q36,75 36,81 L36,107 Q36,113 30,113 L29,113 Q23,113 23,107 L23,81 Q23,75 29,75 Z"),
        new("upper-arm-left", "Left upper arm", false, "M 91 75 L 90 75 Q 84 75 84 81 L 84 107 Q 84 113 90 113 L 91 113 Q 97 113 97 107 L 97 81 Q 97 75 91 75 Z"),
        new("forearm-right", "Right forearm and elbow", false, "M27,116 L28,116 Q34,116 34,122 L34,148 Q34,154 28,154 L27,154 Q21,154 21,148 L21,122 Q21,116 27,116 Z"),
        new("forearm-left", "Left forearm and elbow", false, "M 93 116 L 92 116 Q 86 116 86 122 L 86 148 Q 86 154 92 154 L 93 154 Q 99 154 99 148 L 99 122 Q 99 116 93 116 Z"),
        new("hand-right", "Right hand", false, "M20,165 A7,9.5 0 1 1 34,165 A7,9.5 0 1 1 20,165 Z"),
        new("hand-left", "Left hand", false, "M 100 165 A 7 9.5 0 1 0 86 165 A 7 9.5 0 1 0 100 165 Z"),
        new("thigh-right", "Right thigh", false, "M49,160 L50.5,160 Q58.5,160 58.5,168 L58.5,205 Q58.5,213 50.5,213 L49,213 Q41,213 41,205 L41,168 Q41,160 49,160 Z"),
        new("thigh-left", "Left thigh", false, "M 71 160 L 69.5 160 Q 61.5 160 61.5 168 L 61.5 205 Q 61.5 213 69.5 213 L 71 213 Q 79 213 79 205 L 79 168 Q 79 160 71 160 Z"),
        new("knee-right", "Right knee", false, "M41.5,221 A8,6.5 0 1 1 57.5,221 A8,6.5 0 1 1 41.5,221 Z"),
        new("knee-left", "Left knee", false, "M 78.5 221 A 8 6.5 0 1 0 62.5 221 A 8 6.5 0 1 0 78.5 221 Z"),
        new("lower-leg-right", "Right lower leg", false, "M48.5,229 L50.5,229 Q56.5,229 56.5,235 L56.5,270 Q56.5,276 50.5,276 L48.5,276 Q42.5,276 42.5,270 L42.5,235 Q42.5,229 48.5,229 Z"),
        new("lower-leg-left", "Left lower leg", false, "M 71.5 229 L 69.5 229 Q 63.5 229 63.5 235 L 63.5 270 Q 63.5 276 69.5 276 L 71.5 276 Q 77.5 276 77.5 270 L 77.5 235 Q 77.5 229 71.5 229 Z"),
        new("foot-right", "Right foot", false, "M39.5,284 A9.5,5.5 0 1 1 58.5,284 A9.5,5.5 0 1 1 39.5,284 Z"),
        new("foot-left", "Left foot", false, "M 80.5 284 A 9.5 5.5 0 1 0 61.5 284 A 9.5 5.5 0 1 0 80.5 284 Z"),
        new("chest", "Chest", false, "M47,56 L73,56 Q81,56 83.4,64 L79.6,89 Q78,97 70,97 L50,97 Q42,97 40.4,89 L36.6,64 Q39,56 47,56 Z"),
        new("stomach", "Stomach", false, "M48,99 L72,99 Q78,99 78,105 L78,122 Q78,128 72,128 L48,128 Q42,128 42,122 L42,105 Q42,99 48,99 Z"),
        new("pelvis", "Pelvis and hips", false, "M48,130 L72,130 Q79,130 81.1,137 L75.4,150 Q74,157 67,157 L53,157 Q46,157 44.6,150 L38.9,137 Q41,130 48,130 Z"),
        new("head", "Head", true, "M46,24 A14,18 0 1 1 74,24 A14,18 0 1 1 46,24 Z"),
        new("neck", "Neck", true, "M57,43 L63,43 Q66,43 66,46 L66,51 Q66,54 63,54 L57,54 Q54,54 54,51 L54,46 Q54,43 57,43 Z"),
        new("shoulder-left", "Left shoulder", true, "M22.5,63 A6.5,8.5 0 1 1 35.5,63 A6.5,8.5 0 1 1 22.5,63 Z"),
        new("shoulder-right", "Right shoulder", true, "M 97.5 63 A 6.5 8.5 0 1 0 84.5 63 A 6.5 8.5 0 1 0 97.5 63 Z"),
        new("upper-arm-left", "Left upper arm", true, "M29,75 L30,75 Q36,75 36,81 L36,107 Q36,113 30,113 L29,113 Q23,113 23,107 L23,81 Q23,75 29,75 Z"),
        new("upper-arm-right", "Right upper arm", true, "M 91 75 L 90 75 Q 84 75 84 81 L 84 107 Q 84 113 90 113 L 91 113 Q 97 113 97 107 L 97 81 Q 97 75 91 75 Z"),
        new("forearm-left", "Left forearm and elbow", true, "M27,116 L28,116 Q34,116 34,122 L34,148 Q34,154 28,154 L27,154 Q21,154 21,148 L21,122 Q21,116 27,116 Z"),
        new("forearm-right", "Right forearm and elbow", true, "M 93 116 L 92 116 Q 86 116 86 122 L 86 148 Q 86 154 92 154 L 93 154 Q 99 154 99 148 L 99 122 Q 99 116 93 116 Z"),
        new("hand-left", "Left hand", true, "M20,165 A7,9.5 0 1 1 34,165 A7,9.5 0 1 1 20,165 Z"),
        new("hand-right", "Right hand", true, "M 100 165 A 7 9.5 0 1 0 86 165 A 7 9.5 0 1 0 100 165 Z"),
        new("thigh-left", "Left thigh", true, "M49,160 L50.5,160 Q58.5,160 58.5,168 L58.5,205 Q58.5,213 50.5,213 L49,213 Q41,213 41,205 L41,168 Q41,160 49,160 Z"),
        new("thigh-right", "Right thigh", true, "M 71 160 L 69.5 160 Q 61.5 160 61.5 168 L 61.5 205 Q 61.5 213 69.5 213 L 71 213 Q 79 213 79 205 L 79 168 Q 79 160 71 160 Z"),
        new("knee-left", "Left knee", true, "M41.5,221 A8,6.5 0 1 1 57.5,221 A8,6.5 0 1 1 41.5,221 Z"),
        new("knee-right", "Right knee", true, "M 78.5 221 A 8 6.5 0 1 0 62.5 221 A 8 6.5 0 1 0 78.5 221 Z"),
        new("lower-leg-left", "Left lower leg", true, "M48.5,229 L50.5,229 Q56.5,229 56.5,235 L56.5,270 Q56.5,276 50.5,276 L48.5,276 Q42.5,276 42.5,270 L42.5,235 Q42.5,229 48.5,229 Z"),
        new("lower-leg-right", "Right lower leg", true, "M 71.5 229 L 69.5 229 Q 63.5 229 63.5 235 L 63.5 270 Q 63.5 276 69.5 276 L 71.5 276 Q 77.5 276 77.5 270 L 77.5 235 Q 77.5 229 71.5 229 Z"),
        new("foot-left", "Left foot", true, "M39.5,284 A9.5,5.5 0 1 1 58.5,284 A9.5,5.5 0 1 1 39.5,284 Z"),
        new("foot-right", "Right foot", true, "M 80.5 284 A 9.5 5.5 0 1 0 61.5 284 A 9.5 5.5 0 1 0 80.5 284 Z"),
        new("upper-back", "Upper back", true, "M47,56 L73,56 Q81,56 83.4,64 L80.6,81 Q79,89 71,89 L49,89 Q41,89 39.4,81 L36.6,64 Q39,56 47,56 Z"),
        new("middle-back", "Middle back", true, "M46,91 L74,91 Q79,91 79,96 L79,107 Q79,112 74,112 L46,112 Q41,112 41,107 L41,96 Q41,91 46,91 Z"),
        new("lower-back", "Lower back", true, "M47,114 L73,114 Q78,114 78,119 L78,129 Q78,134 73,134 L47,134 Q42,134 42,129 L42,119 Q42,114 47,114 Z"),
        new("buttocks", "Buttocks", true, "M48,136 L51,136 Q59,136 59,144 L59,149 Q59,157 51,157 L48,157 Q40,157 40,149 L40,144 Q40,136 48,136 Z M 72 136 L 69 136 Q 61 136 61 144 L 61 149 Q 61 157 69 157 L 72 157 Q 80 157 80 149 L 80 144 Q 80 136 72 136 Z"),
    };

    public static readonly string[] LevelNames = { "", "Mild", "Moderate", "Severe" };

    private static readonly Color[] LevelColours =
    {
        Colors.Transparent, Color.FromRgb(0xFA, 0xCC, 0x15), Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0xDC, 0x26, 0x26)
    };

    private static readonly Color HeatLow = Color.FromRgb(0xFE, 0xF0, 0x8A);
    private static readonly Color HeatMid = Color.FromRgb(0xF9, 0x73, 0x16);
    private static readonly Color HeatHigh = Color.FromRgb(0xB9, 0x1C, 0x1C);
    private static readonly Brush PrintBase = Frozen(Color.FromRgb(0xEE, 0xEA, 0xF4));
    private static readonly Brush PrintLine = Frozen(Color.FromRgb(0xC9, 0xBE, 0xDD));

    private readonly bool _editable;
    private readonly bool _forPrint;
    private readonly Dictionary<string, List<Path>> _paths = new();
    private Dictionary<string, int> _levels = new();

    public event Action<Dictionary<string, int>>? AreasChanged;

    public BodyMap(bool editable, bool forPrint = false)
    {
        _editable = editable;
        _forPrint = forPrint;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(Figure(false));
        var back = Figure(true);
        Grid.SetColumn(back, 2);
        row.Children.Add(back);
        Content = new Viewbox { Child = row, Stretch = Stretch.Uniform };
    }

    public static string LabelFor(string key) => Regions.FirstOrDefault(r => r.Key == key)?.Label ?? key;

    // I list the marked areas worst first, like "Lower back (severe), Left knee (mild)"
    public static string Describe(IReadOnlyDictionary<string, int>? areas)
    {
        if (areas == null || areas.Count == 0) return "";
        var order = Regions.Select(r => r.Key).Distinct().ToList();
        return string.Join(", ", areas.Where(a => a.Value is >= 1 and <= 3)
            .OrderByDescending(a => a.Value).ThenBy(a => order.IndexOf(a.Key))
            .Select(a => $"{LabelFor(a.Key)} ({LevelNames[a.Value].ToLowerInvariant()})"));
    }

    private FrameworkElement Figure(bool back)
    {
        var stack = new StackPanel();
        var title = new TextBlock { Text = back ? "Back" : "Front", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        if (_forPrint) title.Foreground = Frozen(Color.FromRgb(0x6F, 0x67, 0x84));
        else title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(title);

        var canvas = new Canvas { Width = 120, Height = 300 };
        foreach (var region in Regions.Where(r => r.Back == back))
        {
            var geometry = Geometry.Parse(region.Data);
            geometry.Freeze();
            var path = new Path { Data = geometry, StrokeThickness = 0.8, Tag = region.Key, ToolTip = region.Label };
            if (_forPrint)
            {
                path.Fill = PrintBase;
                path.Stroke = PrintLine;
            }
            else
            {
                path.SetResourceReference(Shape.FillProperty, "Brush.AccentSoft");
                path.SetResourceReference(Shape.StrokeProperty, "Brush.Border");
            }
            if (_editable)
            {
                path.Cursor = Cursors.Hand;
                path.MouseEnter += (_, _) => Hover(region.Key, true);
                path.MouseLeave += (_, _) => Hover(region.Key, false);
                path.MouseLeftButtonUp += (_, e) =>
                {
                    Step(region.Key, false);
                    e.Handled = true;
                };
                path.MouseRightButtonUp += (_, e) =>
                {
                    Step(region.Key, true);
                    e.Handled = true;
                };
            }
            if (!_paths.TryGetValue(region.Key, out var list)) _paths[region.Key] = list = new List<Path>();
            list.Add(path);
            canvas.Children.Add(path);
        }
        stack.Children.Add(canvas);
        return stack;
    }

    // I show where it hurts today, each area coloured by how bad it is
    public void Show(IReadOnlyDictionary<string, int>? areas)
    {
        _levels = areas?.Where(a => a.Value is >= 1 and <= 3).ToDictionary(a => a.Key, a => a.Value) ?? new Dictionary<string, int>();
        foreach (var key in _paths.Keys) Paint(key);
    }

    // I colour each area from pale yellow to deep red by how much it hurt over a stretch of days
    public void ShowHeat(IReadOnlyDictionary<string, double> heat, Func<string, string>? tip = null)
    {
        foreach (var (key, paths) in _paths)
        {
            var amount = heat.TryGetValue(key, out var value) ? Math.Clamp(value, 0, 1) : 0;
            foreach (var path in paths)
            {
                if (amount > 0) path.Fill = Frozen(HeatColour(amount));
                else if (_forPrint) path.Fill = PrintBase;
                else path.SetResourceReference(Shape.FillProperty, "Brush.AccentSoft");
                path.ToolTip = tip?.Invoke(key) ?? LabelFor(key);
            }
        }
    }

    public static Color HeatColour(double amount)
    {
        static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        return amount < 0.5 ? Mix(HeatLow, HeatMid, amount * 2) : Mix(HeatMid, HeatHigh, (amount - 0.5) * 2);
    }

    // I go one step worse with a left click and back to nothing with a right click
    private void Step(string key, bool clear)
    {
        if (clear && !_levels.ContainsKey(key)) return;
        _levels.TryGetValue(key, out var level);
        level = clear ? 0 : (level + 1) % 4;
        if (level == 0) _levels.Remove(key);
        else _levels[key] = level;
        Paint(key);
        AreasChanged?.Invoke(new Dictionary<string, int>(_levels));
    }

    private void Paint(string key)
    {
        if (!_paths.TryGetValue(key, out var paths)) return;
        _levels.TryGetValue(key, out var level);
        foreach (var path in paths)
        {
            if (level > 0) path.Fill = Frozen(LevelColours[level]);
            else if (_forPrint) path.Fill = PrintBase;
            else path.SetResourceReference(Shape.FillProperty, "Brush.AccentSoft");
            path.ToolTip = level > 0 ? $"{LabelFor(key)}: {LevelNames[level].ToLowerInvariant()}" : LabelFor(key);
        }
    }

    private void Hover(string key, bool on)
    {
        if (!_paths.TryGetValue(key, out var paths)) return;
        foreach (var path in paths)
        {
            if (on) path.SetResourceReference(Shape.StrokeProperty, "Brush.Accent");
            else path.SetResourceReference(Shape.StrokeProperty, "Brush.Border");
            path.StrokeThickness = on ? 1.6 : 0.8;
        }
    }

    // I work out how much each area hurt over some days, scaled so a couple of severe days or a week of mild ones is the darkest red
    public static Dictionary<string, double> Heat(IEnumerable<IReadOnlyDictionary<string, int>?> days, out Dictionary<string, (int Days, int Worst)> totals)
    {
        totals = new Dictionary<string, (int Days, int Worst)>();
        var sums = new Dictionary<string, double>();
        foreach (var day in days)
        {
            if (day == null) continue;
            foreach (var (key, level) in day)
            {
                if (level is < 1 or > 3) continue;
                sums[key] = sums.GetValueOrDefault(key) + level;
                var (count, worst) = totals.GetValueOrDefault(key);
                totals[key] = (count + 1, Math.Max(worst, level));
            }
        }
        var top = Math.Max(6, sums.Count == 0 ? 1 : sums.Values.Max());
        return sums.ToDictionary(s => s.Key, s => s.Value / top);
    }

    // I build a small legend for the heatmap, from a little pain to the most
    public static FrameworkElement HeatKey(bool forPrint)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        TextBlock Words(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            if (forPrint) block.Foreground = Frozen(Color.FromRgb(0x6F, 0x67, 0x84));
            else block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            return block;
        }
        row.Children.Add(Words("Less"));
        var bar = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        bar.GradientStops.Add(new GradientStop(HeatLow, 0));
        bar.GradientStops.Add(new GradientStop(HeatMid, 0.5));
        bar.GradientStops.Add(new GradientStop(HeatHigh, 1));
        bar.Freeze();
        row.Children.Add(new Border { Width = 110, Height = 10, CornerRadius = new CornerRadius(5), Background = bar, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Words("More"));
        return row;
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
