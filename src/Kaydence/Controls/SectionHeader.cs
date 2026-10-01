using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Kaydence.Controls;

// I show a dot and a short summary on each section so I can see what's filled in without opening it
public sealed class SectionHeader : Grid
{
    private readonly Ellipse _dot = new()
    {
        Width = 7,
        Height = 7,
        Margin = new Thickness(0, 0, 9, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed
    };

    private readonly TextBlock _title = new()
    {
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _summary = new()
    {
        FontSize = 12,
        Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    public SectionHeader()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _dot.SetResourceReference(Shape.FillProperty, "Brush.Accent");
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _summary.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        SetColumn(_title, 1);
        SetColumn(_summary, 2);
        Children.Add(_dot);
        Children.Add(_title);
        Children.Add(_summary);
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public string? Summary
    {
        get => _summary.Text;
        set
        {
            _summary.Text = value ?? "";
            _dot.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
