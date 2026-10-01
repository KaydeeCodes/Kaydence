using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

public enum PageTool
{
    Type,
    Pen,
    Highlighter,
    Eraser,
    Lasso,
    Shape
}

public enum ShapeKind
{
    Line,
    Arrow,
    Rectangle,
    Circle,
    Heart
}

// I'm the freeform page: click anywhere to type, paste or drop pictures anywhere, and draw over the top
public partial class DiaryPage : UserControl
{
    private static readonly Point StartPoint = new(56, 64);
    public const double MinZoom = 0.25;
    public const double MaxZoom = 3;

    private DiaryStore? _store;
    private DateOnly _day;
    private bool _loading;
    private Point _lastClick = StartPoint;
    private PageTool _tool = PageTool.Type;
    private ShapeKind _shape = ShapeKind.Rectangle;
    private Color _penColor = Color.FromRgb(0x7B, 0x4F, 0xB8);
    private Color _highlightColor = Color.FromRgb(0xFF, 0xE0, 0x66);
    private double _penWidth = 2.6;
    private string _pageStyle = "Plain";

    // I remember the last text box and picture I was on so the ribbon knows what to change
    private PageTextItem? _activeText;
    private PageImageItem? _activeImage;

    private readonly ScaleTransform _scale = new(1, 1);
    private double _zoom = 1;

    private bool _drawingShape;
    private Point _shapeStart;

    // I keep snapshots of the page so Ctrl+Z and Ctrl+Y can step back and forward through my changes
    private sealed record Snapshot(string Items, byte[]? Ink);
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();
    private readonly DispatcherTimer _historyTimer;
    private Snapshot? _current;
    private const int HistoryLimit = 200;

    public event Action? Changed;
    public event Action? ZoomChanged;

    public DiaryPage()
    {
        InitializeComponent();

        _historyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _historyTimer.Tick += (_, _) => Commit();
        ThemeService.ThemeChanged += ApplyPageStyle;

        Surface.LayoutTransform = _scale;
        ItemsLayer.MouseLeftButtonDown += ItemsLayer_MouseLeftButtonDown;
        Surface.PreviewDragOver += Surface_PreviewDragOver;
        Surface.PreviewDrop += Surface_PreviewDrop;
        Scroller.SizeChanged += (_, _) => UpdateExtent();
        Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
        Ink.SelectionMoved += (_, _) => RaiseChanged();
        Ink.SelectionResized += (_, _) => RaiseChanged();
        Ink.PreviewMouseLeftButtonDown += Ink_ShapeDown;
        Ink.PreviewMouseMove += Ink_ShapeMove;
        Ink.PreviewMouseLeftButtonUp += Ink_ShapeUp;
        Ink.PreviewMouseLeftButtonDown += (_, e) => _inputKind = e.StylusDevice == null ? "mouse" : $"{e.StylusDevice.TabletDevice?.Type.ToString() ?? "stylus"} as mouse";
        Ink.PreviewStylusDown += (_, e) => _inputKind = e.StylusDevice.TabletDevice?.Type.ToString() ?? "stylus";
        Ink.StrokeCollected += (_, e) => DescribeStroke(e.Stroke);
        HookStrokes();
        ApplyTool();
    }

    public void Initialise(DiaryStore store) => _store = store;

    public string PageStyle
    {
        get => _pageStyle;
        set
        {
            _pageStyle = value;
            ApplyPageStyle();
        }
    }

