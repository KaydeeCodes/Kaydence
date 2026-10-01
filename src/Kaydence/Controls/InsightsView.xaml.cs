using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I turn my check-ins into charts: mood over time, by weekday, month and season, my year in pixels and my health numbers
public partial class InsightsView : UserControl
{
    private static readonly (string Value, string Label)[] Ranges =
    {
        ("30", "Last 30 days"), ("90", "Last 3 months"), ("365", "Last 12 months"), ("year", "This year"), ("all", "Everything")
    };

    private DiaryStore? _store;
    private AppSettings? _settings;
    private string _range = "90";
    private int _pixelYear = DateTime.Now.Year;

    public event Action<DateOnly>? OpenDay;

    public InsightsView()
    {
        InitializeComponent();
        RangeHost.Content = UiKit.ChoiceChips(Ranges, _range, v =>
        {
            _range = v;
            Refresh();
        });
    }

    public void Initialise(DiaryStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private (DateOnly From, DateOnly To) RangeDates()
    {
        var first = _store != null && _store.Days.Count > 0 ? _store.Days.Min() : Today;
        return _range switch
        {
            "30" => (Today.AddDays(-29), Today),
            "90" => (Today.AddDays(-89), Today),
            "365" => (Today.AddDays(-364), Today),
            "year" => (new DateOnly(Today.Year, 1, 1), Today),
            _ => (first < Today ? first : Today.AddDays(-29), Today)
        };
    }

    public void Refresh()
    {
        if (_store == null || _settings == null) return;
        Body.Children.Clear();

        var (from, to) = RangeDates();
        var days = _store.Days.Where(d => d >= from && d <= to).OrderBy(d => d)
            .Select(d => (Day: d, CheckIn: _store.LoadDay(d).CheckIn)).ToList();

        Log.Info("Insights", $"Showing {_range}: {days.Count} days from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        AddSummary(days);
        AddMoodCharts(days, from, to);
        AddYearInPixels();
        AddHealth(days, from, to);
        if (!_settings.HiddenSections.Contains("Cycle")) AddCycle();
    }

    private void AddSummary(List<(DateOnly Day, CheckIn CheckIn)> days)
    {
        var (current, longest) = Streaks();
        var moods = days.Where(d => d.CheckIn.Mood.HasValue).Select(d => d.CheckIn.Mood!.Value).ToList();
        var average = moods.Count > 0 ? moods.Average() : (double?)null;

        var weekdays = days.Where(d => d.CheckIn.Mood.HasValue)
            .GroupBy(d => d.Day.DayOfWeek)
            .Select(g => (Day: g.Key, Average: g.Average(x => x.CheckIn.Mood!.Value), Count: g.Count()))
            .Where(g => g.Count >= 2)
            .OrderByDescending(g => g.Average)
            .ToList();

        var wrap = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        wrap.Children.Add(Stat(days.Count.ToString("N0", CultureInfo.CurrentCulture), days.Count == 1 ? "day written" : "days written", "in this time"));
        wrap.Children.Add(Stat(current.ToString(CultureInfo.CurrentCulture), "day streak",
            longest > current ? $"Your best is {longest} days" : "Your best streak yet"));
        if (average is { } mood)
        {
            var level = Moods.Get((int)Math.Round(mood));
            wrap.Children.Add(Stat(mood.ToString("0.0", CultureInfo.CurrentCulture), "average mood", level != null ? $"Mostly {level.Name.ToLowerInvariant()}" : ""));
        }
        if (weekdays.Count > 0)
        {
            var best = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(weekdays[0].Day);
            wrap.Children.Add(Stat(best, "is your best day", "Going by your mood"));
        }
        Body.Children.Add(wrap);
    }

    private static FrameworkElement Stat(string big, string small, string note)
    {
        var stack = new StackPanel();
        var top = new WrapPanel();
        var number = UiKit.Text(big, 24, "Brush.Accent", FontWeights.SemiBold);
        var words = UiKit.Text(small, 13, "Brush.Text", FontWeights.SemiBold);
        words.Margin = new Thickness(8, 0, 0, 4);
        words.VerticalAlignment = VerticalAlignment.Bottom;
        top.Children.Add(number);
        top.Children.Add(words);
        stack.Children.Add(top);
        if (note.Length > 0) stack.Children.Add(UiKit.Text(note, 12, "Brush.TextMuted"));
        var card = UiKit.Card(stack, new Thickness(16, 12, 16, 12));
        card.MinWidth = 200;
        card.Margin = new Thickness(0, 0, 10, 10);
        return card;
    }

    // I count days in a row with anything written, today counts if I've started, otherwise I start from yesterday
    private (int Current, int Longest) Streaks()
    {
        if (_store == null || _store.Days.Count == 0) return (0, 0);
        var set = _store.Days.ToHashSet();
        var start = set.Contains(Today) ? Today : Today.AddDays(-1);
        var current = 0;
        for (var day = start; set.Contains(day); day = day.AddDays(-1)) current++;

        var longest = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in set.OrderBy(d => d))
        {
            run = previous is { } p && day.DayNumber - p.DayNumber == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day;
        }
        return (current, longest);
    }

    private void AddMoodCharts(List<(DateOnly Day, CheckIn CheckIn)> days, DateOnly from, DateOnly to)
    {
        var moods = days.Where(d => d.CheckIn.Mood.HasValue).Select(d => (d.Day, Mood: d.CheckIn.Mood!.Value)).ToList();
        Body.Children.Add(UiKit.SectionLabel("Mood over time"));
        if (moods.Count == 0)
        {
            Body.Children.Add(Empty("Once you pick a mood in your check-in, your mood chart shows up here."));
            return;
        }

        var accent = (TryFindResource("Brush.Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0x7B, 0x4F, 0xB8);
        var chart = new LineChart
        {
            MinX = from.DayNumber,
            MaxX = to.DayNumber,
            MinY = 0.6,
            MaxY = 5.4,
            Ticks = new double[] { 1, 2, 3, 4, 5 },
            FormatY = v => Moods.Get((int)Math.Round(v))?.Name ?? "",
            FormatTip = v => v % 1 == 0 ? Moods.Get((int)v)?.Name ?? "" : v.ToString("0.0", CultureInfo.CurrentCulture)
        };
        chart.Series.Add(new ChartSeries
        {
            Name = "Mood",
            Color = accent,
            Thickness = 1.4,
            Opacity = 0.55,
            Points = moods.Select(m => new Point(m.Day.DayNumber, m.Mood)).ToList()
        });
        // I smooth my mood over the week before so I can see the trend, not just the bumps
        var smooth = moods.Select(m =>
        {
            var window = moods.Where(o => o.Day.DayNumber > m.Day.DayNumber - 7 && o.Day <= m.Day).Select(o => o.Mood);
            return new Point(m.Day.DayNumber, Math.Round(window.Average(), 2));
        }).ToList();
        chart.Series.Add(new ChartSeries { Name = "Week average", Color = accent, Thickness = 3, ShowDots = false, Points = smooth });
        Body.Children.Add(ChartCard(chart, "Each dot is a day, the thick line is your average over the week before it."));

        var wrap = new WrapPanel();
        var weekOrder = MonthCalendar.MondayFirst
            ? new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            : new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
        var names = CultureInfo.CurrentCulture.DateTimeFormat;
        var byWeekday = weekOrder.Select(day =>
        {
            var set = moods.Where(m => m.Day.DayOfWeek == day).Select(m => m.Mood).ToList();
            return MoodBar(names.GetAbbreviatedDayName(day), set, names.GetDayName(day));
        }).ToList();
        wrap.Children.Add(ChartPanel("By day of the week", BarChart.Build(byWeekday, 5, v => v.ToString("0.0", CultureInfo.CurrentCulture)), 420));

        var seasons = new[] { ("Winter", new[] { 12, 1, 2 }), ("Spring", new[] { 3, 4, 5 }), ("Summer", new[] { 6, 7, 8 }), ("Autumn", new[] { 9, 10, 11 }) };
        var bySeason = seasons.Select(s =>
        {
            var set = moods.Where(m => s.Item2.Contains(m.Day.Month)).Select(m => m.Mood).ToList();
            return MoodBar(s.Item1, set, s.Item1);
        }).ToList();
        wrap.Children.Add(ChartPanel("By season", BarChart.Build(bySeason, 5, v => v.ToString("0.0", CultureInfo.CurrentCulture)), 420));
        Body.Children.Add(wrap);

        var months = new List<BarChart.Bar>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var start = month;
            var set = moods.Where(m => m.Day.Year == start.Year && m.Day.Month == start.Month).Select(m => m.Mood).ToList();
            var label = months.Count == 0 || start.Month == 1 ? start.ToString("MMM yy", CultureInfo.CurrentCulture) : start.ToString("MMM", CultureInfo.CurrentCulture);
            months.Add(MoodBar(label, set, start.ToString("MMMM yyyy", CultureInfo.CurrentCulture)));
        }
        if (months.Count > 24) months = months.Skip(months.Count - 24).ToList();
        if (months.Count > 1) Body.Children.Add(ChartPanel("By month", BarChart.Build(months, 5, v => v.ToString("0.0", CultureInfo.CurrentCulture)), double.NaN));
    }

    private static BarChart.Bar MoodBar(string label, List<int> moods, string longName)
    {
        if (moods.Count == 0) return new BarChart.Bar(label, null, $"{longName}: no moods picked");
        var average = moods.Average();
        var colour = Moods.Get((int)Math.Round(average))?.Color;
        return new BarChart.Bar(label, average, $"{longName}: {average.ToString("0.0", CultureInfo.CurrentCulture)} from {moods.Count} {(moods.Count == 1 ? "day" : "days")}", colour);
    }

    // I colour one square for every day of the year by my mood, the classic year in pixels
    private void AddYearInPixels()
    {
        if (_store == null) return;
        var header = new DockPanel { Margin = new Thickness(2, 22, 0, 10) };
        var arrows = new StackPanel { Orientation = Orientation.Horizontal };
        var back = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE76B", ToolTip = "Previous year", Width = 26, Height = 26 };
        var next = new Button { Style = (Style)FindResource("IconButton"), Content = "\uE76C", ToolTip = "Next year", Width = 26, Height = 26 };
        back.Click += (_, _) =>
        {
            _pixelYear--;
            Refresh();
        };
        next.Click += (_, _) =>
        {
            _pixelYear++;
            Refresh();
        };
        next.IsEnabled = _pixelYear < Today.Year;
        var yearText = UiKit.Text(_pixelYear.ToString(CultureInfo.InvariantCulture), 13, "Brush.Text", FontWeights.SemiBold);
        yearText.VerticalAlignment = VerticalAlignment.Center;
        yearText.Margin = new Thickness(6, 0, 6, 0);
        arrows.Children.Add(back);
        arrows.Children.Add(yearText);
        arrows.Children.Add(next);
        DockPanel.SetDock(arrows, Dock.Right);
        header.Children.Add(arrows);
        var title = UiKit.Text("YOUR YEAR IN PIXELS", 11, "Brush.TextFaint", FontWeights.Bold);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        Body.Children.Add(header);

        const double cell = 15;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        for (var m = 0; m < 12; m++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell + 8) });
        for (var r = 0; r < 32; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? 20 : cell + 3) });

        var culture = CultureInfo.CurrentCulture;
        for (var m = 1; m <= 12; m++)
        {
            var name = UiKit.Text(culture.DateTimeFormat.GetAbbreviatedMonthName(m)[..1], 11, "Brush.TextFaint", FontWeights.SemiBold);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.ToolTip = culture.DateTimeFormat.GetMonthName(m);
            Grid.SetColumn(name, m);
            grid.Children.Add(name);
        }
        for (var d = 1; d <= 31; d++)
        {
            if (d % 5 != 1 && d != 31) continue;
            var number = UiKit.Text(d.ToString(CultureInfo.InvariantCulture), 10, "Brush.TextFaint");
            number.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(number, d);
            grid.Children.Add(number);
        }

        for (var m = 1; m <= 12; m++)
        {
            for (var d = 1; d <= DateTime.DaysInMonth(_pixelYear, m); d++)
            {
                var day = new DateOnly(_pixelYear, m, d);
                var summary = _store.GetSummary(day);
                var level = Moods.Get(summary?.Mood);
                var square = new Border { Width = cell, Height = cell, CornerRadius = new CornerRadius(4) };
                if (level != null) square.Background = new SolidColorBrush(level.Color);
                else if (summary != null) square.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
                else
                {
                    square.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
                    if (day > Today) square.Opacity = 0.45;
                }

                var tip = day.ToString("dddd d MMMM", culture);
                square.ToolTip = level != null ? $"{tip}: {level.Name}" : summary != null ? $"{tip}: written, no mood picked" : tip;
                if (summary != null) UiKit.Clickable(square, () => OpenDay?.Invoke(day));

                Grid.SetColumn(square, m);
                Grid.SetRow(square, d);
                grid.Children.Add(square);
            }
        }

        var key = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var level in Moods.All.Reverse())
        {
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(level.Color), Margin = new Thickness(0, 0, 6, 0) };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            item.Children.Add(dot);
            item.Children.Add(UiKit.Text(level.Name, 12, "Brush.TextMuted"));
            key.Children.Add(item);
        }

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(key);
        var card = UiKit.Card(stack, new Thickness(18, 14, 18, 14));
        card.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(card);
    }

    private void AddHealth(List<(DateOnly Day, CheckIn CheckIn)> days, DateOnly from, DateOnly to)
    {
        var charts = HealthCharts.Build(days, from, to, _settings?.WeightUnit ?? "kg", forPrint: false);
        Body.Children.Add(UiKit.SectionLabel("Your health"));
        if (charts.Count == 0)
        {
            Body.Children.Add(Empty("Sleep, weight, blood pressure and pain charts show up here once you've filled them in on a few days."));
            return;
        }
        var wrap = new WrapPanel();
        foreach (var (title, note, chart) in charts) wrap.Children.Add(ChartPanel(title, chart, 520, note));
        Body.Children.Add(wrap);
    }

    // I show my cycle history as bars, with the period part in pink, plus my averages and a rough guess at what's next
    private void AddCycle()
    {
        if (_store == null) return;
        Body.Children.Add(UiKit.SectionLabel("Your cycle"));
        var summary = CycleService.Analyse(_store, Today);
        if (summary.Cycles.Count == 0)
        {
            Body.Children.Add(Empty("Once you log your flow on a few period days, your cycle lengths and a guess at your next period show up here."));
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        var pink = Color.FromRgb(0xEC, 0x5F, 0xA0);
        var wrap = new WrapPanel();
        var known = summary.Cycles.Count(c => c.Length.HasValue);
        wrap.Children.Add(Stat(summary.AverageCycle.ToString("0", culture), "day cycle", known > 0 ? $"Average of your last {Math.Min(6, known)}" : "A typical guess until you've logged more"));
        wrap.Children.Add(Stat(summary.AveragePeriod.ToString("0", culture), "day period", "On average"));
        if (summary.NextStart is { } next)
        {
            wrap.Children.Add(Stat(next.ToString("d MMM", culture), "next period", "A rough guess"));
            var ovulation = next.AddDays(-14);
            wrap.Children.Add(Stat($"{ovulation.AddDays(-5).ToString("d MMM", culture)} to {ovulation.AddDays(1).ToString("d MMM", culture)}",
                "", "Possible fertile days, a rough guess"));
        }
        Body.Children.Add(wrap);

        var rows = new StackPanel();
        var longest = Math.Max(35, summary.Cycles.Max(c => c.Length ?? c.PeriodDays));
        foreach (var cycle in summary.Cycles.TakeLast(12).Reverse())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var date = UiKit.Text(cycle.Start.ToString("d MMM yyyy", culture), 12, "Brush.TextMuted");
            date.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(date);

            var bars = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            var scale = 1.0 / longest;
            var whole = new Border { CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
            whole.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            var period = new Border { CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(pink) };
            bars.Children.Add(whole);
            bars.Children.Add(period);
            Grid.SetColumn(bars, 1);
            row.Children.Add(bars);
            row.SizeChanged += (_, _) =>
            {
                var room = Math.Max(0, row.ActualWidth - 240);
                whole.Width = room * (cycle.Length ?? cycle.PeriodDays) * scale;
                period.Width = room * cycle.PeriodDays * scale;
            };

            var words = cycle.Length is int length ? $"{length} days, period {cycle.PeriodDays}" : $"Now, period {cycle.PeriodDays} days";
            var label = UiKit.Text(words, 12, "Brush.Text");
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(label, 2);
            row.Children.Add(label);
            rows.Children.Add(row);
        }
        var note = UiKit.Text("Guesses are based on your last few cycles. They're rough, not medical advice, and must never be used as contraception.", 11.5, "Brush.TextFaint", wrap: true);
        note.Margin = new Thickness(0, 6, 0, 0);
        rows.Children.Add(note);
        Body.Children.Add(ChartPanel("Cycle history", rows, double.NaN, "Newest first. The pink part is your period."));
    }

    private static FrameworkElement ChartCard(FrameworkElement chart, string note)
    {
        var stack = new StackPanel();
        stack.Children.Add(chart);
        var small = UiKit.Text(note, 12, "Brush.TextMuted", wrap: true);
        small.Margin = new Thickness(4, 8, 0, 0);
        stack.Children.Add(small);
        var card = UiKit.Card(stack, new Thickness(14, 14, 18, 12));
        // I line this card up with the smaller ones below it and leave the same gap underneath
        card.Margin = new Thickness(0, 0, 10, 10);
        return card;
    }

    private static FrameworkElement ChartPanel(string title, FrameworkElement content, double width, string note = "")
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(title, 13.5, "Brush.Text", FontWeights.SemiBold));
        if (note.Length > 0)
        {
            var small = UiKit.Text(note, 12, "Brush.TextMuted", wrap: true);
            small.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(small);
        }
        content.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(content);
        var card = UiKit.Card(stack, new Thickness(16, 14, 16, 12));
        card.Margin = new Thickness(0, 0, 10, 10);
        if (!double.IsNaN(width)) card.Width = width;
        return card;
    }

    private static FrameworkElement Empty(string text)
    {
        var note = UiKit.Text(text, 13, "Brush.TextMuted", wrap: true);
        var card = UiKit.Card(note, new Thickness(16, 14, 16, 14));
        card.Margin = new Thickness(0, 0, 10, 10);
        return card;
    }
}
