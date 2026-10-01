using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kaydence.Services;

namespace Kaydence.Controls;

// I keep the little building blocks my screens share in one place so they all look the same
public static class UiKit
{
    public static TextBlock Text(string text, double size = 13, string brush = "Brush.Text", FontWeight? weight = null, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border
        {
            Child = child,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(14, 12, 14, 12)
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Card");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    public static MoodFace Face(int level, double size) => new() { Level = level, Width = size, Height = size };

    public static Border? Thumb(string? path, double height, int decodeWidth = 400)
    {
        if (path == null) return null;
        var source = ImageHelper.Load(path, decodeWidth);
        if (source == null) return null;
        return new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(8),
            Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill }
        };
    }

    public static void Clickable(FrameworkElement element, Action action)
    {
        element.Cursor = Cursors.Hand;
        element.MouseLeftButtonUp += (_, e) =>
        {
            action();
            e.Handled = true;
        };
    }

    // I make the little pink period label for the week and month views, solid for a logged day and an outline for a guess
    public static Border? PeriodTag(DateOnly day, string? flow)
    {
        if (!MonthCalendar.ShowCycle) return null;
        var logged = flow is "Light" or "Medium" or "Heavy";
        var guessed = !logged && MonthCalendar.PredictedPeriod.Contains(day);
        if (!logged && !guessed) return null;

        var pink = Color.FromRgb(0xEC, 0x5F, 0xA0);
        var words = new TextBlock
        {
            Text = logged ? $"Period, {flow!.ToLowerInvariant()}" : "Period may start",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = logged ? Brushes.White : new SolidColorBrush(pink)
        };
        return new Border
        {
            Child = words,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 1, 7, 2),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(pink),
            Background = logged ? new SolidColorBrush(pink) : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = logged ? "A logged period day" : "A rough guess from past cycles, not medical advice"
        };
    }

    public static TextBlock SectionLabel(string text)
    {
        var label = Text(text.ToUpperInvariant(), 11, "Brush.TextFaint", FontWeights.Bold);
        label.Margin = new Thickness(2, 22, 0, 10);
        return label;
    }

    // I use chips as one-click choices, and this keeps only one of them ticked
    public static WrapPanel ChoiceChips(IEnumerable<(string Value, string Label)> options, string selected, Action<string> onChosen)
    {
        var panel = new WrapPanel();
        foreach (var (value, label) in options)
        {
            var chip = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = label,
                Tag = value,
                IsChecked = value == selected,
                Style = (Style)Application.Current.FindResource("Chip")
            };
            chip.Click += (_, _) =>
            {
                foreach (var other in panel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>())
                    other.IsChecked = ReferenceEquals(other, chip);
                onChosen(value);
            };
            panel.Children.Add(chip);
        }
        return panel;
    }
}
