using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I show the whole month at a glance, each day tinted by how I felt
public partial class MonthView : UserControl
{
    private DiaryStore? _store;
    private AppSettings? _settings;
    private DateOnly _month;

    public event Action<DateOnly>? OpenDay;

    public MonthView()
    {
        InitializeComponent();
    }

    public void Initialise(DiaryStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    public void ShowMonthOf(DateOnly day)
    {
        _month = new DateOnly(day.Year, day.Month, 1);
        Render();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => ShowMonthOf(_month.AddMonths(-1));

    private void Next_Click(object sender, RoutedEventArgs e) => ShowMonthOf(_month.AddMonths(1));

    private void ThisMonth_Click(object sender, RoutedEventArgs e) => ShowMonthOf(DateOnly.FromDateTime(DateTime.Now));

    private void Render()
    {
        if (_store == null) return;
        var culture = CultureInfo.CurrentCulture;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var mondayFirst = _settings?.WeekStartsMonday != false;
        var first = mondayFirst ? DayOfWeek.Monday : DayOfWeek.Sunday;

        Heading.Text = _month.ToString("MMMM yyyy", culture);

        DayNames.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var name = culture.DateTimeFormat.GetDayName((DayOfWeek)(((int)first + i) % 7));
            var header = new Border { Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0.5) };
            header.SetResourceReference(Border.BackgroundProperty, "Brush.Card");
            header.Child = UiKit.Text(name, 12, "Brush.TextMuted", FontWeights.Bold);
            DayNames.Children.Add(header);
        }

        var offset = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        var start = _month.AddDays(-offset);
        var daysInMonth = DateTime.DaysInMonth(_month.Year, _month.Month);
        var moodCounts = new int[6];
        var written = 0;
        var pictures = 0;
        var words = 0;
        var streak = 0;
        var bestStreak = 0;
        (DateOnly Day, DayEntry Entry)? highlight = null;

        Cells.Children.Clear();
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var inMonth = day.Month == _month.Month;
            var summary = _store.GetSummary(day);
            DayEntry? entry = summary != null ? _store.LoadDay(day) : null;
            Cells.Children.Add(BuildCell(day, inMonth, day == today, summary, entry));

            if (!inMonth) continue;
            if (summary != null && entry != null)
            {
                written++;
                streak++;
                bestStreak = Math.Max(bestStreak, streak);
                if (summary.Mood is int mood) moodCounts[mood]++;
                var names = DayText.ImageNames(entry).ToList();
                pictures += names.Count;
                words += DayText.WordCount(DayText.Get(entry));
                var better = highlight == null || (summary.Mood ?? 0) > (highlight.Value.Entry.CheckIn.Mood ?? 0)
                             || ((summary.Mood ?? 0) == (highlight.Value.Entry.CheckIn.Mood ?? 0) && names.Count > 0 && !DayText.ImageNames(highlight.Value.Entry).Any());
                if (better) highlight = (day, entry);
            }
            else
            {
                streak = 0;
            }
        }

