using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kaydence.Controls;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence;

// I add or change a days since counter: a name and the date it all started
public sealed class CounterDialog : Window
{
    private static readonly string[] Ideas =
    {
        "Started HRT", "Alcohol free", "Smoke free", "Vape free", "Sober", "New medication", "Therapy started", "Came out"
    };

    private readonly TextBox _name = new() { Tag = "What are you counting?" };
    private readonly TextBox _date = new() { Tag = "The date it started, like 3/5/2024" };
    private readonly TextBlock _preview;
    private readonly TextBlock _error;

    public CounterDialog(Window owner, Counter? existing)
    {
        Owner = owner;
        Title = existing == null ? "Add a counter" : "Change this counter";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        Result = existing == null
            ? new Counter()
            : new Counter { Id = existing.Id, Name = existing.Name, Since = existing.Since };

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(UiKit.Text(existing == null ? "Count the days since..." : "Change this counter", 16, "Brush.Text", FontWeights.SemiBold));
        var hint = UiKit.Text("A date in the future counts down instead, handy for an appointment or surgery.", 12, "Brush.TextMuted", wrap: true);
        hint.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(hint);

        stack.Children.Add(MakeLabel("Name"));
        _name.Text = Result.Name;
        stack.Children.Add(_name);

        // I offer a few ideas so I don't have to think of a name
        var ideas = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var idea in Ideas)
        {
            var chip = new Button { Content = idea, Style = (Style)FindResource("LinkButton"), Margin = new Thickness(0, 0, 12, 4), FontSize = 12 };
            chip.Click += (_, _) =>
            {
                _name.Text = idea;
                _date.Focus();
            };
            ideas.Children.Add(chip);
        }
        stack.Children.Add(ideas);

        stack.Children.Add(MakeLabel("Since"));
        var dateRow = new DockPanel();
        var today = new Button { Content = "Today", Style = (Style)FindResource("SoftButton"), Margin = new Thickness(8, 0, 0, 0) };
        today.Click += (_, _) => _date.Text = DateTime.Today.ToString("d", CultureInfo.CurrentCulture);
        DockPanel.SetDock(today, Dock.Right);
        dateRow.Children.Add(today);
        dateRow.Children.Add(_date);
        stack.Children.Add(dateRow);
        _date.Text = Result.Since.ToDateTime(TimeOnly.MinValue).ToString("d", CultureInfo.CurrentCulture);

        _preview = UiKit.Text("", 12, "Brush.TextMuted", wrap: true);
        _preview.Margin = new Thickness(2, 8, 0, 0);
        stack.Children.Add(_preview);

        _error = UiKit.Text("", 12, "Brush.Text", wrap: true);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x5A));
        _error.Margin = new Thickness(2, 8, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        stack.Children.Add(_error);

        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("SoftButton"), IsCancel = true, Height = 38 };
        var ok = new Button { Content = "Save", Style = (Style)FindResource("AccentButton"), IsDefault = true, Height = 38 };
        ok.Click += (_, _) => Confirm();
        Grid.SetColumn(ok, 2);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        _date.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        Content = stack;
        Loaded += (_, _) => _name.Focus();
    }

    public Counter Result { get; }

    private static TextBlock MakeLabel(string text)
    {
        var label = UiKit.Text(text, 12, "Brush.TextMuted");
        label.Margin = new Thickness(2, 14, 0, 6);
        return label;
    }

    private static DateOnly? ParseDate(string text) =>
        DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;

    private void UpdatePreview()
    {
        var date = ParseDate(_date.Text);
        _preview.Text = date is { } day
            ? $"{day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)}, {CounterMaths.Describe(day)}"
            : "That doesn't look like a date yet";
    }

    private void Confirm()
    {
        var date = ParseDate(_date.Text);
        if (string.IsNullOrWhiteSpace(_name.Text)) ShowError("Give it a name first");
        else if (date == null) ShowError("That date doesn't look right, try something like 3/5/2024");
        else
        {
            Result.Name = _name.Text.Trim();
            Result.Since = date.Value;
            DialogResult = true;
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}

// I work out the numbers for my counters so the sidebar and the dialog agree
public static class CounterMaths
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static int Days(DateOnly since) => Today.DayNumber - since.DayNumber;

    public static string Describe(DateOnly since)
    {
        var days = Days(since);
        if (days == 0) return "that's today";
        var span = Span(days > 0 ? since : Today, days > 0 ? Today : since);
        return days > 0 ? $"{span} ago" : $"in {span}";
    }

    // I say it the way I'd say it out loud, like 1 year and 2 months
    public static string Span(DateOnly from, DateOnly to)
    {
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (to.Day < from.Day) months--;
        var years = months / 12;
        months %= 12;
        var afterMonths = from.AddMonths(years * 12 + months);
        var days = to.DayNumber - afterMonths.DayNumber;

        var parts = new List<string>();
        if (years > 0) parts.Add(Plural(years, "year"));
        if (months > 0) parts.Add(Plural(months, "month"));
        if (years == 0 && days > 0) parts.Add(Plural(days, "day"));
        return parts.Count == 0 ? "0 days" : string.Join(" and ", parts);
    }

    // I celebrate round numbers and anniversaries
    public static bool IsMilestone(DateOnly since)
    {
        var days = Days(since);
        if (days <= 0) return false;
        if (days is 7 or 30 or 50 || days % 100 == 0) return true;
        return since.Month == Today.Month && since.Day == Today.Day;
    }

    private static string Plural(int count, string word) => $"{count} {word}{(count == 1 ? "" : "s")}";
}
