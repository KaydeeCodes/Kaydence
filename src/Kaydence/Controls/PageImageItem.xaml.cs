using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I'm a picture that floats on the page: click me to select me, then drag, resize, rotate, crop or delete me
public partial class PageImageItem : UserControl, IPageElement
{
    private double _aspect;

    public event Action? Changed;
    public event Action<UIElement>? RemoveRequested;
    public event Action<UIElement, bool>? OrderRequested;
    public event Action<PageImageItem, string>? EditRequested;

    public PageImageItem(string path, string fileName, double width, double height)
    {
        InitializeComponent();

        FileName = fileName;
        Width = Math.Max(40, width);
        Height = Math.Max(20, height);
        _aspect = Width / Height;
        LoadPicture(path);

        MoveThumb.DragStarted += (_, _) => Keyboard.Focus(this);
        MoveThumb.DragDelta += (_, e) =>
        {
            Canvas.SetLeft(this, Math.Max(0, Canvas.GetLeft(this) + e.HorizontalChange));
            Canvas.SetTop(this, Math.Max(0, Canvas.GetTop(this) + e.VerticalChange));
        };
        MoveThumb.DragCompleted += (_, _) => Changed?.Invoke();

        ResizeThumb.DragDelta += (_, e) =>
        {
            var newWidth = Math.Max(40, Width + e.HorizontalChange);
            Width = newWidth;
            Height = newWidth / _aspect;
        };
        ResizeThumb.DragCompleted += (_, _) => Changed?.Invoke();

        PreviewMouseLeftButtonDown += (_, _) => Keyboard.Focus(this);
        MouseEnter += (_, _) => UpdateChrome();
        MouseLeave += (_, _) => UpdateChrome();
        IsKeyboardFocusWithinChanged += (_, _) => UpdateChrome();
        KeyDown += OnKeyDown;
    }

    public string FileName { get; }

    public bool IsBlank => false;

    public string PlainText => "";

    public PageItem ToItem(double x, double y, int z) => new()
    {
        Kind = "image",
        X = x,
        Y = y,
        Z = z,
        Width = Width,
        Height = Height,
        Image = FileName
    };

    private void LoadPicture(string path)
    {
        try
        {
            // I load the picture fully into memory so the file is never locked, and unseal it if my diary is encrypted
            var source = ImageHelper.Load(path, 0);
            if (source == null)
            {
                Log.Warn("Pictures", $"Showing \"Picture not found\" for {FileName}, see the lines above for why");
                Missing.Visibility = Visibility.Visible;
            }
            else
            {
                Picture.Source = source;
                FixSidewaysBox(source);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Pictures", $"Showing \"Picture not found\" for {FileName} after an error", ex);
            Missing.Visibility = Visibility.Visible;
        }
    }

    // I swap the box round for an older photo that was placed sideways before photos were turned upright
    private void FixSidewaysBox(System.Windows.Media.ImageSource source)
    {
        if (source is not System.Windows.Media.Imaging.BitmapSource bitmap || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) return;
        var picture = bitmap.PixelWidth / (double)bitmap.PixelHeight;
        var box = Width / Height;
        if (Math.Abs(box - picture) < 0.02 || Math.Abs(box - 1 / picture) >= 0.02) return;
        (Width, Height) = (Height, Width);
        _aspect = Width / Height;
        Log.Info("Pictures", $"Turned the box for {FileName} round to match the upright photo");
    }

    private void UpdateChrome()
    {
        var selected = IsKeyboardFocusWithin;
        Frame.Opacity = selected ? 1 : IsMouseOver ? 0.4 : 0;
        ResizeThumb.Opacity = selected || IsMouseOver ? 1 : 0;
        DeleteButton.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            RemoveRequested?.Invoke(this);
            e.Handled = true;
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action }) EditRequested?.Invoke(this, action);
    }

    private void Front_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, true);

    private void Back_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, false);

    private void Delete_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this);
}
