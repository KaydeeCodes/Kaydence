using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kaydence.Controls;
using Kaydence.Services;

namespace Kaydence;

// I greet people the first time Kaydence opens and walk through the few choices that matter
public sealed class WelcomeWindow : Window
{
    public static readonly IReadOnlyList<(string Value, string Label)> Fonts = new List<(string, string)>
    {
        ("Segoe UI", "Segoe UI"),
        ("Verdana", "Verdana"),
        ("Comic Sans MS", "Comic Sans"),
        ("Century Gothic", "Century Gothic"),
        ("Segoe Print", "Handwriting")
    };

    private readonly AppSettings _s;
    private readonly Action _applied;
    private readonly List<FrameworkElement> _steps = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back;
    private readonly Button _next;
    private readonly ContentControl _host = new();
    private int _step;

    public WelcomeWindow(Window owner, AppSettings settings, Action applied)
    {
        Owner = owner;
        _s = settings;
        _applied = applied;
        Title = "Welcome to Kaydence";
        Width = 600;
        Height = 660;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        _steps.Add(Hello());
        _steps.Add(Look());
        _steps.Add(Tracking());
        _steps.Add(Privacy());
        _steps.Add(Tips());

        _back = new Button { Content = "Back", Style = (Style)FindResource("SoftButton"), MinWidth = 96, Height = 38, Margin = new Thickness(0, 0, 8, 0) };
        _back.Click += (_, _) => Go(_step - 1);
        _next = new Button { Content = "Next", Style = (Style)FindResource("AccentButton"), MinWidth = 120, Height = 38, IsDefault = true };
        _next.Click += (_, _) =>
        {
            if (_step == _steps.Count - 1) Close();
            else Go(_step + 1);
        };

        var footer = new DockPanel { Margin = new Thickness(32, 0, 32, 24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_dots);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var scroller = new ScrollViewer
        {
            Content = _host,
            Style = (Style)FindResource("PageScrollViewer"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        root.Children.Add(scroller);
        Content = root;

        Closed += (_, _) =>
        {
            Log.Info("Welcome", $"Welcome tour closed on step {_step + 1} of {_steps.Count}");
            _s.FirstRunDone = true;
            Save();
        };
        Go(0);
    }

    private void Save()
    {
        SettingsService.Save(_s);
        _applied();
    }

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, _steps.Count - 1);
        Log.Debug("Welcome", $"Welcome tour step {_step + 1}");
        _host.Content = _steps[_step];
        _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _next.Content = _step == _steps.Count - 1 ? "Start writing" : _step == 0 ? "Let's go" : "Next";

        _dots.Children.Clear();
        for (var i = 0; i < _steps.Count; i++)
        {
            var dot = new Border { Width = i == _step ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, i == _step ? "Brush.Accent" : "Brush.Border");
            _dots.Children.Add(dot);
        }
    }

    private static StackPanel MakePage(string title, string subtitle)
    {
        var page = new StackPanel { Margin = new Thickness(32, 30, 32, 16) };
        page.Children.Add(UiKit.Text(title, 24, "Brush.Text", FontWeights.SemiBold, wrap: true));
        var sub = UiKit.Text(subtitle, 13.5, "Brush.TextMuted", wrap: true);
        sub.Margin = new Thickness(0, 6, 0, 18);
        page.Children.Add(sub);
        return page;
    }

    private static FrameworkElement Feature(string glyph, string title, string text)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 18,
            Width = 40,
            Height = 40,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, 10, 0, 0)
        };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var round = new Border { CornerRadius = new CornerRadius(12), Child = icon, VerticalAlignment = VerticalAlignment.Top };
        round.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");

        var words = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        words.Children.Add(UiKit.Text(title, 14, "Brush.Text", FontWeights.SemiBold, wrap: true));
        var body = UiKit.Text(text, 12.5, "Brush.TextMuted", wrap: true);
        body.Margin = new Thickness(0, 2, 0, 0);
        words.Children.Add(body);

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(round, Dock.Left);
        row.Children.Add(round);
        row.Children.Add(words);
        return row;
    }

    private FrameworkElement Hello()
    {
        var page = new StackPanel { Margin = new Thickness(32, 34, 32, 16) };
        page.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Kaydence.png")),
            Width = 72,
            Height = 72,
            HorizontalAlignment = HorizontalAlignment.Left
        });
        var title = UiKit.Text("Welcome to Kaydence", 28, "Brush.Text", FontWeights.SemiBold);
        title.Margin = new Thickness(0, 16, 0, 4);
        page.Children.Add(title);
        var sub = UiKit.Text("Your own private diary. A few quick choices and you're ready to write.", 14, "Brush.TextMuted", wrap: true);
        sub.Margin = new Thickness(0, 0, 0, 24);
        page.Children.Add(sub);

        page.Children.Add(Feature("\uE72E", "Everything stays on this PC", "No accounts, no cloud, no tracking. Your diary lives in a folder on this PC, backed up every day."));
        page.Children.Add(Feature("\uE70F", "Just click and type", "The page works like OneNote. Write anywhere, paste pictures, draw, add sticky notes and checklists."));
        page.Children.Add(Feature("\uE9D9", "A quick check-in each day", "Mood, sleep, tasks and more on the right. Fill in only what you want, everything is optional."));
        page.Children.Add(Feature("\uE74E", "It saves itself", "There's no save button. Close Kaydence whenever you like."));
        return page;
    }

    private FrameworkElement Look()
    {
        var page = MakePage("Make it yours", "You can change all of this later in Settings.");
        page.Children.Add(MakeLabel("Light or dark"));
        page.Children.Add(Chips(new[] { ("System", "Follow Windows"), ("Light", "Light"), ("Dark", "Dark") }, _s.Theme, v =>
        {
            _s.Theme = v;
            ThemeService.Apply(v);
            Save();
        }));

        page.Children.Add(MakeLabel("Accent colour"));
        page.Children.Add(Chips(ThemeService.Accents.Keys.Select(k => (k, k)), _s.Accent, v =>
        {
            _s.Accent = v;
            ThemeService.ApplyAccent(v);
            Save();
        }));

        page.Children.Add(MakeLabel("Font for your writing"));
        var sample = UiKit.Text("The quick brown fox jumps over the lazy dog", 16, "Brush.Text", wrap: true);
        sample.FontFamily = new FontFamily(_s.WritingFont);
        page.Children.Add(Chips(Fonts, _s.WritingFont, v =>
        {
            _s.WritingFont = v;
            sample.FontFamily = new FontFamily(v);
            Save();
        }));
        sample.Margin = new Thickness(2, 4, 0, 0);
        page.Children.Add(sample);
        var tip = UiKit.Text("Verdana and Comic Sans are often easier to read with dyslexia.", 12, "Brush.TextFaint", wrap: true);
        tip.Margin = new Thickness(2, 6, 0, 0);
        page.Children.Add(tip);

        page.Children.Add(SwitchRow("Roomy spacing", "More space between lines, easier on the eyes", _s.RoomySpacing, v => _s.RoomySpacing = v));
        return page;
    }

    private FrameworkElement Tracking()
    {
        var page = MakePage("What would you like to track?",
            "These are the sections in your daily check-in. Everything is optional, and you can switch them on or off, or change their order, in Settings whenever you like.");

        var everyday = new UniformGrid { Columns = 2 };
        var optional = new[] { "Transition", "Cycle" }.Concat(AppSettings.HealthSections).Concat(AppSettings.MindSections).ToHashSet();
        foreach (var (key, label) in CheckInPanel.Sections.Where(s => !optional.Contains(s.Key)))
        {
            var box = new CheckBox
            {
                Content = label,
                IsChecked = !_s.HiddenSections.Contains(key),
                Margin = new Thickness(0, 0, 12, 12)
            };
            box.Click += (_, _) =>
            {
                _s.ShowSections(box.IsChecked == true, key);
                Save();
            };
            everyday.Children.Add(box);
        }
        page.Children.Add(everyday);

        // I group the health extras so someone who doesn't want them never sees them anywhere
        page.Children.Add(MakeLabel("Optional extras"));
        page.Children.Add(SwitchRow("Health", "Medications, symptoms and pain, blood pressure and weight, with charts and a report for your doctor",
            _s.ShowsAny(AppSettings.HealthSections), v => _s.ShowSections(v, AppSettings.HealthSections)));
        page.Children.Add(SwitchRow("Depression and anxiety", "Rate each one from 1 to 10 every day to see the waves and patterns over time, with charts and a report for your doctor",
            _s.ShowsAny(AppSettings.MindSections), v => _s.ShowSections(v, AppSettings.MindSections)));
        page.Children.Add(SwitchRow("Period and cycle", "Log your flow and Kaydence makes a rough guess at when your next period is due, marked on the calendar",
            _s.ShowsAny("Cycle"), v => _s.ShowSections(v, "Cycle")));
        page.Children.Add(SwitchRow("Transition", "HRT or treatment notes, injection sites and milestones for your transition",
            _s.ShowsAny("Transition"), v => _s.ShowSections(v, "Transition")));
        var note = UiKit.Text("Kaydence isn't a medical app. The health tools are for your own notes, and cycle guesses must never be used as contraception.",
            12, "Brush.TextFaint", wrap: true);
        note.Margin = new Thickness(2, 10, 0, 0);
        page.Children.Add(note);
        return page;
    }

    private FrameworkElement Privacy()
    {
        var page = MakePage("Privacy and reminders", "All optional, you can skip this page.");

        var status = UiKit.Text(HasPassword ? "Your password is on." : "No password yet, Kaydence opens straight away.", 12, "Brush.TextMuted", wrap: true);
        var set = new Button { Content = HasPassword ? "Change your password" : "Set a password", Style = (Style)FindResource("SoftButton"), HorizontalAlignment = HorizontalAlignment.Left };
        set.Click += (_, _) =>
        {
            if (HasPassword && new PasswordDialog(this, _s, false).ShowDialog() != true) return;
            var dialog = new PasswordDialog(this, _s, true);
            if (dialog.ShowDialog() != true || dialog.NewPassword == null) return;
            var wasOn = HasPassword;
            EncryptionService.PasswordChanged(_s, dialog.NewPassword);
            if (!wasOn) EncryptionService.Enable(this, _s, dialog.NewPassword);
            Save();
            status.Text = "Your password is on. You can choose when it asks for it in Settings.";
            set.Content = "Change your password";
        };
        page.Children.Add(MakeLabel("Lock your diary with a password"));
        page.Children.Add(set);
        status.Margin = new Thickness(2, 8, 0, 0);
        page.Children.Add(status);

        page.Children.Add(SwitchRow("Remind me to write", "A little Windows notification if you haven't written anything yet that day",
            _s.ReminderEnabled, v => _s.ReminderEnabled = v));
        page.Children.Add(Chips(ReminderTimes, _s.ReminderTime, v =>
        {
            _s.ReminderTime = v;
            Save();
        }));

        page.Children.Add(SwitchRow("Start Kaydence with Windows", "So it's there when you need it, and the reminder can reach you",
            _s.StartWithWindows, v =>
            {
                _s.StartWithWindows = v;
                StartupService.Apply(v);
            }));
        return page;
    }

    private FrameworkElement Tips()
    {
        var page = MakePage("A few handy tips", "That's everything. Here are some shortcuts worth knowing.");
        page.Children.Add(Feature("\uE8D2", "The ribbon above the page", "Home for bold, colours and headings. Insert for sticky notes, checklists and pictures. Draw for pens and shapes. View for zoom."));
        page.Children.Add(Feature("\uE765", "Keyboard shortcuts", "Ctrl+T today, Alt+Left and Right to flick between days, Ctrl+F search, Ctrl+P print, Ctrl+Z undo."));
        page.Children.Add(Feature("\uE8A7", "Open Kaydence from anywhere", $"Press {HotkeyService.Describe(_s.Hotkey)} in any app. You can change this in Settings."));
        page.Children.Add(Feature("\uE897", "The full guide", "Press F1 any time for the whole guide, it opens in your web browser."));
        var guide = new Button { Content = "Open the guide now", Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left };
        guide.Click += (_, _) => GuideService.Open();
        page.Children.Add(guide);
        return page;
    }

    public static readonly IReadOnlyList<(string Value, string Label)> ReminderTimes = new List<(string, string)>
    {
        ("08:00", "8am"), ("12:00", "Noon"), ("17:00", "5pm"), ("19:00", "7pm"),
        ("20:00", "8pm"), ("21:00", "9pm"), ("22:00", "10pm"), ("23:00", "11pm")
    };

    private bool HasPassword => _s.PasswordEnabled && !string.IsNullOrEmpty(_s.PasswordHash);

    private static TextBlock MakeLabel(string text)
    {
        var label = UiKit.Text(text, 12.5, "Brush.TextMuted", FontWeights.SemiBold);
        label.Margin = new Thickness(2, 16, 0, 8);
        return label;
    }

    private FrameworkElement Chips(IEnumerable<(string Value, string Label)> options, string selected, Action<string> set) =>
        UiKit.ChoiceChips(options, selected, set);

    private FrameworkElement SwitchRow(string title, string description, bool value, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = value, Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            Save();
        };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(UiKit.Text(title, 13.5, "Brush.Text", FontWeights.SemiBold, wrap: true));
        var small = UiKit.Text(description, 12, "Brush.TextMuted", wrap: true);
        small.Margin = new Thickness(0, 2, 0, 0);
        words.Children.Add(small);

        var row = new DockPanel { Margin = new Thickness(0, 20, 0, 6) };
        DockPanel.SetDock(box, Dock.Right);
        box.Margin = new Thickness(16, 0, 0, 0);
        row.Children.Add(box);
        row.Children.Add(words);
        return row;
    }
}
