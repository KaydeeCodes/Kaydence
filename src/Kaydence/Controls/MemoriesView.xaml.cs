using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I bring back this day from past years and list every milestone I've marked
public partial class MemoriesView : UserControl
{
    private DiaryStore? _store;

    public event Action<DateOnly>? OpenDay;

    public MemoriesView()
    {
        InitializeComponent();
    }

    public void Initialise(DiaryStore store) => _store = store;

    public void Refresh()
    {
        if (_store == null) return;
        Body.Children.Clear();
        var culture = CultureInfo.CurrentCulture;
        var today = DateOnly.FromDateTime(DateTime.Now);

        Body.Children.Add(UiKit.SectionLabel("On this day"));
        var pastDays = _store.Days
            .Where(d => d.Month == today.Month && d.Day == today.Day && d.Year < today.Year)
            .OrderByDescending(d => d)
            .ToList();
        if (pastDays.Count == 0)
        {
            Body.Children.Add(UiKit.Text("Nothing from this day in past years yet. Next year there will be.", 13, "Brush.TextMuted"));
        }
        else
        {
            var wrap = new WrapPanel();
            foreach (var day in pastDays)
            {
                var entry = _store.LoadDay(day);
                var years = today.Year - day.Year;
                var stack = new StackPanel();
                var picture = DayText.ImageNames(entry).FirstOrDefault();
                if (picture != null && UiKit.Thumb(_store.ImagePath(day, picture), 140) is { } thumb)
                {
                    thumb.Margin = new Thickness(0, 0, 0, 10);
                    stack.Children.Add(thumb);
                }
                stack.Children.Add(UiKit.Text($"{years} {(years == 1 ? "year" : "years")} ago  ·  {day.ToString("d MMM yyyy", culture)}".ToUpperInvariant(), 11, "Brush.Accent", FontWeights.Bold));
                var text = DayText.Get(entry);
                var snippet = UiKit.Text(text.Length > 0 ? DayText.Snippet(text, 160) : "A day you checked in", 13, "Brush.Text", wrap: true);
                snippet.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(snippet);

                var card = UiKit.Card(stack);
                card.Width = 300;
                card.Margin = new Thickness(0, 0, 14, 14);
                UiKit.Clickable(card, () => OpenDay?.Invoke(day));
                wrap.Children.Add(card);
            }
            Body.Children.Add(wrap);
        }

        Body.Children.Add(UiKit.SectionLabel("Your milestones"));
        var milestones = _store.Days
            .Where(d => _store.GetSummary(d)?.Milestone == true)
            .OrderByDescending(d => d)
            .ToList();
        if (milestones.Count == 0)
        {
            Body.Children.Add(UiKit.Text("Tick \"Today is a milestone\" in the Transition section and the day shows up here.", 13, "Brush.TextMuted", wrap: true));
            return;
        }

        var year = 0;
        foreach (var day in milestones)
        {
            if (day.Year != year)
            {
                year = day.Year;
                var yearLabel = UiKit.Text(year.ToString(culture), 20, "Brush.Accent", FontWeights.SemiBold);
                yearLabel.Margin = new Thickness(2, 10, 0, 10);
                Body.Children.Add(yearLabel);
            }
            Body.Children.Add(MilestoneCard(day, _store.LoadDay(day)));
        }
    }

    private Border MilestoneCard(DateOnly day, DayEntry entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(UiKit.Text(day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture), 12, "Brush.TextMuted", FontWeights.SemiBold));
        var text = DayText.Get(entry);
        var title = CheckIn.Has(entry.CheckIn.Transition.Milestone) ? entry.CheckIn.Transition.Milestone! : DayText.Snippet(text, 80);
        stack.Children.Add(UiKit.Text("★  " + title, 16, "Brush.Text", FontWeights.SemiBold, wrap: true));
        if (text.Length > 0 && CheckIn.Has(entry.CheckIn.Transition.Milestone))
        {
            var more = UiKit.Text(DayText.Snippet(text, 180), 13, "Brush.TextMuted", wrap: true);
            more.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(more);
        }
        grid.Children.Add(stack);

        var picture = DayText.ImageNames(entry).FirstOrDefault();
        if (picture != null && _store != null && UiKit.Thumb(_store.ImagePath(day, picture), 84) is { } thumb)
        {
            thumb.Width = 120;
            thumb.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(thumb, 1);
            grid.Children.Add(thumb);
        }

        var card = UiKit.Card(grid);
        card.Margin = new Thickness(0, 0, 0, 12);
        card.MaxWidth = 900;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.MinWidth = 600;
        UiKit.Clickable(card, () => OpenDay?.Invoke(day));
        return card;
    }
}
