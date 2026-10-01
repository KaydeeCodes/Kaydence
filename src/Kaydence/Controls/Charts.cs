using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I'm one line on a chart, the X values are day numbers so gaps in my diary show as gaps
public sealed class ChartSeries
{
    public string Name { get; init; } = "";
    public Color Color { get; init; }
    public List<Point> Points { get; init; } = new();
    public bool ShowDots { get; init; } = true;
    public double Thickness { get; init; } = 2;
    public double Opacity { get; init; } = 1;
}

// I draw simple line charts myself so they match my theme and print nicely too
public sealed class LineChart : FrameworkElement
{
    private Point? _hover;

    public LineChart()
    {
        Height = 220;
        Loaded += (_, _) => ThemeService.ThemeChanged += InvalidateVisual;
        Unloaded += (_, _) => ThemeService.ThemeChanged -= InvalidateVisual;
        MouseMove += (_, e) =>
        {
            if (!Interactive) return;
            _hover = e.GetPosition(this);
            InvalidateVisual();
        };
        MouseLeave += (_, _) =>
        {
            _hover = null;
            InvalidateVisual();
        };
        Cursor = Cursors.Cross;
    }

    public List<ChartSeries> Series { get; } = new();
    public double? MinX { get; set; }
    public double? MaxX { get; set; }
    public double? MinY { get; set; }
    public double? MaxY { get; set; }
    public IReadOnlyList<double>? Ticks { get; set; }
    public Func<double, string> FormatY { get; set; } = v => v.ToString("0.#", CultureInfo.CurrentCulture);
    public Func<double, string> FormatX { get; set; } = x => DateOnly.FromDayNumber((int)x).ToString("d MMM", CultureInfo.CurrentCulture);
    public Func<double, string>? FormatTip { get; set; }

    // I break the line if I skipped more than this many days, so it doesn't pretend I wrote in between
    public int GapDays { get; set; } = 10;

    // I switch this off for printing, and pick fixed colours so the page never comes out dark
    public bool Interactive { get; set; } = true;
    public bool PrintColours { get; set; }

    private Brush Themed(string key, Color fallback)
    {
        if (!PrintColours && TryFindResource(key) is Brush brush) return brush;
        return new SolidColorBrush(fallback);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 160 || height < 60) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var faint = Themed("Brush.TextFaint", Color.FromRgb(0x8A, 0x84, 0x96));
        var lineBrush = Themed("Brush.Border", Color.FromRgb(0xE4, 0xDF, 0xEC));
        var text = Themed("Brush.Text", Color.FromRgb(0x2A, 0x24, 0x38));
        var card = Themed("Brush.Card", Colors.White);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var all = Series.SelectMany(s => s.Points).ToList();
        if (all.Count == 0)
        {
            var empty = MakeText("Nothing to show here yet", faint, 12.5, dpi);
            dc.DrawText(empty, new Point((width - empty.Width) / 2, (height - empty.Height) / 2));
            return;
        }

        var minX = MinX ?? all.Min(p => p.X);
        var maxX = MaxX ?? all.Max(p => p.X);
        if (maxX - minX < 1) maxX = minX + 1;
        var minY = MinY ?? all.Min(p => p.Y);
        var maxY = MaxY ?? all.Max(p => p.Y);
        if (MinY == null || MaxY == null)
        {
            var pad = Math.Max(1, (maxY - minY) * 0.12);
            if (MinY == null) minY = Math.Floor(minY - pad);
            if (MaxY == null) maxY = Math.Ceiling(maxY + pad);
        }
        if (maxY - minY < 0.001) maxY = minY + 1;

        const double left = 58;
        const double top = 10;
        var right = width - 14;
        var bottom = height - 28;
        double MapX(double x) => left + (x - minX) / (maxX - minX) * (right - left);
        double MapY(double y) => bottom - (y - minY) / (maxY - minY) * (bottom - top);

        // I draw the grid lines and labels up the side
        var gridPen = new Pen(lineBrush, 1);
        var ticks = Ticks ?? Enumerable.Range(0, 5).Select(i => minY + (maxY - minY) * i / 4).ToList();
        foreach (var tick in ticks)
        {
            var y = Math.Round(MapY(tick)) + 0.5;
            dc.DrawLine(gridPen, new Point(left, y), new Point(right, y));
            var label = MakeText(FormatY(tick), faint, 11, dpi);
            dc.DrawText(label, new Point(left - 8 - label.Width, y - label.Height / 2));
        }

        // I spread a few date labels along the bottom
        var labels = Math.Max(2, Math.Min(6, (int)((right - left) / 110)));
        for (var i = 0; i < labels; i++)
        {
            var x = minX + (maxX - minX) * i / (labels - 1);
            var label = MakeText(FormatX(x), faint, 11, dpi);
            var at = Math.Clamp(MapX(x) - label.Width / 2, left - 10, width - label.Width);
            dc.DrawText(label, new Point(at, bottom + 8));
        }

