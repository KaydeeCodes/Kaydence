using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Kaydence.Models;

namespace Kaydence.Controls;

// I built my own month view so every day with an entry gets a little mood coloured dot like Outlook
public partial class MonthCalendar : UserControl
{
    private readonly Button[] _cells = new Button[42];
    private DateOnly _month;
    private DateOnly _selected;

    // I can start my weeks on Monday or Sunday from settings
    public static bool MondayFirst { get; set; } = true;

    // I only show period marks when I've switched the cycle tracker on
    public static bool ShowCycle { get; set; }
    public static HashSet<DateOnly> PredictedPeriod { get; set; } = new();
    private static readonly Color PeriodColour = Color.FromRgb(0xEC, 0x5F, 0xA0);

    public Func<DateOnly, DaySummary?>? SummaryProvider { get; set; }

    public event Action<DateOnly>? DateSelected;
    public event Action<DateOnly>? DeleteRequested;

    public MonthCalendar()
    {
        InitializeComponent();

        for (var i = 0; i < _cells.Length; i++)
        {
            var cell = new Button { Style = (Style)FindResource("DayButton"), Height = 34 };
            cell.Click += Cell_Click;
            cell.ContextMenu = BuildMenu(cell);
            _cells[i] = cell;
            DaysGrid.Children.Add(cell);
        }

        _selected = DateOnly.FromDateTime(DateTime.Now);
        _month = new DateOnly(_selected.Year, _selected.Month, 1);
        MouseWheel += (_, e) => ShowMonth(_month.AddMonths(e.Delta > 0 ? -1 : 1));
        Render();
    }

    public DateOnly SelectedDate
    {
        get => _selected;
        set
        {
            _selected = value;
            _month = new DateOnly(value.Year, value.Month, 1);
            Render();
        }
    }

    public void Refresh() => Render();

    private void ShowMonth(DateOnly firstOfMonth)
    {
        _month = firstOfMonth;
        Render();
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(-1));

    private void NextMonth_Click(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(1));

    // I right click a day to open it or delete what I wrote that day
    private ContextMenu BuildMenu(Button cell)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open this day" };
        open.Click += (_, _) =>
        {
            if (cell.Tag is DateOnly day) DateSelected?.Invoke(day);
        };
        var delete = new MenuItem { Header = "Delete this day's entry" };
        delete.Click += (_, _) =>
        {
            if (cell.Tag is DateOnly day) DeleteRequested?.Invoke(day);
        };
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.Opened += (_, _) => delete.IsEnabled = cell.Tag is DateOnly day && SummaryProvider?.Invoke(day) != null;
        return menu;
    }

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateOnly date }) DateSelected?.Invoke(date);
    }

    private void Render()
    {
        MonthTitle.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        var first = MondayFirst ? DayOfWeek.Monday : DayOfWeek.Sunday;
        DayHeaders.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)first + i) % 7));
            var header = new TextBlock
            {
                Text = name.Substring(0, 1).ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
            DayHeaders.Children.Add(header);
        }

        var offset = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        var start = _month.AddDays(-offset);
        var today = DateOnly.FromDateTime(DateTime.Now);

        for (var i = 0; i < _cells.Length; i++)
        {
            var date = start.AddDays(i);
            var cell = _cells[i];
            var inMonth = date.Month == _month.Month;
            var isSelected = date == _selected;
            var isToday = date == today;

            cell.Tag = date;
            cell.ToolTip = date.ToString("dddd d MMMM", CultureInfo.CurrentCulture);

            if (isSelected) cell.SetResourceReference(BackgroundProperty, "Brush.Accent");
            else cell.Background = Brushes.Transparent;

            if (isToday && !isSelected) cell.SetResourceReference(BorderBrushProperty, "Brush.Accent");
            else cell.BorderBrush = Brushes.Transparent;

            cell.Content = BuildCell(date, inMonth, isSelected, isToday, SummaryProvider?.Invoke(date));
        }
    }

    private static UIElement BuildCell(DateOnly date, bool inMonth, bool isSelected, bool isToday, DaySummary? summary)
    {
        var grid = new Grid();

        var number = new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.CurrentCulture),
            FontSize = 13,
            FontWeight = isToday || isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5),
            Opacity = inMonth ? 1 : 0.55
        };
        number.SetResourceReference(TextBlock.ForegroundProperty,
            isSelected ? "Brush.OnAccent" : isToday ? "Brush.Accent" : inMonth ? "Brush.Text" : "Brush.TextFaint");
        grid.Children.Add(number);

        // I put a little pink dot in the corner on period days, and a pink ring where I'm guessing the next one
        var period = ShowCycle && summary?.Flow is "Light" or "Medium" or "Heavy";
        var guessed = ShowCycle && !period && PredictedPeriod.Contains(date);
        if (period || guessed)
        {
            var mark = new Ellipse
            {
                Width = 6,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 4, 0),
                Opacity = inMonth ? 1 : 0.6,
                ToolTip = period ? "Period" : "Period might start around here"
            };
            if (period) mark.Fill = new SolidColorBrush(PeriodColour);
            else
            {
                mark.Stroke = new SolidColorBrush(PeriodColour);
                mark.StrokeThickness = 1.2;
            }
            grid.Children.Add(mark);
        }

        if (summary == null) return grid;

        var mood = Moods.Get(summary.Mood);
        if (summary.Milestone)
        {
            // I mark milestone days with a tiny star instead of a dot
            var star = new TextBlock
            {
                Text = "\uE735",
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2)
            };
            star.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            if (mood != null) star.Foreground = new SolidColorBrush(mood.Color);
            else star.SetResourceReference(TextBlock.ForegroundProperty, isSelected ? "Brush.OnAccent" : "Brush.Accent");
            grid.Children.Add(star);
        }
        else
        {
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 4),
                Opacity = inMonth ? 1 : 0.6
            };
            if (mood != null) dot.Fill = new SolidColorBrush(mood.Color);
            else dot.SetResourceReference(Shape.FillProperty, isSelected ? "Brush.OnAccent" : "Brush.TextMuted");
            if (isSelected)
            {
                dot.SetResourceReference(Shape.StrokeProperty, "Brush.OnAccent");
                dot.StrokeThickness = 1;
            }
            grid.Children.Add(dot);
        }
        return grid;
    }
}
