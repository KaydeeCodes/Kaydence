using System.Windows;
using System.Windows.Media;
using Kaydence.Models;

namespace Kaydence.Controls;

// I draw my mood faces myself because WPF can only show emoji in black and white
public sealed class MoodFace : FrameworkElement
{
    private static readonly Brush FeatureBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xD9, 0x2A, 0x24, 0x38)));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(int), typeof(MoodFace),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Level
    {
        get => (int)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var mood = Moods.Get(Level) ?? Moods.All[2];
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var r = size / 2;

        dc.DrawEllipse(Freeze(new SolidColorBrush(mood.Color)), null, new Point(cx, cy), r, r);

        var eyeR = size * 0.065;
        var eyeY = cy - size * 0.1;
        var eyeX = size * 0.15;
        dc.DrawEllipse(FeatureBrush, null, new Point(cx - eyeX, eyeY), eyeR, eyeR * 1.15);
        dc.DrawEllipse(FeatureBrush, null, new Point(cx + eyeX, eyeY), eyeR, eyeR * 1.15);

        // I bend the mouth from a big smile at 5 down to a big frown at 1
        var curve = Level switch
        {
            5 => 0.17,
            4 => 0.09,
            3 => 0.0,
            2 => -0.08,
            _ => -0.15
        } * size;
        var baseY = curve > 0 ? cy + size * 0.1 : curve < 0 ? cy + size * 0.24 : cy + size * 0.17;

        var mouth = new StreamGeometry();
        using (var g = mouth.Open())
        {
            g.BeginFigure(new Point(cx - size * 0.19, baseY), false, false);
            g.QuadraticBezierTo(new Point(cx, baseY + 2 * curve), new Point(cx + size * 0.19, baseY), true, true);
        }
        mouth.Freeze();

        var pen = new Pen(FeatureBrush, size * 0.075) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, mouth);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
