using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I show my days since counters in the sidebar, like 142 days since I started HRT
public sealed class CountersPanel : StackPanel
{
    private DiaryStore? _store;
    private List<Counter> _counters = new();

    public void Initialise(DiaryStore store)
    {
        _store = store;
        _counters = store.LoadCounters();
        Render();
    }

    // I get called at midnight and after a restore so the numbers stay right
    public void Reload()
    {
        if (_store == null) return;
        _counters = _store.LoadCounters();
        Render();
    }

    public void Render()
    {
        Children.Clear();

        var header = new DockPanel { Margin = new Thickness(4, 22, 0, 8) };
        var add = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "\uE710",
            Width = 24,
            Height = 24,
            FontSize = 11,
            ToolTip = "Add a days since counter"
        };
        add.Click += (_, _) => Edit(null);
        DockPanel.SetDock(add, Dock.Right);
        header.Children.Add(add);
        var title = UiKit.Text("Days since", 11, "Brush.TextFaint", FontWeights.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        Children.Add(header);

        if (_counters.Count == 0)
        {
            var empty = UiKit.Text("Count the days since something, like starting HRT or going alcohol free.", 12, "Brush.TextMuted", wrap: true);
            empty.Margin = new Thickness(4, 0, 0, 0);
            Children.Add(empty);
            return;
        }

        foreach (var counter in _counters.OrderBy(c => c.Since)) Children.Add(Tile(counter));
    }

    private FrameworkElement Tile(Counter counter)
    {
        var days = CounterMaths.Days(counter.Since);
        var ahead = days < 0;

        var number = UiKit.Text(Math.Abs(days).ToString("N0", CultureInfo.CurrentCulture), 20, "Brush.Accent", FontWeights.SemiBold);
        number.VerticalAlignment = VerticalAlignment.Bottom;
        var unit = UiKit.Text(ahead ? (days == -1 ? "day to go" : "days to go") : (days == 1 ? "day" : "days"), 12, "Brush.TextMuted");
        unit.Margin = new Thickness(6, 0, 0, 3);
        unit.VerticalAlignment = VerticalAlignment.Bottom;

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(number);
        top.Children.Add(unit);

        var body = new StackPanel();
        body.Children.Add(top);
        var name = UiKit.Text(counter.Name, 12.5, "Brush.Text");
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        body.Children.Add(name);
        if (CounterMaths.IsMilestone(counter.Since))
        {
            var party = UiKit.Text("", 11.5, "Brush.Accent", FontWeights.SemiBold);
            party.Margin = new Thickness(0, 3, 0, 0);
            party.Inlines.Add(new Run("\uE734") { FontFamily = (FontFamily)FindResource("Font.Icons") });
            party.Inlines.Add(new Run("  Milestone today"));
            body.Children.Add(party);
        }

        var card = UiKit.Card(body, new Thickness(12, 8, 12, 8));
        card.Margin = new Thickness(0, 0, 0, 6);
        card.ToolTip = $"Since {counter.Since.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)}, {CounterMaths.Describe(counter.Since)}";

        var menu = new ContextMenu();
        var change = new MenuItem { Header = "Change this counter" };
        change.Click += (_, _) => Edit(counter);
        var restart = new MenuItem { Header = "Start again from today" };
        restart.Click += (_, _) => Restart(counter);
        var delete = new MenuItem { Header = "Delete this counter" };
        delete.Click += (_, _) => Delete(counter);
        menu.Items.Add(change);
        menu.Items.Add(restart);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        card.ContextMenu = menu;

        UiKit.Clickable(card, () => Edit(counter));
        return card;
    }

    private void Edit(Counter? counter)
    {
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var dialog = new CounterDialog(owner, counter);
        if (dialog.ShowDialog() != true) return;

        var index = counter == null ? -1 : _counters.FindIndex(c => c.Id == counter.Id);
        if (index >= 0) _counters[index] = dialog.Result;
        else _counters.Add(dialog.Result);
        Save();
    }

    // I reset a counter kindly, a slip is just a new start
    private void Restart(Counter counter)
    {
        var owner = Window.GetWindow(this);
        var answer = MessageBox.Show(owner!, $"Start \"{counter.Name}\" again from today?\n\nEvery day still counted, this is just a fresh start.",
            "Start again", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        counter.Since = CounterMaths.Today;
        Save();
    }

    private void Delete(Counter counter)
    {
        var owner = Window.GetWindow(this);
        var answer = MessageBox.Show(owner!, $"Delete the \"{counter.Name}\" counter?", "Delete counter",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _counters.RemoveAll(c => c.Id == counter.Id);
        Save();
    }

    private void Save()
    {
        try
        {
            _store?.SaveCounters(_counters);
            Log.Info("Counters", $"Saved {_counters.Count} counters");
        }
        catch (Exception ex)
        {
            Log.Error("Counters", "Counters couldn't be saved", ex);
            MessageBox.Show(Window.GetWindow(this)!, $"Your counters couldn't be saved.\n\n{ex.Message}", "Kaydence",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Render();
    }
}
