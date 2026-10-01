using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I'm a checklist sitting right on my page: Enter adds a line, Backspace on an empty line removes it
public partial class PageChecklistItem : UserControl, IPageElement
{
    public const double MinListWidth = 160;

    public event Action? Changed;
    public event Action<UIElement>? RemoveRequested;
    public event Action<UIElement, bool>? OrderRequested;

    public PageChecklistItem()
    {
        InitializeComponent();

        MoveThumb.DragDelta += MoveThumb_DragDelta;
        MoveThumb.DragCompleted += (_, _) => Changed?.Invoke();
        WidthThumb.DragDelta += (_, e) => Width = Math.Max(MinListWidth, Width + e.HorizontalChange);
        WidthThumb.DragCompleted += (_, _) => Changed?.Invoke();

        MouseEnter += (_, _) => UpdateChrome();
        MouseLeave += (_, _) => UpdateChrome();
        IsKeyboardFocusWithinChanged += (_, _) =>
        {
            UpdateChrome();
            if (!IsKeyboardFocusWithin && IsBlank && Keyboard.FocusedElement != null)
            {
                // I tidy away a checklist I never wrote anything in, same as an empty text box
                Dispatcher.InvokeAsync(() =>
                {
                    if (IsBlank && !IsKeyboardFocusWithin) RemoveRequested?.Invoke(this);
                });
            }
        };
    }

    private IEnumerable<(CheckBox Box, TextBox Line)> Lines =>
        Rows.Children.OfType<DockPanel>().Select(row => ((CheckBox)row.Children[0], (TextBox)row.Children[1]));

    public bool IsBlank => Lines.All(l => string.IsNullOrWhiteSpace(l.Line.Text));

    public string PlainText => DayText.ChecklistText(GetChecks());

    public PageItem ToItem(double x, double y, int z) => new()
    {
        Kind = "checklist",
        X = x,
        Y = y,
        Z = z,
        Width = double.IsNaN(Width) ? MinListWidth : Width,
        Checks = GetChecks()
    };

    public List<CheckEntry> GetChecks() =>
        Lines.Select(l => new CheckEntry { Text = l.Line.Text, Done = l.Box.IsChecked == true }).ToList();

    public void SetChecks(IEnumerable<CheckEntry>? checks)
    {
        Rows.Children.Clear();
        foreach (var check in checks ?? Enumerable.Empty<CheckEntry>()) AddRow(Rows.Children.Count, check.Text, check.Done);
        if (Rows.Children.Count == 0) AddRow(0, "", false);
    }

    public void FocusFirst() => FocusLine(0, false);

    public void FocusAtEnd() => FocusLine(Rows.Children.Count - 1, true);

    private TextBox AddRow(int index, string text, bool done)
    {
        var box = new CheckBox { IsChecked = done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0), Focusable = false };
        var line = new TextBox
        {
            Style = (Style)FindResource("PlainTextBox"),
            Text = text,
            FontSize = PageTextItem.DefaultFontSize,
            AcceptsReturn = false,
            VerticalAlignment = VerticalAlignment.Center,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-GB")
        };
        if (PageTextItem.WritingFont != null) line.FontFamily = PageTextItem.WritingFont;
        SpellCheck.SetIsEnabled(line, PageTextItem.SpellCheckOn);

        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(line);
        Rows.Children.Insert(index, row);

        box.Checked += (_, _) => LineTicked(box, line);
        box.Unchecked += (_, _) => LineTicked(box, line);
        line.TextChanged += (_, _) => Changed?.Invoke();
        line.PreviewKeyDown += (_, e) => Line_PreviewKeyDown(row, line, e);
        ShowDone(line, done);
        return line;
    }

    private void LineTicked(CheckBox box, TextBox line)
    {
        ShowDone(line, box.IsChecked == true);
        Changed?.Invoke();
    }

    // I cross off and fade the lines I've done
    private static void ShowDone(TextBox line, bool done)
    {
        line.TextDecorations = done ? TextDecorations.Strikethrough : null;
        if (done) line.SetResourceReference(ForegroundProperty, "Brush.TextFaint");
        else line.SetResourceReference(ForegroundProperty, "Brush.Text");
    }

    private void Line_PreviewKeyDown(DockPanel row, TextBox line, KeyEventArgs e)
    {
        var index = Rows.Children.IndexOf(row);
        switch (e.Key)
        {
            case Key.Enter:
                // I split the line at the caret so Enter works like it does anywhere else
                var rest = line.Text[line.CaretIndex..];
                line.Text = line.Text[..line.CaretIndex];
                var next = AddRow(index + 1, rest, false);
                Dispatcher.InvokeAsync(() =>
                {
                    Keyboard.Focus(next);
                    next.CaretIndex = 0;
                });
                Changed?.Invoke();
                e.Handled = true;
                break;
            case Key.Back when line.CaretIndex == 0 && line.SelectionLength == 0 && index > 0:
                // I join this line onto the one above, or remove it if it's empty
                var (_, above) = Lines.ElementAt(index - 1);
                var join = above.Text.Length;
                above.Text += line.Text;
                Rows.Children.Remove(row);
                Keyboard.Focus(above);
                above.CaretIndex = join;
                Changed?.Invoke();
                e.Handled = true;
                break;
            case Key.Up when index > 0 && line.GetLineIndexFromCharacterIndex(line.CaretIndex) <= 0:
                FocusLine(index - 1, true);
                e.Handled = true;
                break;
            case Key.Down when index < Rows.Children.Count - 1 && line.GetLineIndexFromCharacterIndex(line.CaretIndex) >= line.LineCount - 1:
                FocusLine(index + 1, false);
                e.Handled = true;
                break;
            case Key.Space when Keyboard.Modifiers == ModifierKeys.Control:
                // I tick or untick the line I'm on with Ctrl+Space
                var box = (CheckBox)row.Children[0];
                box.IsChecked = box.IsChecked != true;
                e.Handled = true;
                break;
        }
    }

    private void FocusLine(int index, bool atEnd)
    {
        if (index < 0 || index >= Rows.Children.Count) return;
        var (_, line) = Lines.ElementAt(index);
        Dispatcher.InvokeAsync(() =>
        {
            Keyboard.Focus(line);
            line.CaretIndex = atEnd ? line.Text.Length : 0;
        });
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Canvas.SetLeft(this, Math.Max(0, Canvas.GetLeft(this) + e.HorizontalChange));
        Canvas.SetTop(this, Math.Max(0, Canvas.GetTop(this) + e.VerticalChange));
    }

    private void UpdateChrome()
    {
        var active = IsMouseOver || IsKeyboardFocusWithin;
        Frame.Opacity = active ? 1 : 0;
        MoveThumb.Opacity = active ? 1 : 0;
        WidthThumb.Opacity = active ? 1 : 0;
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        AddRow(Rows.Children.Count, "", false);
        FocusAtEnd();
        Changed?.Invoke();
    }

    private void Untick_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (box, _) in Lines.ToList()) box.IsChecked = false;
    }

    private void RemoveDone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.Children.OfType<DockPanel>().ToList())
        {
            if (((CheckBox)row.Children[0]).IsChecked == true) Rows.Children.Remove(row);
        }
        if (Rows.Children.Count == 0) AddRow(0, "", false);
        Changed?.Invoke();
    }

    private void Front_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, true);

    private void Back_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, false);

    private void Delete_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this);
}