    public PageTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Log.Debug("Page", $"Tool is now {value}");
            ApplyTool();
        }
    }

    public ShapeKind Shape
    {
        get => _shape;
        set => _shape = value;
    }

    public Color PenColor
    {
        get => _penColor;
        set
        {
            _penColor = value;
            ApplyTool();
        }
    }

    public Color HighlightColor
    {
        get => _highlightColor;
        set
        {
            _highlightColor = value;
            ApplyTool();
        }
    }

    public double PenWidth
    {
        get => _penWidth;
        set
        {
            _penWidth = Math.Clamp(value, 1, 20);
            ApplyTool();
        }
    }

    public double Zoom => _zoom;

    public void Load(DayEntry entry, DateOnly day, byte[]? ink)
    {
        _loading = true;
        try
        {
            _day = day;
            _activeText = null;
            _activeImage = null;
            ItemsLayer.Children.Clear();
            foreach (var item in entry.Items.OrderBy(i => i.Z)) AddElement(item);

            UnhookStrokes();
            Ink.Strokes = LoadStrokes(ink);
            HookStrokes();

            Scroller.ScrollToHome();
            _lastClick = StartPoint;
        }
        finally
        {
            _loading = false;
        }
        UpdateHint();
        Dispatcher.InvokeAsync(UpdateExtent, DispatcherPriority.Loaded);

        _historyTimer.Stop();
        _undo.Clear();
        _redo.Clear();
        _current = Capture();
        Log.Info("Page", $"Showing {day:yyyy-MM-dd}: {DiaryStore.DescribeItems(entry.Items)}, {Ink.Strokes.Count} drawn strokes");
    }

    public bool Undo()
    {
        if (_historyTimer.IsEnabled) Commit();
        if (_undo.Count == 0 || _current == null) return false;
        Log.Debug("Page", $"Undo, {_undo.Count - 1} steps left");
        _redo.Add(_current);
        _current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(_current);
        return true;
    }

    public bool Redo()
    {
        if (_historyTimer.IsEnabled) Commit();
        if (_redo.Count == 0 || _current == null) return false;
        Log.Debug("Page", $"Redo, {_redo.Count - 1} steps left");
        _undo.Add(_current);
        _current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(_current);
        return true;
    }

    // I delete whatever is selected: a picture, drawings picked with the lasso, or the box I'm in if I ask for it
    public bool DeleteSelection(bool includeText)
    {
        var focused = FocusedElement();
        if (focused is PageImageItem or PageVoiceItem)
        {
            RemoveElement(focused);
            return true;
        }
        if (focused != null)
        {
            if (!includeText) return false;
            RemoveElement(focused);
            return true;
        }

        var strokes = Ink.GetSelectedStrokes();
        if (strokes.Count > 0)
        {
            Ink.Strokes.Remove(strokes);
            return true;
        }
        return false;
    }

    public string GetPlainText() => string.Join("\n\n", ItemsLayer.Children.OfType<FrameworkElement>()
        .Where(c => c is IPageElement)
        .OrderBy(c => Safe(Canvas.GetTop(c)))
        .ThenBy(c => Safe(Canvas.GetLeft(c)))
        .Select(c => ((IPageElement)c).PlainText)
        .Where(t => t.Length > 0));

    public List<PageItem> CollectItems()
    {
        var items = new List<PageItem>();
        foreach (UIElement child in ItemsLayer.Children)
        {
            if (child is not IPageElement element || element.IsBlank) continue;
            var item = element.ToItem(Safe(Canvas.GetLeft(child)), Safe(Canvas.GetTop(child)), Panel.GetZIndex(child));
            item.Width = Safe(item.Width);
            item.Height = Safe(item.Height);
            items.Add(item);
        }
        return items;
    }

    public byte[]? GetInk()
    {
        if (Ink.Strokes.Count == 0) return null;
        using var stream = new MemoryStream();
        Ink.Strokes.Save(stream);
        return stream.ToArray();
    }

    // I put the caret somewhere useful: a fresh line on an empty page, or the end of my last bit of writing
    public void FocusForWriting(bool createIfEmpty)
    {
        var last = ItemsLayer.Children.OfType<PageTextItem>()
            .Where(t => !t.IsEmpty && !t.IsSticky)
            .OrderBy(t => Canvas.GetTop(t))
            .LastOrDefault();

        if (last != null)
        {
            Dispatcher.InvokeAsync(last.FocusAtEnd, DispatcherPriority.Input);
            return;
        }
        if (createIfEmpty && !PageHasContent()) CreateTextAt(StartPoint);
    }

    // I handle Ctrl+V myself so pictures always float on the page instead of getting stuck inside text
    public bool TryPaste()
    {
        if (_store == null) return false;
        IDataObject? data;
        try
        {
            data = Clipboard.GetDataObject();
        }
        catch (Exception ex)
        {
            // I give up quietly if another app is holding the clipboard, I can just paste again
            Log.Warn("Paste", "Another app is holding the clipboard", ex);
            return false;
        }
        if (data == null)
        {
            Log.Debug("Paste", "The clipboard is empty");
            return false;
        }

        var focusedText = FocusedTextItem();
        var at = focusedText != null ? focusedText.CaretPointOn(ItemsLayer) : VisibleSpot(_lastClick);
        Log.Info("Paste", $"Pasting on {_day:yyyy-MM-dd}, clipboard holds: {string.Join(", ", SafeFormats(data))}, typing in a box: {focusedText != null}");

        var image = ReadClipboardImage(data);
        if (image != null)
        {
            var name = _store.SaveImage(_day, image);
            AddNewImage(name, image.PixelWidth, image.PixelHeight, at, select: focusedText == null);
            return true;
        }

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var pictures = files.Where(DiaryStore.IsImageFile).ToArray();
            Log.Info("Paste", $"Clipboard has {files.Length} copied files, {pictures.Length} of them pictures");
            if (pictures.Length > 0)
            {
                AddImageFiles(pictures, at);
                return true;
            }
        }

        if (focusedText == null && Keyboard.FocusedElement is not TextBoxBase && data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string;
            if (!string.IsNullOrEmpty(text))
            {
                Log.Info("Paste", $"Pasting {text.Length} characters of text into a new box");
                CreateTextAt(at, text);
                return true;
            }
        }
        Log.Debug("Paste", "Letting the normal paste happen");
        return false;
    }

    // I list clipboard format names only, never what's in them
    private static IEnumerable<string> SafeFormats(IDataObject data)
    {
        try
        {
            return data.GetFormats(false);
        }
        catch (Exception ex)
        {
            return new[] { $"(couldn't list: {ex.GetType().Name})" };
        }
    }

    public void AddImageFiles(IEnumerable<string> paths, Point? at = null)
    {
        if (_store == null) return;
        var spot = at ?? VisibleSpot(_lastClick);
        foreach (var path in paths)
        {
            string? name = null;
            try
            {
                name = _store.ImportImage(_day, path);
                var frame = ImageHelper.Frame(_store.ImagePath(_day, name));
                AddNewImage(name, frame.PixelWidth, frame.PixelHeight, spot, select: true);
                spot = new Point(spot.X + 24, spot.Y + 24);
            }
            catch (Exception ex)
            {
                // I skip any file that isn't really a picture, but still show it if it was saved so I can see something went wrong
                Log.Error("Pictures", $"Couldn't add a {Path.GetExtension(path)} picture (saved as {name ?? "nothing yet"})", ex);
                if (name != null) AddNewImage(name, 400, 300, spot, select: true);
            }
        }
    }

    // I add a fresh text box somewhere I can see it, for the Insert tab
    public void AddTextBox() => CreateTextAt(NextSpot());

    public void AddSticky(string hex)
    {
        var spot = NextSpot();
        var element = AddTextElement(new PageItem { Kind = "sticky", X = spot.X, Y = spot.Y, Width = 220, Z = NextZ(), Color = hex });
        Dispatcher.InvokeAsync(element.FocusAtEnd, DispatcherPriority.Input);
        RaiseChanged();
    }

    public void AddChecklist()
    {
        var spot = NextSpot();
        var element = AddChecklistElement(new PageItem
        {
            Kind = "checklist",
            X = spot.X,
            Y = spot.Y,
            Width = 300,
            Z = NextZ(),
            Checks = new List<CheckEntry> { new() }
        });
        UpdateHint();
        element.FocusFirst();
    }

    // I pop the time in where I'm typing, handy for noting when a symptom started
    public void InsertTimeStamp()
    {
        var stamp = DateTime.Now.ToString("t", CultureInfo.CurrentCulture) + " ";
        var target = ActiveText();
        if (target != null) target.InsertText(stamp);
        else CreateTextAt(NextSpot(), stamp);
    }

    // I apply ribbon formatting to the text box I'm in, or the last one I was in
    public bool Format(string action, Color? colour = null)
    {
        var target = ActiveText();
        if (target == null) return false;
        target.Format(action, colour);
        return true;
    }

    public bool EditSelectedImage(string action)
    {
        var target = ItemsLayer.Children.OfType<PageImageItem>().FirstOrDefault(i => i.IsKeyboardFocusWithin)
                     ?? (_activeImage != null && ItemsLayer.Children.Contains(_activeImage) ? _activeImage : null);
        if (target == null) return false;
        EditImage(target, action);
        return true;
    }

    public void ZoomBy(double factor) => SetZoom(_zoom * factor, null);

    public void SetZoom(double value, Point? anchor)
    {
        value = Math.Clamp(Math.Round(value, 2), MinZoom, MaxZoom);
        if (Math.Abs(value - _zoom) < 0.001) return;

        // I keep the spot under the mouse, or the middle of the view, in the same place while zooming
        var viewPoint = anchor ?? new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
        var pagePoint = new Point((Scroller.HorizontalOffset + viewPoint.X) / _zoom, (Scroller.VerticalOffset + viewPoint.Y) / _zoom);

        _zoom = value;
        Log.Debug("Page", $"Zoom is now {Math.Round(value * 100)}%");
        _scale.ScaleX = value;
        _scale.ScaleY = value;
        UpdateExtent();
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(Math.Max(0, pagePoint.X * value - viewPoint.X));
        Scroller.ScrollToVerticalOffset(Math.Max(0, pagePoint.Y * value - viewPoint.Y));
        ZoomChanged?.Invoke();
    }

    // I shrink the page until everything on it fits in the window, but never blow it up past normal size
    public void FitToView()
    {
        var (right, bottom) = ContentEdge();
        var fit = right <= 0 || bottom <= 0
            ? 1
            : Math.Min((Scroller.ActualWidth - 24) / (right + 24), (Scroller.ActualHeight - 24) / (bottom + 24));
        SetZoom(Math.Min(1, fit), null);
        Scroller.ScrollToHome();
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        SetZoom(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), e.GetPosition(Scroller));
        e.Handled = true;
    }

    private void ApplyTool()
    {
        Ink.IsHitTestVisible = _tool != PageTool.Type;
        Ink.UseCustomCursor = _tool == PageTool.Shape;
        switch (_tool)
        {
            case PageTool.Type:
                Ink.EditingMode = InkCanvasEditingMode.None;
                break;
            case PageTool.Pen:
                Ink.EditingMode = InkCanvasEditingMode.Ink;
                Ink.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = _penColor,
                    Width = _penWidth,
                    Height = _penWidth,
                    FitToCurve = true
                };
                break;
            case PageTool.Highlighter:
                Ink.EditingMode = InkCanvasEditingMode.Ink;
                Ink.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = _highlightColor,
                    IsHighlighter = true,
                    StylusTip = StylusTip.Rectangle,
                    Width = 6,
                    Height = 20,
                    IgnorePressure = true
                };
                break;
            case PageTool.Eraser:
                Ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
                break;
            case PageTool.Lasso:
                Ink.EditingMode = InkCanvasEditingMode.Select;
                break;
            case PageTool.Shape:
                Ink.EditingMode = InkCanvasEditingMode.None;
                Ink.Cursor = Cursors.Cross;
                break;
        }
    }

    private void Ink_ShapeDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool != PageTool.Shape) return;
        _shapeStart = e.GetPosition(Ink);
        _drawingShape = true;
        Ink.CaptureMouse();
        e.Handled = true;
    }

    private void Ink_ShapeMove(object sender, MouseEventArgs e)
    {
        if (!_drawingShape) return;
        var lines = ShapeLines(_shapeStart, e.GetPosition(Ink), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        var geometry = new PathGeometry();
        foreach (var line in lines)
        {
            geometry.Figures.Add(new PathFigure(line[0], new[] { new PolyLineSegment(line.Skip(1), true) }, false));
        }
        ShapePreview.Data = geometry;
        ShapePreview.Stroke = new SolidColorBrush(_penColor);
        ShapePreview.StrokeThickness = _penWidth;
        e.Handled = true;
    }

    private void Ink_ShapeUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawingShape) return;
        _drawingShape = false;
        Ink.ReleaseMouseCapture();
        ShapePreview.Data = null;

        var end = e.GetPosition(Ink);
        if (Math.Abs(end.X - _shapeStart.X) < 3 && Math.Abs(end.Y - _shapeStart.Y) < 3) return;

        // I turn my shape into normal ink so the eraser, lasso and undo all work on it
        foreach (var line in ShapeLines(_shapeStart, end, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            var attributes = new DrawingAttributes
            {
                Color = _penColor,
                Width = _penWidth,
                Height = _penWidth,
                FitToCurve = false,
                IgnorePressure = true
            };
            Ink.Strokes.Add(new Stroke(new StylusPointCollection(line), attributes));
        }
        e.Handled = true;
    }

    // I work out the points for each shape, holding Shift keeps circles round and lines straight
    private List<List<Point>> ShapeLines(Point a, Point b, bool even)
    {
        if (even)
        {
            if (_shape is ShapeKind.Line or ShapeKind.Arrow)
            {
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var snapped = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                var length = Math.Sqrt(dx * dx + dy * dy);
                b = new Point(a.X + Math.Cos(snapped) * length, a.Y + Math.Sin(snapped) * length);
            }
            else
            {
                var size = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                b = new Point(a.X + (b.X < a.X ? -size : size), a.Y + (b.Y < a.Y ? -size : size));
            }
        }

        var box = new Rect(a, b);
        switch (_shape)
        {
            case ShapeKind.Line:
                return new List<List<Point>> { new() { a, b } };

            case ShapeKind.Arrow:
                var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                var head = Math.Max(12, _penWidth * 4);
                var tip = b;
                Func<double, Point> wing = turn => new Point(tip.X - head * Math.Cos(angle + turn), tip.Y - head * Math.Sin(angle + turn));
                return new List<List<Point>> { new() { a, b }, new() { wing(0.45), b, wing(-0.45) } };

            case ShapeKind.Rectangle:
                return new List<List<Point>> { new() { box.TopLeft, box.TopRight, box.BottomRight, box.BottomLeft, box.TopLeft } };

            case ShapeKind.Circle:
                var ring = new List<Point>();
                for (var i = 0; i <= 90; i++)
                {
                    var t = i * Math.PI * 2 / 90;
                    ring.Add(new Point(box.X + box.Width / 2 * (1 + Math.Cos(t)), box.Y + box.Height / 2 * (1 + Math.Sin(t))));
                }
                return new List<List<Point>> { ring };

            default:
                // I use the classic heart curve and stretch it to fill the box I dragged out
                var raw = new List<Point>();
                for (var i = 0; i <= 120; i++)
                {
                    var t = i * Math.PI * 2 / 120;
                    var x = 16 * Math.Pow(Math.Sin(t), 3);
                    var y = -(13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t));
                    raw.Add(new Point(x, y));
                }
                var minY = raw.Min(p => p.Y);
                var maxY = raw.Max(p => p.Y);
                var heart = raw.Select(p => new Point(
                    box.X + (p.X + 16) / 32 * box.Width,
                    box.Y + (p.Y - minY) / Math.Max(0.001, maxY - minY) * box.Height)).ToList();
                return new List<List<Point>> { heart };
        }
    }

    private void ItemsLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool != PageTool.Type || !ReferenceEquals(e.OriginalSource, ItemsLayer)) return;
        var point = e.GetPosition(ItemsLayer);
        _lastClick = point;
        CreateTextAt(point);
        e.Handled = true;
    }

    private void Surface_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!DroppedPictures(e).Any()) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Surface_PreviewDrop(object sender, DragEventArgs e)
    {
        var pictures = DroppedPictures(e).ToArray();
        if (pictures.Length == 0) return;
        AddImageFiles(pictures, e.GetPosition(ItemsLayer));
        e.Handled = true;
    }

    private static IEnumerable<string> DroppedPictures(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(DiaryStore.IsImageFile)
            : Enumerable.Empty<string>();

    private PageTextItem CreateTextAt(Point point, string? text = null)
    {
        // I nudge the box up and left so the caret lands right where I clicked
        var x = Math.Max(0, point.X - 8);
        var y = Math.Max(0, point.Y - 24);
        var room = Scroller.ActualWidth / _zoom - x - 40;
        var width = Math.Min(560, Math.Max(260, room));

        var element = AddTextElement(new PageItem { Kind = "text", X = x, Y = y, Width = width, Z = NextZ() });
        if (text != null) element.SetPlainText(text);
        Dispatcher.InvokeAsync(element.FocusAtEnd, DispatcherPriority.Input);
        UpdateHint();
        return element;
    }

    private void AddElement(PageItem item)
    {
        switch (item.Kind)
        {
            case "image" when !string.IsNullOrEmpty(item.Image):
                AddImageElement(item);
                break;
            case "text":
            case "sticky":
                AddTextElement(item);
                break;
            case "checklist":
                AddChecklistElement(item);
                break;
            case "voice" when !string.IsNullOrEmpty(item.Audio):
                AddVoiceElement(item);
                break;
        }
    }

    private PageTextItem AddTextElement(PageItem item)
    {
        var element = new PageTextItem { Width = Math.Max(PageTextItem.MinBoxWidth, item.Width) };
        if (!string.IsNullOrEmpty(item.Xaml))
        {
            try
            {
                element.SetXaml(item.Xaml);
            }
            catch (Exception)
            {
                // I'd rather show nothing than crash if one saved box can't be read
            }
        }
        if (item.Kind == "sticky") element.SetSticky(item.Color ?? PageTextItem.StickyColours[0].Hex);
        return Hook(element, item);
    }

    private PageChecklistItem AddChecklistElement(PageItem item)
    {
        var element = new PageChecklistItem { Width = Math.Max(PageChecklistItem.MinListWidth, item.Width) };
        element.SetChecks(item.Checks);
        return Hook(element, item);
    }

    private PageVoiceItem AddVoiceElement(PageItem item)
    {
        var path = _store != null ? _store.ImagePath(_day, item.Audio!) : item.Audio!;
        byte[]? wave = null;
        try
        {
            if (item.Wave != null) wave = Convert.FromBase64String(item.Wave);
        }
        catch (FormatException)
        {
            // I just draw a flat waveform if the saved one is damaged
        }
        return Hook(new PageVoiceItem(path, item.Audio!, item.Duration ?? 0, wave), item);
    }

    // I record a voice note and drop it on the page where I can see it
    public void AddVoiceNote()
    {
        if (_store == null) return;
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var recorder = new VoiceRecorderWindow(owner);
        if (recorder.ShowDialog() != true || recorder.Audio == null) return;
        try
        {
            var name = _store.SaveAudio(_day, recorder.Audio);
            var spot = NextSpot();
            var element = AddVoiceElement(new PageItem
            {
                Kind = "voice",
                X = spot.X,
                Y = spot.Y,
                Width = 330,
                Z = NextZ(),
                Audio = name,
                Duration = recorder.Seconds,
                Wave = Convert.ToBase64String(recorder.Waveform)
            });
            Dispatcher.InvokeAsync(() => Keyboard.Focus(element), DispatcherPriority.Input);
            UpdateHint();
            RaiseChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"The voice note couldn't be saved.\n\n{ex.Message}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private PageImageItem AddImageElement(PageItem item)
    {
        var path = _store != null ? _store.ImagePath(_day, item.Image!) : item.Image!;
        var element = new PageImageItem(path, item.Image!, item.Width, item.Height);
        element.EditRequested += EditImage;
        return Hook(element, item);
    }

    // I wire up anything I put on the page the same way, whatever kind of thing it is
    private T Hook<T>(T element, PageItem item) where T : FrameworkElement, IPageElement
    {
        Place(element, item.X, item.Y, item.Z);
        element.Changed += RaiseChanged;
        element.RemoveRequested += RemoveElement;
        element.OrderRequested += Reorder;
        element.SizeChanged += (_, _) => UpdateExtent();
        element.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (!element.IsKeyboardFocusWithin) return;
            if (element is PageTextItem text) _activeText = text;
            if (element is PageImageItem image) _activeImage = image;
        };
        ItemsLayer.Children.Add(element);
        return element;
    }

    private void AddNewImage(string name, int pixelWidth, int pixelHeight, Point at, bool select)
    {
        var (width, height) = StartingSize(pixelWidth, pixelHeight);
        Log.Info("Pictures", $"Placing {name} on the page at {Math.Round(width)} x {Math.Round(height)} (picture is {pixelWidth} x {pixelHeight})");
        var element = AddImageElement(new PageItem
        {
            Kind = "image",
            Image = name,
            X = Math.Max(0, at.X),
            Y = Math.Max(0, at.Y + 4),
            Width = width,
            Height = height,
            Z = NextZ()
        });
        if (select) Dispatcher.InvokeAsync(() => Keyboard.Focus(element), DispatcherPriority.Input);
        UpdateHint();
        RaiseChanged();
    }

    // I shrink big screenshots down so they don't swamp the page, I can always drag them bigger
    private static (double Width, double Height) StartingSize(int pixelWidth, int pixelHeight)
    {
        double width = Math.Max(1, pixelWidth);
        double height = Math.Max(1, pixelHeight);
        const double maxWidth = 520;
        if (width > maxWidth)
        {
            height = height * maxWidth / width;
            width = maxWidth;
        }
        return (width, height);
    }

    // I rotate or crop a picture into a brand new file, so undo can still bring the old one back
    private void EditImage(PageImageItem element, string action)
    {
        if (_store == null || !ItemsLayer.Children.Contains(element)) return;
        var owner = Window.GetWindow(this);
        Log.Info("Pictures", $"Picture edit '{action}' on {element.FileName}");

        BitmapSource source;
        try
        {
            source = ImageHelper.Frame(_store.ImagePath(_day, element.FileName));
        }
        catch (Exception ex)
        {
            Log.Error("Pictures", $"Couldn't open {element.FileName} to edit it", ex);
            MessageBox.Show(owner!, "That picture couldn't be opened to change it.", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var width = Safe(element.Width);
        var height = Safe(element.Height);
        BitmapSource result;
        switch (action)
        {
            case "left":
            case "right":
                result = new TransformedBitmap(source, new RotateTransform(action == "left" ? 270 : 90));
                (width, height) = (height, width);
                break;
            case "crop":
                var crop = new CropWindow(source) { Owner = owner };
                if (crop.ShowDialog() != true) return;
                var scale = width / Math.Max(1, source.PixelWidth);
                result = new CroppedBitmap(source, crop.Selection);
                width = crop.Selection.Width * scale;
                height = crop.Selection.Height * scale;
                break;
            case "reset":
                (element.Width, element.Height) = StartingSize(source.PixelWidth, source.PixelHeight);
                RaiseChanged();
                return;
            default:
                return;
        }

        try
        {
            var name = _store.SaveImage(_day, result);
            var item = new PageItem
            {
                Kind = "image",
                Image = name,
                X = Safe(Canvas.GetLeft(element)),
                Y = Safe(Canvas.GetTop(element)),
                Z = Panel.GetZIndex(element),
                Width = Math.Max(40, width),
                Height = Math.Max(20, height)
            };
            ItemsLayer.Children.Remove(element);
            var added = AddImageElement(item);
            Dispatcher.InvokeAsync(() => Keyboard.Focus(added), DispatcherPriority.Input);
            RaiseChanged();
            Log.Info("Pictures", $"Edit '{action}' saved as {name}");
        }
        catch (Exception ex)
        {
            Log.Error("Pictures", $"Edit '{action}' on {element.FileName} couldn't be saved", ex);
            MessageBox.Show(owner!, $"The changed picture couldn't be saved.\n\n{ex.Message}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void Place(UIElement element, double x, double y, int z)
    {
        Canvas.SetLeft(element, Safe(x));
        Canvas.SetTop(element, Safe(y));
        Panel.SetZIndex(element, z);
    }

    private void RemoveElement(UIElement element)
    {
        if (!ItemsLayer.Children.Contains(element)) return;
        Log.Info("Page", $"Removed a {element.GetType().Name.Replace("Page", "").Replace("Item", "").ToLowerInvariant()} from the page");
        ItemsLayer.Children.Remove(element);
        if (ReferenceEquals(element, _activeText)) _activeText = null;
        if (ReferenceEquals(element, _activeImage)) _activeImage = null;
        UpdateHint();
        UpdateExtent();
        RaiseChanged();
    }

    private void Reorder(UIElement element, bool toFront)
    {
        var others = ItemsLayer.Children.Cast<UIElement>().Where(c => !ReferenceEquals(c, element)).Select(c => Panel.GetZIndex(c)).ToList();
        var z = others.Count == 0 ? 0 : toFront ? others.Max() + 1 : others.Min() - 1;
        Panel.SetZIndex(element, z);
        RaiseChanged();
    }

    private int NextZ() =>
        ItemsLayer.Children.Count == 0 ? 0 : ItemsLayer.Children.Cast<UIElement>().Max(c => Panel.GetZIndex(c)) + 1;

    private PageTextItem? FocusedTextItem() =>
        ItemsLayer.Children.OfType<PageTextItem>().FirstOrDefault(t => t.IsKeyboardFocusWithin);

    private PageTextItem? ActiveText() =>
        FocusedTextItem() ?? (_activeText != null && ItemsLayer.Children.Contains(_activeText) ? _activeText : null);

    private UIElement? FocusedElement() =>
        ItemsLayer.Children.OfType<UIElement>().FirstOrDefault(c => c is IPageElement && c.IsKeyboardFocusWithin);

    // I use my last click if it's still on screen, otherwise the top left of what I can see
    private Point VisibleSpot(Point preferred)
    {
        var view = new Rect(Scroller.HorizontalOffset / _zoom, Scroller.VerticalOffset / _zoom,
            Scroller.ViewportWidth / _zoom, Scroller.ViewportHeight / _zoom);
        return view.Contains(preferred) ? preferred : new Point(view.X + 40, view.Y + 40);
    }

    // I step each new thing I insert down a little so they don't all land in a pile
    private Point NextSpot()
    {
        var spot = VisibleSpot(_lastClick);
        _lastClick = new Point(spot.X + 24, spot.Y + 24);
        return spot;
    }

    private bool PageHasContent() =>
        Ink.Strokes.Count > 0
        || ItemsLayer.Children.OfType<PageChecklistItem>().Any()
        || ItemsLayer.Children.OfType<IPageElement>().Any(e => !e.IsBlank);

    private void UpdateHint() => Hint.Visibility = PageHasContent() ? Visibility.Collapsed : Visibility.Visible;

    private (double Right, double Bottom) ContentEdge()
    {
        double right = 0, bottom = 0;
        foreach (FrameworkElement element in ItemsLayer.Children)
        {
            right = Math.Max(right, Safe(Canvas.GetLeft(element)) + element.ActualWidth);
            bottom = Math.Max(bottom, Safe(Canvas.GetTop(element)) + element.ActualHeight);
        }
        if (Ink.Strokes.Count > 0)
        {
            var bounds = Ink.Strokes.GetBounds();
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }
        return (right, bottom);
    }

    // I grow the page as I add things so there's always room to keep going down
    private void UpdateExtent()
    {
        var (right, bottom) = ContentEdge();
        Surface.Width = Math.Max(Math.Max(0, Scroller.ActualWidth - 14) / _zoom, right + 40);
        Surface.Height = Math.Max(Math.Max(0, Scroller.ActualHeight - 14) / _zoom, bottom + 400);
    }

    private static StrokeCollection LoadStrokes(byte[]? ink)
    {
        if (ink == null || ink.Length == 0) return new StrokeCollection();
        try
        {
            using var stream = new MemoryStream(ink);
            return new StrokeCollection(stream);
        }
        catch (Exception)
        {
            return new StrokeCollection();
        }
    }

    private string _inputKind = "unknown";

    // I note the shape of each new stroke, never what it looks like, so a stroke that jumps across the page is easy to spot in the log
    private void DescribeStroke(Stroke stroke)
    {
        try
        {
            var points = stroke.StylusPoints;
            var biggest = 0.0;
            var at = 0;
            for (var i = 1; i < points.Count; i++)
            {
                var gap = Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
                if (gap <= biggest) continue;
                biggest = gap;
                at = i;
            }
            var bounds = stroke.GetBounds();
            var dpi = VisualTreeHelper.GetDpi(this);
            AppContext.TryGetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", out var pointer);
            var text = $"Stroke from {_inputKind}: {points.Count} points, {bounds.Width:0} x {bounds.Height:0} at {bounds.X:0},{bounds.Y:0}, " +
                       $"biggest step {biggest:0.0} at point {at}, zoom {_zoom:0.00}, screen scale {dpi.DpiScaleX * 100:0}%, pointer input {pointer}";
            if (biggest > 120) Log.Warn("Ink", text + ". That step looks like a jump");
            else Log.Debug("Ink", text);
        }
        catch (Exception ex)
        {
            Log.Debug("Ink", $"Couldn't describe the stroke: {ex.GetType().Name}");
        }
    }

    private void HookStrokes() => Ink.Strokes.StrokesChanged += Strokes_StrokesChanged;

    private void UnhookStrokes() => Ink.Strokes.StrokesChanged -= Strokes_StrokesChanged;

    private void Strokes_StrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        UpdateExtent();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        if (_loading) return;
        UpdateHint();
        _historyTimer.Stop();
        _historyTimer.Start();
        Changed?.Invoke();
    }

    private Snapshot Capture() => new(JsonSerializer.Serialize(CollectItems()), GetInk());

    private static bool Same(Snapshot a, Snapshot b) =>
        a.Items == b.Items && (a.Ink == null ? b.Ink == null : b.Ink != null && a.Ink.AsSpan().SequenceEqual(b.Ink));

    // I save a snapshot a moment after I stop changing things so one Ctrl+Z undoes one step
    private void Commit()
    {
        _historyTimer.Stop();
        var now = Capture();
        if (_current != null && Same(now, _current)) return;
        if (_current != null)
        {
            _undo.Add(_current);
            if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        }
        _current = now;
        _redo.Clear();
    }

    private void Restore(Snapshot snapshot)
    {
        var focused = FocusedElement();
        Point? focusSpot = focused != null ? new Point(Safe(Canvas.GetLeft(focused)), Safe(Canvas.GetTop(focused))) : null;

        _loading = true;
        try
        {
            ItemsLayer.Children.Clear();
            _activeText = null;
            _activeImage = null;
            var items = JsonSerializer.Deserialize<List<PageItem>>(snapshot.Items) ?? new List<PageItem>();
            foreach (var item in items.OrderBy(i => i.Z)) AddElement(item);
            UnhookStrokes();
            Ink.Strokes = LoadStrokes(snapshot.Ink);
            HookStrokes();
        }
        finally
        {
            _loading = false;
        }

        UpdateHint();
        Dispatcher.InvokeAsync(UpdateExtent, DispatcherPriority.Loaded);
        Changed?.Invoke();

        // I put the caret back in the same box if I was typing when I pressed undo
        if (focusSpot is { } spot)
        {
            var match = ItemsLayer.Children.OfType<UIElement>().FirstOrDefault(c =>
                Math.Abs(Safe(Canvas.GetLeft(c)) - spot.X) < 1 && Math.Abs(Safe(Canvas.GetTop(c)) - spot.Y) < 1);
            if (match is PageTextItem text) Dispatcher.InvokeAsync(text.FocusAtEnd, DispatcherPriority.Input);
            else if (match is PageChecklistItem list) list.FocusAtEnd();
        }
    }

    // I draw lines, a grid or dots behind my writing if I've picked a page style
    private void ApplyPageStyle()
    {
        var colour = (TryFindResource("Brush.Border") as SolidColorBrush)?.Color ?? Color.FromRgb(0xE2, 0xDA, 0xEF);
        var lineBrush = new SolidColorBrush(colour);
        lineBrush.Freeze();
        var pen = new Pen(lineBrush, 1);
        pen.Freeze();

        Surface.Background = _pageStyle switch
        {
            "Lined" => Tile(32, new LineGeometry(new Point(0, 31.5), new Point(32, 31.5)), pen, null),
            "Grid" => Tile(24, new GeometryGroup
            {
                Children =
                {
                    new LineGeometry(new Point(0, 23.5), new Point(24, 23.5)),
                    new LineGeometry(new Point(23.5, 0), new Point(23.5, 24))
                }
            }, pen, null),
            "Dots" => Tile(22, new EllipseGeometry(new Point(11, 11), 1.4, 1.4), null, lineBrush),
            _ => Brushes.Transparent
        };
    }

    private static Brush Tile(double size, Geometry geometry, Pen? pen, Brush? fill)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
        group.Children.Add(new GeometryDrawing(fill, pen, geometry));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, size, size),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    private static BitmapSource? ReadClipboardImage(IDataObject data)
    {
        // I prefer the PNG copy because it keeps transparency and colours right, and fall back to the plain bitmap if it won't decode
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
            {
                var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                Log.Info("Paste", $"Read the PNG picture from the clipboard: {frame.PixelWidth} x {frame.PixelHeight}, {png.Length:N0} bytes");
                return frame;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Paste", "The PNG copy on the clipboard wouldn't decode, trying the plain bitmap", ex);
        }

        try
        {
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                // I drop the alpha channel here because Windows often hands over screenshots with it set to invisible
                Log.Info("Paste", image == null ? "Windows said there's a picture but gave nothing back" : $"Read the bitmap picture from the clipboard: {image.PixelWidth} x {image.PixelHeight}, format {image.Format}");
                return image == null ? null : new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            }
        }
        catch (Exception ex)
        {
            // I just let the normal paste happen if the clipboard picture can't be read
            Log.Error("Paste", "The clipboard picture couldn't be read", ex);
        }
        return null;
    }

    private static double Safe(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
}
