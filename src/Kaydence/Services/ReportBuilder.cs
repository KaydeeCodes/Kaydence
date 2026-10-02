using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Kaydence.Controls;
using Kaydence.Models;

namespace Kaydence.Services;

public sealed class ReportOptions
{
    public bool Writing { get; set; } = true;
    public bool Pictures { get; set; } = true;
    public bool Mood { get; set; } = true;
    public bool MoodChart { get; set; } = true;
    public bool CheckIn { get; set; } = true;
    public bool Health { get; set; }
    public bool HealthCharts { get; set; }
    public bool Transition { get; set; }
    public bool DayPerPage { get; set; }
}

// I build my printable diary pages, always dark text on white paper whatever theme I'm using
public static class ReportBuilder
{
    private static readonly Brush Ink = Frozen(0x2A, 0x24, 0x38);
    private static readonly Brush Muted = Frozen(0x6F, 0x67, 0x84);
    private static readonly Brush Band = Frozen(0xF3, 0xED, 0xFB);
    private static readonly Brush Accent = Frozen(0x7B, 0x4F, 0xB8);
    private static readonly Brush Rule = Frozen(0xE6, 0xDE, 0xF1);

    public static FlowDocument Build(DiaryStore store, DateOnly from, DateOnly to, ReportOptions options, string weightUnit, out int dayCount)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = Ink,
            Background = Brushes.White,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity
        };

        document.Blocks.Add(Header(from, to));

        var entries = store.Days
            .Where(d => d >= from && d <= to)
            .OrderBy(d => d)
            .Select(d => (Day: d, Entry: store.LoadDay(d)))
            .ToList();
        dayCount = entries.Count;

        if (entries.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run("Nothing written between these dates.")) { Foreground = Muted });
            return document;
        }

        if (options.MoodChart && BuildChart(entries, from, to) is { } moodChart)
        {
            document.Blocks.Add(new Paragraph(new Run("Mood over these days") { FontWeight = FontWeights.Bold, Foreground = Ink }) { FontSize = 11.5, Margin = new Thickness(0, 4, 0, 2) });
            document.Blocks.Add(new BlockUIContainer(moodChart) { Margin = new Thickness(0, 0, 0, 6) });
        }

        if (options.HealthCharts)
        {
            var charts = HealthCharts.Build(entries.Select(e => (e.Day, e.Entry.CheckIn)), from, to, weightUnit, forPrint: true);
            foreach (var (title, note, chart) in charts)
            {
                chart.Width = 640;
                var heading = new Paragraph { FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 10, 0, 2) };
                heading.Inlines.Add(new Run(title) { FontWeight = FontWeights.Bold, Foreground = Ink });
                if (note.Length > 0) heading.Inlines.Add(new Run("   " + note));
                document.Blocks.Add(heading);
                document.Blocks.Add(new BlockUIContainer(chart) { Margin = new Thickness(0, 0, 0, 6) });
            }
        }

        if ((options.Health || options.HealthCharts) && PainPicture(entries) is { } pain)
        {
            var heading = new Paragraph { FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 10, 0, 2), KeepWithNext = true };
            heading.Inlines.Add(new Run("Where it hurt") { FontWeight = FontWeights.Bold, Foreground = Ink });
            heading.Inlines.Add(new Run("   Darker red means more days and worse pain in that area"));
            document.Blocks.Add(heading);
            document.Blocks.Add(new BlockUIContainer(pain) { Margin = new Thickness(0, 0, 0, 6) });
        }

        var first = true;
        foreach (var (day, entry) in entries)
        {
            var section = BuildDay(store, day, entry, options, weightUnit);
            if (options.DayPerPage && !first) section.BreakPageBefore = true;
            first = false;
            document.Blocks.Add(section);
        }
        return document;
    }

    private static Paragraph Header(DateOnly from, DateOnly to)
    {
        var culture = CultureInfo.CurrentCulture;
        var header = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(0, 0, 0, 10),
            BorderBrush = Accent,
            BorderThickness = new Thickness(0, 0, 0, 2)
        };
        try
        {
            var logo = new BitmapImage(new Uri("pack://application:,,,/Assets/Kaydence.png", UriKind.Absolute));
            header.Inlines.Add(new InlineUIContainer(new Image { Source = logo, Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) })
            {
                BaselineAlignment = BaselineAlignment.Center
            });
        }
        catch (Exception)
        {
            // I just leave the logo off if it can't load
        }
        header.Inlines.Add(new Run("My diary") { FontSize = 20, FontWeight = FontWeights.SemiBold });
        header.Inlines.Add(new Run($"     {from.ToString("d MMMM yyyy", culture)} to {to.ToString("d MMMM yyyy", culture)}") { Foreground = Muted, FontSize = 12 });
        return header;
    }

    private static Section BuildDay(DiaryStore store, DateOnly day, DayEntry entry, ReportOptions options, string weightUnit)
    {
        var c = entry.CheckIn;
        var section = new Section();

        // I give every day its own coloured band so days never run into each other
        var band = new Paragraph
        {
            Background = Band,
            BorderBrush = Accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 16, 0, 8),
            KeepWithNext = true
        };
        band.Inlines.Add(new Run(day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)) { FontWeight = FontWeights.Bold, FontSize = 13.5 });
        if (options.Mood && Moods.Get(c.Mood) is { } mood)
        {
            band.Inlines.Add(new Run("     "));
            band.Inlines.Add(new InlineUIContainer(new MoodFace { Level = mood.Value, Width = 14, Height = 14 }) { BaselineAlignment = BaselineAlignment.Center });
            band.Inlines.Add(new Run($" {mood.Name}") { Foreground = Muted });
        }
        if (c.Transition.IsMilestone) band.Inlines.Add(new Run("     ★ Milestone") { Foreground = Accent });
        section.Blocks.Add(band);

        if (options.Writing)
        {
            var lines = DayText.Get(entry).Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                section.Blocks.Add(new Paragraph(new Run(line.Trim())) { Margin = new Thickness(0, 0, 0, 5) });
        }

        if (options.Pictures)
        {
            foreach (var name in DayText.ImageNames(entry).Take(6))
            {
                var source = ImageHelper.Load(store.ImagePath(day, name), 900);
                if (source == null) continue;
                var image = new Image { Source = source, MaxWidth = 300, MaxHeight = 220, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                section.Blocks.Add(new BlockUIContainer(image) { Margin = new Thickness(0, 4, 0, 8) });
            }

            if (Drawings(store, day) is { } drawings)
            {
                section.Blocks.Add(new Paragraph(new Run("Drawings") { FontWeight = FontWeights.Bold }) { FontSize = 11.5, Margin = new Thickness(0, 6, 0, 2), KeepWithNext = true });
                section.Blocks.Add(new BlockUIContainer(drawings) { Margin = new Thickness(0, 0, 0, 8) });
            }
        }

        var facts = Facts(c, options, weightUnit);
        if (facts.Count > 0) section.Blocks.Add(FactsBlock(facts));
        return section;
    }

    // I show a body heatmap of where it hurt over the printed days, with the worst areas listed beside it
    private static UIElement? PainPicture(List<(DateOnly Day, DayEntry Entry)> entries)
    {
        var heat = BodyMap.Heat(entries.Select(e => (IReadOnlyDictionary<string, int>?)e.Entry.CheckIn.Symptoms.Areas), out var totals);
        if (heat.Count == 0) return null;
        var map = new BodyMap(editable: false, forPrint: true) { Width = 240 };
        map.ShowHeat(heat);

        var list = new StackPanel { Margin = new Thickness(24, 20, 0, 0) };
        foreach (var (key, (days, worst)) in totals.OrderByDescending(t => heat[t.Key]).Take(8))
        {
            list.Children.Add(new TextBlock
            {
                Text = $"{BodyMap.LabelFor(key)}: {days} {(days == 1 ? "day" : "days")}, worst {BodyMap.LevelNames[worst].ToLowerInvariant()}",
                FontSize = 12,
                Foreground = Ink,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        var left = new StackPanel();
        left.Children.Add(map);
        left.Children.Add(BodyMap.HeatKey(forPrint: true));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(left);
        row.Children.Add(list);
        return row;
    }

    // I draw the pen, highlighter and shape strokes from the page as a crisp picture that scales to the paper
    private static UIElement? Drawings(DiaryStore store, DateOnly day)
    {
        try
        {
            var ink = store.LoadInk(day);
            if (ink == null || ink.Length == 0) return null;
            using var stream = new MemoryStream(ink);
            var strokes = new StrokeCollection(stream);
            if (strokes.Count == 0) return null;
            var bounds = strokes.GetBounds();
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return null;

            var group = new DrawingGroup();
            using (var context = group.Open())
            {
                // I add a see through frame around the strokes so nothing gets clipped at the edges
                context.DrawRectangle(Brushes.Transparent, null, new Rect(bounds.X - 6, bounds.Y - 6, bounds.Width + 12, bounds.Height + 12));
                strokes.Draw(context);
            }
            group.Freeze();
            return new Image
            {
                Source = new DrawingImage(group),
                Width = Math.Min(600, bounds.Width + 12),
                MaxHeight = 520,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Print", $"Couldn't add the drawings for {day:yyyy-MM-dd}", ex);
            return null;
        }
    }

    internal static List<(string Label, string Value)> Facts(CheckIn c, ReportOptions options, string weightUnit)
    {
        var facts = new List<(string, string)>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) facts.Add((label, value.Trim()));
        }

        if (options.Mood) Add("Mood note", c.MoodNote);

        if (options.CheckIn)
        {
            Add("Note", c.Note);
            Add("Sleep", Join(c.Sleep.Hours is double hours ? $"{hours:0.#} hours" : null, c.Sleep.Notes));
            Add("Fitness", Join(c.Fitness.Activity, c.Fitness.Minutes is int minutes ? $"{minutes} minutes" : null));
            var done = c.Tasks.Where(t => t.Done && CheckIn.Has(t.Text)).Select(t => t.Text!).ToList();
            var open = c.Tasks.Where(t => !t.Done && CheckIn.Has(t.Text)).Select(t => t.Text!).ToList();
            Add("Tasks done", string.Join(", ", done));
            Add("Tasks to do", string.Join(", ", open));
            Add("Struggles", c.Struggles);
            Add("Wins", c.Wins);
            var h = c.Habits;
            Add("Habits", Join(
                h.Water > 0 ? $"{h.Water} {(h.Water == 1 ? "glass" : "glasses")} of water" : null,
                h.Caffeine > 0 ? $"{h.Caffeine} caffeine {(h.Caffeine == 1 ? "drink" : "drinks")}" : null,
                h.Smoked ? (h.Cigarettes is int smoked ? $"smoked {smoked}" : "smoked") : null,
                h.Drank ? (h.Alcohol is double drank ? $"{drank:0.#} units or glasses of alcohol" : "drank alcohol") : null));
        }

        if (options.Health)
        {
            var meds = c.Medications.Select(m => $"{m.Name} {m.Dose}".Trim() + (m.Time != null ? $" at {m.Time}" : ""));
            Add("Medications", Join(string.Join(", ", meds), c.MedsOther));
            Add("Pain", Join(c.Symptoms.Pain is int pain ? $"{pain} out of 10" : null, c.Symptoms.Notes));
            Add("Where it hurts", BodyMap.Describe(c.Symptoms.Areas));
            var readings = c.BloodPressure.Where(b => b.HasData).Select(b =>
                $"{b.Systolic}/{b.Diastolic}" + (b.Pulse.HasValue ? $", pulse {b.Pulse}" : "") + (CheckIn.Has(b.Time) ? $" at {b.Time}" : ""));
            Add("Blood pressure", string.Join("; ", readings));
            if (c.WeightKg is double kg) Add("Weight", Units.FormatWeight(kg, weightUnit));
            Add("Period", Join(c.Cycle.Flow is { } flow ? $"{flow} flow" : null, c.Cycle.Symptoms, c.Cycle.Notes));
        }

        if (options.Transition)
        {
            Add("HRT", c.Transition.Hrt);
            Add("Injection site", c.Transition.InjectionSite);
            Add("Milestone", c.Transition.Milestone);
        }
        return facts;
    }

    private static string? Join(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return kept.Count == 0 ? null : string.Join(", ", kept);
    }

    // I lay each fact out as its own line, label then value, which prints cleanly at any page width
    private static Section FactsBlock(List<(string Label, string Value)> facts)
    {
        var block = new Section { Margin = new Thickness(0, 4, 0, 6) };
        foreach (var (label, value) in facts)
        {
            var line = new Paragraph { Margin = new Thickness(0, 0, 0, 3), TextIndent = 0 };
            line.Inlines.Add(new Run(label + ":  ") { Foreground = Ink, FontWeight = FontWeights.Bold, FontSize = 11.5 });
            line.Inlines.Add(new Run(value) { FontSize = 12 });
            block.Blocks.Add(line);
        }
        return block;
    }

    // I draw a little line of my moods across the dates I'm printing
    private static UIElement? BuildChart(List<(DateOnly Day, DayEntry Entry)> entries, DateOnly from, DateOnly to)
    {
        var points = entries.Where(e => e.Entry.CheckIn.Mood.HasValue)
            .Select(e => (e.Day, Mood: e.Entry.CheckIn.Mood!.Value))
            .ToList();
        if (points.Count < 2) return null;

        const double width = 640;
        const double height = 90;
        var span = Math.Max(1, to.DayNumber - from.DayNumber);
        double X(DateOnly d) => 6 + (d.DayNumber - from.DayNumber) / (double)span * (width - 12);
        static double Y(int mood) => 8 + (5 - mood) / 4.0 * (height - 16);

        var canvas = new Canvas { Width = width, Height = height };
        for (var level = 1; level <= 5; level++)
            canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = Y(level), Y2 = Y(level), Stroke = Rule, StrokeThickness = 1 });

        var line = new Polyline { Stroke = Accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        foreach (var point in points) line.Points.Add(new Point(X(point.Day), Y(point.Mood)));
        canvas.Children.Add(line);

        foreach (var point in points)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Moods.Get(point.Mood)!.Color) };
            Canvas.SetLeft(dot, X(point.Day) - 3.5);
            Canvas.SetTop(dot, Y(point.Mood) - 3.5);
            canvas.Children.Add(dot);
        }
        return canvas;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
