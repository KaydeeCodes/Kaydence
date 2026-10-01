using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kaydence;

// I let myself drag a box over a picture and keep just that part
public partial class CropWindow : Window
{
    private readonly BitmapSource _source;
    private readonly double _scale;
    private Rect _selection;
    private Point _start;
    private Rect _moveFrom;
    private bool _drawing;
    private bool _moving;

    public CropWindow(BitmapSource source)
    {
        InitializeComponent();
        _source = source;

        // I fit big pictures into the window but never blow small ones up
        _scale = Math.Min(1, Math.Min(760.0 / source.PixelWidth, 520.0 / source.PixelHeight));
        Stage.Width = Math.Max(1, source.PixelWidth * _scale);
        Stage.Height = Math.Max(1, source.PixelHeight * _scale);
        Picture.Source = source;

        _selection = Full;
        ShowSelection();
    }

    // I'm the part of the picture to keep, in real pixels
    public Int32Rect Selection { get; private set; }

    private Rect Full => new(0, 0, Stage.Width, Stage.Height);

    private void Stage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = Clamp(e.GetPosition(Stage));
        _start = point;
        if (_selection != Full && _selection.Contains(point))
        {
            _moving = true;
            _moveFrom = _selection;
        }
        else
        {
            _drawing = true;
            _selection = new Rect(point, point);
        }
        Stage.CaptureMouse();
        ShowSelection();
    }

    private void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        var point = Clamp(e.GetPosition(Stage));
        Stage.Cursor = _moving || (_selection != Full && _selection.Contains(point)) ? Cursors.SizeAll : Cursors.Cross;
        if (_drawing)
        {
            _selection = new Rect(_start, point);
            ShowSelection();
        }
        else if (_moving)
        {
            var x = Math.Clamp(_moveFrom.X + point.X - _start.X, 0, Stage.Width - _moveFrom.Width);
            var y = Math.Clamp(_moveFrom.Y + point.Y - _start.Y, 0, Stage.Height - _moveFrom.Height);
            _selection = new Rect(x, y, _moveFrom.Width, _moveFrom.Height);
            ShowSelection();
        }
    }

    private void Stage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Stage.ReleaseMouseCapture();
        // I treat a tiny accidental drag as a click and keep the whole picture
        if (_drawing && (_selection.Width < 6 || _selection.Height < 6)) _selection = Full;
        _drawing = false;
        _moving = false;
        ShowSelection();
    }

    private Point Clamp(Point point) =>
        new(Math.Clamp(point.X, 0, Stage.Width), Math.Clamp(point.Y, 0, Stage.Height));

    private void ShowSelection()
    {
        Box.Margin = new Thickness(_selection.X, _selection.Y, 0, 0);
        Box.Width = Math.Max(0, _selection.Width);
        Box.Height = Math.Max(0, _selection.Height);

        var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
        shade.Children.Add(new RectangleGeometry(Full));
        shade.Children.Add(new RectangleGeometry(_selection));
        Shade.Data = shade;

        var x = Math.Clamp((int)Math.Round(_selection.X / _scale), 0, _source.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(_selection.Y / _scale), 0, _source.PixelHeight - 1);
        var width = Math.Clamp((int)Math.Round(_selection.Width / _scale), 1, _source.PixelWidth - x);
        var height = Math.Clamp((int)Math.Round(_selection.Height / _scale), 1, _source.PixelHeight - y);
        Selection = new Int32Rect(x, y, width, height);
        SizeText.Text = $"{Selection.Width} x {Selection.Height} pixels";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _selection = Full;
        ShowSelection();
    }

    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _selection != Full;
    }
}
