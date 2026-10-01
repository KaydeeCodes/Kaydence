using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I find anything I've ever written, and can narrow it down by mood or milestones
public partial class SearchView : UserControl
{
    private const int MaxResults = 150;
    private static readonly Brush MatchBrush = MakeMatchBrush();

    private readonly DispatcherTimer _debounce;
    private readonly HashSet<int> _moods = new();
    private DiaryStore? _store;

    public event Action<DateOnly>? OpenDay;

    public SearchView()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) => RunSearch();

        foreach (var mood in Moods.All)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(UiKit.Face(mood.Value, 16));
            content.Children.Add(new TextBlock { Text = mood.Name, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var chip = new ToggleButton { Content = content, Style = (Style)Application.Current.FindResource("Chip") };
            var value = mood.Value;
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) _moods.Add(value);
                else _moods.Remove(value);
                RunSearch();
            };
            MoodFilters.Children.Add(chip);
        }
    }

    public void Initialise(DiaryStore store) => _store = store;

    public void FocusSearch()
    {
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        QueryBox.SelectAll();
        RunSearch();
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Query_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        RunSearch();
        e.Handled = true;
    }

    private void Filter_Click(object sender, RoutedEventArgs e) => RunSearch();

    private void RunSearch()
    {
        _debounce.Stop();
        Results.Children.Clear();
        if (_store == null) return;

        var query = QueryBox.Text.Trim();
        var milestonesOnly = MilestonesOnly.IsChecked == true;
        if (query.Length == 0 && _moods.Count == 0 && !milestonesOnly)
        {
            ResultCount.Text = "Type a word or pick a mood to start";
            return;
        }

        var found = 0;
        foreach (var day in _store.Days.OrderByDescending(d => d))
        {
            var summary = _store.GetSummary(day);
            if (summary == null) continue;
            if (_moods.Count > 0 && (summary.Mood is not int mood || !_moods.Contains(mood))) continue;
            if (milestonesOnly && !summary.Milestone) continue;

            var entry = _store.LoadDay(day);
            var text = DayText.SearchText(entry);
            var index = query.Length == 0 ? 0 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            found++;
            if (found <= MaxResults) Results.Children.Add(ResultCard(day, entry, text, index, query));
        }

        // I only note how many days matched, never what I searched for
        Log.Debug("Search", $"Search found {found} days");
        ResultCount.Text = found switch
        {
            0 => "Nothing found",
            1 => "1 day found",
            > MaxResults => $"{found} days found, showing the newest {MaxResults}",
            _ => $"{found} days found"
        };
    }

    private Border ResultCard(DateOnly day, DayEntry entry, string text, int index, string query)
    {
        var culture = CultureInfo.CurrentCulture;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var date = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        date.Children.Add(Centered(UiKit.Text(day.ToString("ddd", culture).ToUpperInvariant(), 11, "Brush.TextMuted", FontWeights.Bold)));
        date.Children.Add(Centered(UiKit.Text(day.Day.ToString(culture), 26, "Brush.Text", FontWeights.SemiBold)));
        date.Children.Add(Centered(UiKit.Text(day.ToString("MMM yyyy", culture).ToUpperInvariant(), 10.5, "Brush.TextMuted", FontWeights.Bold)));
        if (entry.CheckIn.Mood is int mood)
        {
            var face = UiKit.Face(mood, 20);
            face.Margin = new Thickness(0, 6, 0, 0);
            date.Children.Add(face);
        }
        grid.Children.Add(date);

        var content = new StackPanel { Margin = new Thickness(12, 2, 12, 0) };
        content.Children.Add(UiKit.Text(day.ToString("dddd d MMMM yyyy", culture), 15, "Brush.Text", FontWeights.SemiBold));
        content.Children.Add(Highlighted(text, index, query));
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        var picture = DayText.ImageNames(entry).FirstOrDefault();
        if (picture != null && _store != null && UiKit.Thumb(_store.ImagePath(day, picture), 80) is { } thumb)
        {
            thumb.Width = 110;
            Grid.SetColumn(thumb, 2);
            grid.Children.Add(thumb);
        }

        var card = UiKit.Card(grid);
        card.Margin = new Thickness(0, 0, 0, 10);
        UiKit.Clickable(card, () => OpenDay?.Invoke(day));
        return card;
    }

    // I show a bit of text around the match with the word I searched for highlighted
    private static TextBlock Highlighted(string text, int index, string query)
    {
        var start = Math.Max(0, index - 80);
        var length = Math.Min(text.Length - start, 260);
        var piece = Regex.Replace(text.Substring(start, length), @"\s+", " ").Trim();
        if (start > 0) piece = "..." + piece;
        if (start + length < text.Length) piece += "...";

        var block = UiKit.Text("", 13, "Brush.TextMuted", wrap: true);
        block.Margin = new Thickness(0, 4, 0, 0);
        var at = query.Length == 0 ? -1 : piece.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            block.Inlines.Add(new Run(piece));
            return block;
        }
        block.Inlines.Add(new Run(piece[..at]));
        var match = new Run(piece.Substring(at, query.Length)) { Background = MatchBrush };
        match.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
        block.Inlines.Add(match);
        block.Inlines.Add(new Run(piece[(at + query.Length)..]));
        return block;
    }

    private static FrameworkElement Centered(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        return element;
    }

    private static Brush MakeMatchBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xD6, 0x50));
        brush.Freeze();
        return brush;
    }
}