        foreach (var series in Series)
        {
            if (series.Points.Count == 0) continue;
            var brush = new SolidColorBrush(series.Color) { Opacity = series.Opacity };
            var pen = new Pen(brush, series.Thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var ordered = series.Points.OrderBy(p => p.X).ToList();

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (var i = 0; i < ordered.Count; i++)
                {
                    var point = new Point(MapX(ordered[i].X), MapY(ordered[i].Y));
                    var newRun = i == 0 || ordered[i].X - ordered[i - 1].X > GapDays;
                    if (newRun) ctx.BeginFigure(point, false, false);
                    else ctx.LineTo(point, true, true);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);

            if (series.ShowDots && ordered.Count <= 400)
            {
                var radius = ordered.Count > 120 ? 1.8 : 2.8;
                foreach (var point in ordered) dc.DrawEllipse(brush, null, new Point(MapX(point.X), MapY(point.Y)), radius, radius);
            }
        }

        if (_hover is not { } mouse || mouse.X < left || mouse.X > right) return;

        // I show a little marker with the values for whichever day my mouse is nearest
        var hoverX = minX + (mouse.X - left) / (right - left) * (maxX - minX);
        var nearest = all.OrderBy(p => Math.Abs(p.X - hoverX)).First().X;
        var markX = Math.Round(MapX(nearest)) + 0.5;
        dc.DrawLine(new Pen(faint, 1) { DashStyle = DashStyles.Dash }, new Point(markX, top), new Point(markX, bottom));

        var lines = new List<FormattedText> { MakeText(FormatX(nearest), text, 11.5, dpi, bold: true) };
        foreach (var series in Series)
        {
            var match = series.Points.Where(p => Math.Abs(p.X - nearest) < 0.5).Select(p => (double?)p.Y).FirstOrDefault();
            if (match is not { } value) continue;
            var shown = FormatTip?.Invoke(value) ?? FormatY(value);
            lines.Add(MakeText(series.Name.Length > 0 ? $"{series.Name}: {shown}" : shown, new SolidColorBrush(series.Color), 11.5, dpi));
            dc.DrawEllipse(card, new Pen(new SolidColorBrush(series.Color), 2), new Point(markX, MapY(value)), 4, 4);
        }

        var boxWidth = lines.Max(l => l.Width) + 20;
        var boxHeight = lines.Sum(l => l.Height + 2) + 14;
        var boxX = markX + 12 + boxWidth > width ? markX - 12 - boxWidth : markX + 12;
        var box = new Rect(boxX, top + 4, boxWidth, boxHeight);
        dc.DrawRoundedRectangle(card, new Pen(lineBrush, 1), box, 8, 8);
        var lineY = box.Y + 7;
        foreach (var line in lines)
        {
            dc.DrawText(line, new Point(box.X + 10, lineY));
            lineY += line.Height + 2;
        }
    }

    private static FormattedText MakeText(string value, Brush brush, double size, double dpi, bool bold = false) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, dpi);
}

// I build simple bar charts out of ordinary borders so tooltips just work
public static class BarChart
{
    public sealed record Bar(string Label, double? Value, string Tip, Color? Colour = null);

    public static FrameworkElement Build(IReadOnlyList<Bar> bars, double max, Func<double, string> format, double height = 150)
    {
        var grid = new Grid { Height = height + 26 };
        for (var i = 0; i < bars.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = bars[i];

            var column = new DockPanel { Margin = new Thickness(4, 0, 4, 0), Background = Brushes.Transparent, ToolTip = bar.Tip };
            var label = UiKit.Text(bar.Label, 11.5, "Brush.TextMuted");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 6, 0, 0);
            DockPanel.SetDock(label, Dock.Bottom);
            column.Children.Add(label);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            if (bar.Value is { } value)
            {
                var number = UiKit.Text(format(value), 11.5, "Brush.Text", FontWeights.SemiBold);
                number.HorizontalAlignment = HorizontalAlignment.Center;
                number.Margin = new Thickness(0, 0, 0, 4);
                stack.Children.Add(number);
                var fill = new Border
                {
                    Height = Math.Max(3, value / Math.Max(0.001, max) * (height - 22)),
                    CornerRadius = new CornerRadius(6, 6, 2, 2),
                    MaxWidth = 56
                };
                if (bar.Colour is { } colour) fill.Background = new SolidColorBrush(colour);
                else fill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
                stack.Children.Add(fill);
            }
            else
            {
                var none = new Border { Height = 3, CornerRadius = new CornerRadius(2), MaxWidth = 56 };
                none.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
                stack.Children.Add(none);
            }
            column.Children.Add(stack);

            Grid.SetColumn(column, i);
            grid.Children.Add(column);
        }
        return grid;
    }
}