        Subtitle.Text = $"{written} of {daysInMonth} days written";
        BuildSide(moodCounts, written, bestStreak, pictures, words, highlight);
    }

    private UIElement BuildCell(DateOnly day, bool inMonth, bool isToday, DaySummary? summary, DayEntry? entry)
    {
        var cell = new Border { Margin = new Thickness(0.5), MinHeight = 112 };
        cell.SetResourceReference(Border.BackgroundProperty, "Brush.Card");

        var grid = new Grid();
        var mood = Moods.Get(summary?.Mood);
        if (mood != null && inMonth)
        {
            grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x24, mood.Color.R, mood.Color.G, mood.Color.B)) });
            grid.Children.Add(new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(mood.Color) });
        }

        var stack = new StackPanel { Margin = new Thickness(10, 8, 8, 8) };
        var top = new Grid();
        var number = UiKit.Text(day.Day.ToString(CultureInfo.CurrentCulture), 13, isToday ? "Brush.Accent" : inMonth ? "Brush.Text" : "Brush.TextFaint",
            isToday || inMonth ? FontWeights.SemiBold : FontWeights.Normal);
        if (isToday)
        {
            number.Text = number.Text + "  Today";
        }
        top.Children.Add(number);
        if (mood != null && inMonth)
        {
            var face = UiKit.Face(mood.Value, 18);
            face.HorizontalAlignment = HorizontalAlignment.Right;
            top.Children.Add(face);
        }
        if (summary?.Milestone == true && inMonth)
        {
            var star = UiKit.Text("\uE735", 11, "Brush.Accent");
            star.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            star.HorizontalAlignment = HorizontalAlignment.Right;
            star.Margin = new Thickness(0, 2, 24, 0);
            top.Children.Add(star);
        }
        stack.Children.Add(top);
        if (inMonth && UiKit.PeriodTag(day, summary?.Flow) is { } period) stack.Children.Add(period);

        if (entry != null && inMonth)
        {
            var text = DayText.Get(entry);
            if (text.Length > 0)
            {
                var snippet = UiKit.Text(DayText.Snippet(text, 70), 12, "Brush.TextMuted", wrap: true);
                snippet.MaxHeight = 34;
                snippet.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(snippet);
            }
            var picture = DayText.ImageNames(entry).FirstOrDefault();
            if (picture != null && _store != null && UiKit.Thumb(_store.ImagePath(day, picture), 34, 240) is { } thumb)
            {
                thumb.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(thumb);
            }
        }
        else if (inMonth && day <= DateOnly.FromDateTime(DateTime.Now))
        {
            var none = UiKit.Text("No entry", 12, "Brush.TextFaint");
            none.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(none);
        }

        grid.Children.Add(stack);
        cell.Child = grid;
        if (!inMonth) cell.Opacity = 0.55;
        UiKit.Clickable(cell, () => OpenDay?.Invoke(day));
        return cell;
    }

    private void BuildSide(int[] moodCounts, int written, int bestStreak, int pictures, int words, (DateOnly Day, DayEntry Entry)? highlight)
    {
        Side.Children.Clear();

        var moods = new StackPanel();
        var title = UiKit.Text("How the month felt", 13, "Brush.Text", FontWeights.SemiBold);
        title.Margin = new Thickness(0, 0, 0, 12);
        moods.Children.Add(title);
        var max = Math.Max(1, moodCounts.Max());
        foreach (var mood in Moods.All)
        {
            var count = moodCounts[mood.Value];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            row.Children.Add(UiKit.Face(mood.Value, 20));
            var name = UiKit.Text(mood.Name, 12.5);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(count, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(max - count, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(4) };
            track.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            if (count > 0) bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(mood.Color) });
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var number = UiKit.Text(count.ToString(CultureInfo.CurrentCulture), 12.5, "Brush.Text", FontWeights.SemiBold);
            number.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(number, 3);
            row.Children.Add(number);
            moods.Children.Add(row);
        }
        Side.Children.Add(UiKit.Card(moods));

        Side.Children.Add(StatRow(("days written", written), ("longest streak", bestStreak)));
        Side.Children.Add(StatRow(("pictures", pictures), ("words", words)));

        if (highlight is { } best)
        {
            var stack = new StackPanel();
            var heading = UiKit.Text("Highlight of the month", 13, "Brush.Text", FontWeights.SemiBold);
            heading.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(heading);
            var picture = DayText.ImageNames(best.Entry).FirstOrDefault();
            if (picture != null && _store != null && UiKit.Thumb(_store.ImagePath(best.Day, picture), 120) is { } thumb)
            {
                thumb.Margin = new Thickness(0, 0, 0, 10);
                stack.Children.Add(thumb);
            }
            stack.Children.Add(UiKit.Text(best.Day.ToString("dddd d MMMM", CultureInfo.CurrentCulture), 12.5, "Brush.Accent", FontWeights.SemiBold));
            var text = DayText.Get(best.Entry);
            if (text.Length > 0) stack.Children.Add(UiKit.Text(DayText.Snippet(text, 140), 12.5, "Brush.Text", wrap: true));
            var card = UiKit.Card(stack);
            card.Margin = new Thickness(0, 12, 0, 0);
            UiKit.Clickable(card, () => OpenDay?.Invoke(best.Day));
            Side.Children.Add(card);
        }
    }

    private static UIElement StatRow((string Label, int Value) left, (string Label, int Value) right)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var a = Stat(left);
        var b = Stat(right);
        Grid.SetColumn(b, 2);
        grid.Children.Add(a);
        grid.Children.Add(b);
        return grid;
    }

    private static Border Stat((string Label, int Value) stat)
    {
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(stat.Value.ToString("N0", CultureInfo.CurrentCulture), 20, "Brush.Text", FontWeights.SemiBold));
        stack.Children.Add(UiKit.Text(stat.Label, 11.5, "Brush.TextMuted"));
        return UiKit.Card(stack, new Thickness(12, 10, 12, 10));
    }
}
