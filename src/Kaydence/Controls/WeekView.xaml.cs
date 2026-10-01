using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I show seven days side by side so I can see how my week went
public partial class WeekView : UserControl
{
    private DiaryStore? _store;
    private AppSettings? _settings;
    private DateOnly _weekStart;

    public event Action<DateOnly>? OpenDay;

    public WeekView()
    {
        InitializeComponent();
    }

    public void Initialise(DiaryStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    public void ShowWeekOf(DateOnly day)
    {
        _weekStart = StartOfWeek(day);
        Render();
    }

    private DateOnly StartOfWeek(DateOnly day)
    {
        var first = _settings?.WeekStartsMonday == false ? DayOfWeek.Sunday : DayOfWeek.Monday;
        var back = ((int)day.DayOfWeek - (int)first + 7) % 7;
        return day.AddDays(-back);
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = _weekStart.AddDays(-7);
        Render();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = _weekStart.AddDays(7);
        Render();
    }

    private void ThisWeek_Click(object sender, RoutedEventArgs e) => ShowWeekOf(DateOnly.FromDateTime(DateTime.Now));

    private void Render()
    {
        if (_store == null) return;
        var culture = CultureInfo.CurrentCulture;
        var end = _weekStart.AddDays(6);
        Heading.Text = $"{_weekStart.ToString("d MMMM", culture)} to {end.ToString("d MMMM yyyy", culture)}";

        Days.Children.Clear();
        var written = 0;
        for (var i = 0; i < 7; i++)
        {
            var day = _weekStart.AddDays(i);
            var summary = _store.GetSummary(day);
            if (summary != null) written++;
            Days.Children.Add(BuildDay(day, summary));
        }
        Subtitle.Text = written == 0 ? "Nothing written this week yet" : $"{written} of 7 days written";
    }

    private UIElement BuildDay(DateOnly day, DaySummary? summary)
    {
        var culture = CultureInfo.CurrentCulture;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var isToday = day == today;
        var stack = new StackPanel();

        var top = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        var date = new StackPanel();
        date.Children.Add(UiKit.Text(day.ToString("ddd", culture).ToUpperInvariant() + (isToday ? "  TODAY" : ""), 11,
            isToday ? "Brush.Accent" : "Brush.TextMuted", FontWeights.Bold));
        date.Children.Add(UiKit.Text(day.Day.ToString(culture), 26, isToday ? "Brush.Accent" : "Brush.Text", FontWeights.SemiBold));
        if (UiKit.PeriodTag(day, summary?.Flow) is { } period) date.Children.Add(period);
        top.Children.Add(date);
        if (summary?.Mood is int mood)
        {
            var face = UiKit.Face(mood, 30);
            face.HorizontalAlignment = HorizontalAlignment.Right;
            face.VerticalAlignment = VerticalAlignment.Top;
            top.Children.Add(face);
        }
        stack.Children.Add(top);

        if (summary == null)
        {
            stack.Children.Add(UiKit.Text(day > today ? "Not here yet" : "No entry", 13, "Brush.TextFaint"));
            if (day <= today)
            {
                var write = new Button { Content = "Write about this day", Style = (Style)FindResource("LinkButton"), Margin = new Thickness(0, 8, 0, 0) };
                write.Click += (_, _) => OpenDay?.Invoke(day);
                stack.Children.Add(write);
            }
        }
        else
        {
            var entry = _store!.LoadDay(day);
            var text = DayText.Get(entry);
            if (text.Length > 0)
            {
                var snippet = UiKit.Text(DayText.Snippet(text, 280), 13, "Brush.Text", wrap: true);
                snippet.MaxHeight = 150;
                stack.Children.Add(snippet);
            }

            var picture = DayText.ImageNames(entry).FirstOrDefault();
            if (picture != null && UiKit.Thumb(_store.ImagePath(day, picture), 96) is { } thumb)
            {
                thumb.Margin = new Thickness(0, 10, 0, 0);
                stack.Children.Add(thumb);
            }

            var c = entry.CheckIn;
            var facts = new List<string>();
            if (c.Sleep.Hours is double hours) facts.Add($"Sleep {hours:0.#} h");
            if (c.Fitness.Minutes is int minutes) facts.Add($"{minutes} min active");
            if (c.Tasks.Count > 0) facts.Add($"Tasks {c.Tasks.Count(t => t.Done)}/{c.Tasks.Count}");
            if (summary.Milestone) facts.Add("Milestone");
            if (facts.Count > 0)
            {
                var foot = UiKit.Text(string.Join("  ·  ", facts), 11.5, "Brush.TextFaint", wrap: true);
                foot.Margin = new Thickness(0, 10, 0, 0);
                stack.Children.Add(foot);
            }
        }

        var card = UiKit.Card(stack);
        card.Margin = new Thickness(5, 0, 5, 0);
        card.MinHeight = 380;
        if (isToday) card.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        if (day <= today || summary != null) UiKit.Clickable(card, () => OpenDay?.Invoke(day));
        else card.Opacity = 0.6;
        return card;
    }
}