// I build my health charts once so Insights and my doctor printout always show the same thing
public static class HealthCharts
{
    public static List<(string Title, string Note, LineChart Chart)> Build(
        IEnumerable<(DateOnly Day, CheckIn CheckIn)> source, DateOnly from, DateOnly to, string unit, bool forPrint)
    {
        var days = source.ToList();
        var charts = new List<(string Title, string Note, LineChart Chart)>();

        var sleep = days.Where(d => d.CheckIn.Sleep.Hours.HasValue).Select(d => new Point(d.Day.DayNumber, d.CheckIn.Sleep.Hours!.Value)).ToList();
        if (sleep.Count > 0)
        {
            var chart = Make(from, to, forPrint, v => $"{v:0.#} h");
            chart.MinY = 0;
            chart.Series.Add(new ChartSeries { Name = "Sleep", Color = Color.FromRgb(0x6C, 0x8C, 0xF5), Points = sleep });
            charts.Add(("Sleep", $"Average {sleep.Average(p => p.Y):0.#} hours a night", chart));
        }

        var weight = days.Where(d => d.CheckIn.WeightKg.HasValue).Select(d => new Point(d.Day.DayNumber, ToUnit(d.CheckIn.WeightKg!.Value, unit))).ToList();
        if (weight.Count > 0)
        {
            var label = unit switch { "lb" => "lb", "st" => "st", _ => "kg" };
            var chart = Make(from, to, forPrint, v => $"{v:0.#} {label}");
            chart.FormatTip = v => unit == "st" ? Units.FormatWeight(v * 14 / Units.PoundsPerKg, "st") : $"{v:0.#} {label}";
            chart.Series.Add(new ChartSeries { Name = "Weight", Color = Color.FromRgb(0x30, 0xA4, 0x6C), Points = weight });
            var change = weight.Last().Y - weight.First().Y;
            var note = weight.Count > 1 ? $"{(change >= 0 ? "Up" : "Down")} {Math.Abs(change):0.#} {label} in this time" : "";
            charts.Add(("Weight", note, chart));
        }

        var readings = days.SelectMany(d => d.CheckIn.BloodPressure.Where(b => b.HasData).Select(b => (d.Day, Reading: b))).ToList();
        if (readings.Count > 0)
        {
            // I use the average of the day if I took more than one reading
            Point Daily(Func<BpReading, int?> pick, IGrouping<DateOnly, (DateOnly Day, BpReading Reading)> g) =>
                new(g.Key.DayNumber, g.Where(x => pick(x.Reading).HasValue).Average(x => pick(x.Reading)!.Value));
            var grouped = readings.GroupBy(r => r.Day).ToList();
            var chart = Make(from, to, forPrint, v => $"{v:0}");
            chart.Series.Add(new ChartSeries
            {
                Name = "Systolic",
                Color = Color.FromRgb(0xE5, 0x48, 0x4D),
                Points = grouped.Where(g => g.Any(x => x.Reading.Systolic.HasValue)).Select(g => Daily(r => r.Systolic, g)).ToList()
            });
            chart.Series.Add(new ChartSeries
            {
                Name = "Diastolic",
                Color = Color.FromRgb(0xF7, 0x6B, 0x15),
                Points = grouped.Where(g => g.Any(x => x.Reading.Diastolic.HasValue)).Select(g => Daily(r => r.Diastolic, g)).ToList()
            });
            chart.Series.Add(new ChartSeries
            {
                Name = "Pulse",
                Color = Color.FromRgb(0x8B, 0x8D, 0x98),
                Thickness = 1.4,
                Points = grouped.Where(g => g.Any(x => x.Reading.Pulse.HasValue)).Select(g => Daily(r => r.Pulse, g)).ToList()
            });
            charts.Add(("Blood pressure and pulse", "Red is systolic, orange is diastolic, grey is pulse", chart));
        }

        var pain = days.Where(d => d.CheckIn.Symptoms.Pain.HasValue).Select(d => new Point(d.Day.DayNumber, d.CheckIn.Symptoms.Pain!.Value)).ToList();
        if (pain.Count > 0)
        {
            var chart = Make(from, to, forPrint, v => $"{v:0}");
            chart.MinY = 0;
            chart.MaxY = 10;
            chart.Ticks = new double[] { 0, 2, 4, 6, 8, 10 };
            chart.FormatTip = v => $"{v:0} out of 10";
            chart.Series.Add(new ChartSeries { Name = "Pain", Color = Color.FromRgb(0xEC, 0x5F, 0xA0), Points = pain });
            charts.Add(("Pain", $"Average {pain.Average(p => p.Y):0.#} out of 10", chart));
        }

        return charts;
    }

    private static double ToUnit(double kg, string unit) => unit switch
    {
        "lb" => kg * Units.PoundsPerKg,
        "st" => kg * Units.PoundsPerKg / 14,
        _ => kg
    };

    private static LineChart Make(DateOnly from, DateOnly to, bool forPrint, Func<double, string> format) => new()
    {
        MinX = from.DayNumber,
        MaxX = to.DayNumber,
        FormatY = format,
        Height = forPrint ? 150 : 200,
        Interactive = !forPrint,
        PrintColours = forPrint
    };
}
