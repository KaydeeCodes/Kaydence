using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I'm one free floating text box on the page, like a OneNote container, and I can turn into a sticky note too
public partial class PageTextItem : UserControl, IPageElement
{
    public const double MinBoxWidth = 120;

    // I keep my sticky note colours soft so dark writing always reads well on them
    public static readonly IReadOnlyList<(string Name, string Hex)> StickyColours = new List<(string, string)>
    {
        ("Yellow", "#FFF1A8"),
        ("Pink", "#FFD3E2"),
        ("Blue", "#CFE6FF"),
        ("Green", "#D4F5CF"),
        ("Purple", "#E6DAFF")
    };

    // I set these from my settings so new text boxes match how I like to write
    public static double DefaultFontSize { get; set; } = 15;
    public static bool SpellCheckOn { get; set; } = true;
    public static FontFamily? WritingFont { get; set; }
    public static bool Roomy { get; set; }

    // I know every font I offer, so saved writing can switch to whichever one I pick now
    private static readonly HashSet<string> KnownFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Segoe UI", "Verdana", "Comic Sans MS", "Century Gothic", "Segoe Print"
    };

    public event Action? Changed;
    public event Action<UIElement>? RemoveRequested;
    public event Action<UIElement, bool>? OrderRequested;

    public PageTextItem()
    {
        InitializeComponent();

        Editor.FontSize = DefaultFontSize;
        if (WritingFont != null) Editor.FontFamily = WritingFont;
        if (Roomy) Editor.Document.LineHeight = Math.Round(DefaultFontSize * 1.8);
        SpellCheck.SetIsEnabled(Editor, SpellCheckOn);

        Editor.TextChanged += (_, _) => Changed?.Invoke();
        Editor.LostKeyboardFocus += Editor_LostKeyboardFocus;
        DataObject.AddPastingHandler(Editor, OnPasting);

        MoveThumb.DragDelta += MoveThumb_DragDelta;
        MoveThumb.DragCompleted += (_, _) => Changed?.Invoke();
        WidthThumb.DragDelta += (_, e) => Width = Math.Max(MinBoxWidth, Width + e.HorizontalChange);
        WidthThumb.DragCompleted += (_, _) => Changed?.Invoke();

        MouseEnter += (_, _) => UpdateChrome();
        MouseLeave += (_, _) => UpdateChrome();
        IsKeyboardFocusWithinChanged += (_, _) => UpdateChrome();

        foreach (var (name, hex) in StickyColours)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) =>
            {
                SetSticky(hex);
                Changed?.Invoke();
            };
            ColourMenu.Items.Add(item);
        }
    }

    // I'm only a sticky note when I've got a paper colour
    public string? StickyColour { get; private set; }

    public bool IsSticky => StickyColour != null;

    public string PlainText => new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text.Trim();

    public bool IsEmpty => string.IsNullOrWhiteSpace(new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text);

    // I keep empty sticky notes because the coloured square is there on purpose, but empty text boxes just tidy away
    public bool IsBlank => !IsSticky && IsEmpty;

    public PageItem ToItem(double x, double y, int z) => new()
    {
        Kind = IsSticky ? "sticky" : "text",
        X = x,
        Y = y,
        Z = z,
        Width = double.IsNaN(Width) ? MinBoxWidth : Width,
        Xaml = GetXaml(),
        Color = StickyColour
    };

    public void SetSticky(string? hex)
    {
        Color colour = default;
        var valid = false;
        try
        {
            if (hex != null && ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                colour = parsed;
                valid = true;
            }
        }
        catch (FormatException)
        {
            // I fall back to a plain text box if a saved colour is ever broken
        }

        StickyColour = valid ? hex : null;
        if (valid)
        {
            // I always write in dark ink on a sticky note, whatever the theme is
            var ink = new SolidColorBrush(ThemeService.LightText);
            Paper.Background = new SolidColorBrush(colour);
            Paper.Visibility = Visibility.Visible;
            Editor.Foreground = ink;
            Editor.CaretBrush = ink;
            Editor.Margin = new Thickness(10, 0, 10, 10);
            MinHeight = 120;
            DeleteMenu.Header = "Delete this sticky note";
            ColourMenu.Visibility = Visibility.Visible;
            ColourSeparator.Visibility = Visibility.Visible;
        }
        else
        {
            Paper.Visibility = Visibility.Collapsed;
            Editor.SetResourceReference(ForegroundProperty, "Brush.Text");
            Editor.SetResourceReference(RichTextBox.CaretBrushProperty, "Brush.Text");
            Editor.Margin = new Thickness(0);
            MinHeight = 0;
            DeleteMenu.Header = "Delete this text";
            ColourMenu.Visibility = Visibility.Collapsed;
            ColourSeparator.Visibility = Visibility.Collapsed;
        }
        UpdateChrome();
    }

    public string GetXaml()
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Xaml);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    public void SetXaml(string xaml)
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        range.Load(stream, DataFormats.Xaml);
        foreach (var block in Editor.Document.Blocks.ToList()) ClearThemeColour(block);
    }

    public void SetPlainText(string text)
    {
        new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text = text;
    }

    public void FocusAtEnd()
    {
        Editor.Focus();
        Keyboard.Focus(Editor);
        Editor.CaretPosition = Editor.Document.ContentEnd;
    }

    // I type a bit of text right where my caret is, like the time
    public void InsertText(string text)
    {
        Editor.Focus();
        Editor.Selection.Text = text;
        Editor.CaretPosition = Editor.Selection.End;
    }

    // I do all the formatting from my ribbon: bold, colours, highlights, lists and headings
    public void Format(string action, Color? colour = null)
    {
        Editor.Focus();
        var selection = Editor.Selection;
        switch (action)
        {
            case "bold":
                EditingCommands.ToggleBold.Execute(null, Editor);
                break;
            case "italic":
                EditingCommands.ToggleItalic.Execute(null, Editor);
                break;
            case "underline":
                EditingCommands.ToggleUnderline.Execute(null, Editor);
                break;
            case "bullets":
                EditingCommands.ToggleBullets.Execute(null, Editor);
                break;
            case "numbers":
                EditingCommands.ToggleNumbering.Execute(null, Editor);
                break;
            case "bigger":
                EditingCommands.IncreaseFontSize.Execute(null, Editor);
                break;
            case "smaller":
                EditingCommands.DecreaseFontSize.Execute(null, Editor);
                break;
            case "colour":
                // I use the plain theme colour for automatic, it gets cleared on load so it flips with light and dark
                var ink = colour ?? (IsSticky || !ThemeService.IsDark ? ThemeService.LightText : ThemeService.DarkText);
                selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(ink));
                break;
            case "highlight":
                selection.ApplyPropertyValue(TextElement.BackgroundProperty,
                    colour is { } marker ? new SolidColorBrush(marker) : Brushes.Transparent);
                break;
            case "title":
                SetParagraphLook(DefaultFontSize + 11, FontWeights.SemiBold);
                break;
            case "heading":
                SetParagraphLook(DefaultFontSize + 4, FontWeights.SemiBold);
                break;
            case "normal":
                SetParagraphLook(DefaultFontSize, FontWeights.Normal);
                break;
            case "clear":
                if (!selection.IsEmpty) selection.ClearAllProperties();
                break;
        }
        Changed?.Invoke();
    }

    // I size whole lines for titles and headings, so I don't have to select the words first
    private void SetParagraphLook(double size, FontWeight weight)
    {
        var first = Editor.Selection.Start.Paragraph;
        var last = Editor.Selection.End.Paragraph;
        var range = new TextRange(first?.ContentStart ?? Editor.Selection.Start, last?.ContentEnd ?? Editor.Selection.End);
        range.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        range.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
        foreach (var paragraph in new[] { first, last })
        {
            if (paragraph == null) continue;
            paragraph.FontSize = size;
            paragraph.FontWeight = weight;
        }
    }

    // I work out where the caret is on the page so pasted pictures land right under what I'm writing
    public Point CaretPointOn(UIElement target)
    {
        var rect = Editor.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty) rect = new Rect(0, ActualHeight, 0, 0);
        return Editor.TranslatePoint(rect.BottomLeft, target);
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Canvas.SetLeft(this, Math.Max(0, Canvas.GetLeft(this) + e.HorizontalChange));
        Canvas.SetTop(this, Math.Max(0, Canvas.GetTop(this) + e.VerticalChange));
    }

    private void UpdateChrome()
    {
        var active = IsMouseOver || IsKeyboardFocusWithin;
        Frame.Opacity = active && !IsSticky ? 1 : 0;
        MoveThumb.Opacity = active ? 1 : IsSticky ? 0.35 : 0;
        WidthThumb.Opacity = active ? 1 : 0;
    }

    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // I only tidy away empty boxes when I click somewhere else in the app, not when I alt tab away
        if (e.NewFocus == null || !IsBlank) return;
        if (e.NewFocus is Visual visual && IsAncestorOf(visual)) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (IsBlank && !IsKeyboardFocusWithin) RemoveRequested?.Invoke(this);
        });
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        // I strip pictures out of rich text pastes because pictures should float freely on the page instead
        if (e.FormatToApply == DataFormats.Rtf
            && e.DataObject.GetData(DataFormats.Rtf) is string rtf
            && rtf.Contains("\\pict", StringComparison.Ordinal)
            && e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.FormatToApply = DataFormats.UnicodeText;
        }
    }

    // I clear saved colours and fonts that only came from my settings so old writing follows the theme and font I use now
    private static void ClearThemeColour(TextElement element)
    {
        if (element.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family && KnownFonts.Contains(family.Source))
        {
            element.ClearValue(TextElement.FontFamilyProperty);
        }

        if (element.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush brush
            && (brush.Color == ThemeService.LightText || brush.Color == ThemeService.DarkText))
        {
            element.ClearValue(TextElement.ForegroundProperty);
        }

        switch (element)
        {
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines.ToList()) ClearThemeColour(inline);
                break;
            case Span span:
                foreach (var inline in span.Inlines.ToList()) ClearThemeColour(inline);
                break;
            case System.Windows.Documents.List list:
                foreach (var item in list.ListItems.ToList()) ClearThemeColour(item);
                break;
            case ListItem listItem:
                foreach (var block in listItem.Blocks.ToList()) ClearThemeColour(block);
                break;
            case Section section:
                foreach (var block in section.Blocks.ToList()) ClearThemeColour(block);
                break;
        }
    }

    private void Front_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, true);

    private void Back_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, false);

    private void Delete_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this);
}
