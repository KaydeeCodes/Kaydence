using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Kaydence.Models;

namespace Kaydence.Controls;

// I click one face for how I'm doing, and click it again to clear it
public sealed class MoodPicker : UserControl
{
    private readonly List<(Button Button, MoodFace Face, int Value)> _faces = new();
    private int? _value;

    public event Action<int?>? ValueChanged;

    public MoodPicker()
    {
        var grid = new UniformGrid { Columns = Moods.All.Count, Rows = 1 };
        foreach (var mood in Moods.All)
        {
            var face = new MoodFace
            {
                Level = mood.Value,
                Width = 36,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var label = new TextBlock
            {
                Text = mood.Name,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

            var stack = new StackPanel();
            stack.Children.Add(face);
            stack.Children.Add(label);

            var button = new Button
            {
                Content = stack,
                Style = (Style)FindResource("PlainButton"),
                Padding = new Thickness(2, 6, 2, 4),
                ToolTip = mood.Name
            };
            var value = mood.Value;
            button.Click += (_, _) =>
            {
                Value = _value == value ? null : value;
                ValueChanged?.Invoke(_value);
            };

            _faces.Add((button, face, value));
            grid.Children.Add(button);
        }
        Content = grid;
        Render();
    }

    public int? Value
    {
        get => _value;
        set
        {
            _value = value;
            Render();
        }
    }

    private void Render()
    {
        foreach (var (button, face, value) in _faces)
        {
            var selected = _value == value;
            button.Opacity = _value == null || selected ? 1 : 0.4;
            var scale = selected ? 1.15 : 1.0;
            face.RenderTransform = new ScaleTransform(scale, scale);
        }
    }
}
