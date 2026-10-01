using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using Kaydence.Controls;
using Kaydence.Services;
using Microsoft.Win32;

namespace Kaydence;

// I'm the settings window: every choice takes effect straight away and saves itself
public partial class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly DiaryStore _store;
    private readonly Action _applied;
    private readonly Action<string> _restore;
    private readonly Action? _printEverything;
    private readonly List<FrameworkElement> _needsPassword = new();
    private FrameworkElement? _daysRow;
    private TextBlock? _recoveryStatus;

    public SettingsWindow(AppSettings settings, DiaryStore store, Action applied, Action<string> restore, Action? printEverything = null)
    {
        _s = settings;
        _store = store;
        _applied = applied;
        _restore = restore;
        _printEverything = printEverything;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        Build();
    }

    private void Changed()
    {
        SettingsService.Save(_s);
        _applied();
    }

    private void Build()
    {
        var look = Section("\uE790", "Look and feel", "Make Kaydence feel like yours.");
        Row(look, "Theme", null, Choice(new[] { ("System", "Follow Windows"), ("Light", "Light"), ("Dark", "Dark") }, _s.Theme, v =>
        {
            _s.Theme = v;
            ThemeService.Apply(v);
        }));
        Row(look, "Accent colour", "Buttons, headings and today's date", Choice(
            ThemeService.Accents.Keys.Select(k => (k, k)), _s.Accent, v =>
            {
                _s.Accent = v;
                ThemeService.ApplyAccent(v);
            }), below: true);
        Row(look, "Page style", "What a blank page looks like", Choice(new[] { ("Plain", "Plain"), ("Lined", "Lined"), ("Grid", "Grid"), ("Dots", "Dots") }, _s.PageStyle, v => _s.PageStyle = v));
        Row(look, "Text size for new writing", null, Choice(new[] { ("13", "Small"), ("15", "Medium"), ("17", "Large"), ("20", "Extra large") },
            _s.TextSize.ToString(CultureInfo.InvariantCulture), v => _s.TextSize = double.Parse(v, CultureInfo.InvariantCulture)), below: true);
        Row(look, "Weeks start on", null, Choice(new[] { ("Mon", "Monday"), ("Sun", "Sunday") }, _s.WeekStartsMonday ? "Mon" : "Sun", v => _s.WeekStartsMonday = v == "Mon"));
        Row(look, "Font for your writing", "Verdana and Comic Sans are often easier to read with dyslexia",
            Choice(WelcomeWindow.Fonts, _s.WritingFont, v => _s.WritingFont = v), below: true);
        Row(look, "Roomy spacing", "More space between lines of writing, easier on the eyes", MakeSwitch(_s.RoomySpacing, v => _s.RoomySpacing = v));
        Row(look, "Show the mood key", "The little list of faces under the calendar", MakeSwitch(_s.ShowMoodKey, v => _s.ShowMoodKey = v));
        Row(look, "Show days since counters", "Under the calendar, like days since you went alcohol free. Right click one to change it",
            MakeSwitch(_s.ShowCounters, v => _s.ShowCounters = v));
        Row(look, "Show tips when you hover over buttons", null, MakeSwitch(_s.ShowTooltips, v => _s.ShowTooltips = v));

        var panel = Section("\uE9D5", "Today panel", "Choose what shows in your check-in on the right, and in what order.");
        Row(panel, "Open the Today panel when Kaydence starts", null, MakeSwitch(_s.CheckInOpen, v => _s.CheckInOpen = v));
        Row(panel, "Weight", "How you like to see your weight", Choice(new[] { ("kg", "kg"), ("st", "Stone and pounds"), ("lb", "Pounds") }, _s.WeightUnit, v => _s.WeightUnit = v), below: true);
        Note(panel, "Switch sections on or off, and use the arrows to put them in the order you like. You can also right click a section in the Today panel to move or hide it.");
        var sectionList = new StackPanel();
        panel.Children.Add(sectionList);
        FillSections(sectionList);

        var writing = Section("\uE70F", "Writing", "How the page behaves.");
        Row(writing, "Spell check", "Takes effect on text boxes you open or make from now on", MakeSwitch(_s.SpellCheck, v => _s.SpellCheck = v));

        var reminder = Section("\uEA8F", "Reminder", "A gentle nudge to write.");
        Row(reminder, "Remind me to write", "A little Windows notification, click it to jump straight to today",
            MakeSwitch(_s.ReminderEnabled, v => _s.ReminderEnabled = v));
        Row(reminder, "Remind me at", null, Choice(WelcomeWindow.ReminderTimes, _s.ReminderTime, v =>
        {
            _s.ReminderTime = v;
            _s.LastReminder = null;
        }), below: true);
        Row(reminder, "Only if nothing has been written that day", null, MakeSwitch(_s.ReminderOnlyIfEmpty, v => _s.ReminderOnlyIfEmpty = v));
        Note(reminder, "Kaydence needs to be running for this, even tucked away by the clock. Starting it with Windows and closing it to the clock is the easy way.");

        var starting = Section("\uE7E8", "Starting and closing", "How Kaydence behaves with Windows.");
        Row(starting, "Start Kaydence when you sign in to Windows", null, MakeSwitch(_s.StartWithWindows, v =>
        {
            _s.StartWithWindows = v;
            StartupService.Apply(v);
        }));
        Row(starting, "Start hidden in the notification area", "Only when it starts with Windows", MakeSwitch(_s.StartHidden, v => _s.StartHidden = v));
        Row(starting, "Show Kaydence in the notification area", "The little icon by the clock", MakeSwitch(_s.ShowTrayIcon, v => _s.ShowTrayIcon = v));
        Row(starting, "Closing the window keeps Kaydence running", "It tucks away by the clock, right click the icon to exit", MakeSwitch(_s.CloseToTray, v => _s.CloseToTray = v));
        Row(starting, "Minimising hides Kaydence by the clock", null, MakeSwitch(_s.MinimiseToTray, v => _s.MinimiseToTray = v));
        Row(starting, "Shortcut to open Kaydence from anywhere", "Works in any app while Kaydence is running",
            Choice(HotkeyService.Choices, _s.Hotkey, v => _s.Hotkey = v), below: true);

        var privacy = Section("\uE72E", "Privacy and lock", "Your diary never leaves this PC.");
        CheckBox? passwordSwitch = null;
        passwordSwitch = MakeSwitch(HasPassword, v =>
        {
            TogglePassword(v, passwordSwitch!);
            UpdatePrivacyRows();
        });
        Row(privacy, "Lock with a password", "Ask for your password before showing your diary", passwordSwitch);
        _needsPassword.Add(Row(privacy, "Change your password", null, ActionButton("Change", ChangePassword)));

        _recoveryStatus = UiKit.Text(RecoveryText(), 12, "Brush.TextMuted", wrap: true);
        _needsPassword.Add(Row(privacy, "Recovery file", "A spare key for the lock screen if you ever forget your password",
            ActionButton("Make a new one", MakeRecoveryFile), extra: _recoveryStatus));

        _needsPassword.Add(Row(privacy, "Ask for your password", null, Choice(new[]
        {
            ("Always", "Every time Kaydence opens"), ("AfterDays", "Only if you haven't used it for a while")
        }, _s.LockMode, v =>
        {
            _s.LockMode = v;
            EncryptionService.UpdateDeviceKey(_s);
            UpdatePrivacyRows();
        }), below: true));
        _daysRow = Row(privacy, "A while means this many days", "If you haven't opened Kaydence for this long, it asks for your password",
            NumberBox(_s.LockAfterDays, 1, 365, v => _s.LockAfterDays = v));
        _needsPassword.Add(_daysRow);
        _needsPassword.Add(Row(privacy, "Lock when you step away", "When you haven't touched Kaydence for a while", Choice(new[]
        {
            ("0", "Never"), ("5", "5 minutes"), ("15", "15 minutes"), ("30", "30 minutes"), ("60", "1 hour")
        }, _s.IdleLockMinutes.ToString(CultureInfo.InvariantCulture), v => _s.IdleLockMinutes = int.Parse(v, CultureInfo.InvariantCulture)), below: true));
        _needsPassword.Add(Row(privacy, "Lock when it's hidden by the clock", null, MakeSwitch(_s.LockWhenHidden, v => _s.LockWhenHidden = v)));
        Note(privacy, "When the password is on, every page, picture and voice note in your diary is encrypted with it, so nobody can read them even by opening the files. " +
                      "Your password and your recovery file are the only ways in, so keep your recovery file somewhere safe. If you lose both, nobody can open your diary, not even the developer.");
        UpdatePrivacyRows();

        var backups = Section("\uE895", "Backups", "A copy of everything so you never lose a thing.");
        Row(backups, "Back up automatically every day", null, MakeSwitch(_s.AutoBackup, v => _s.AutoBackup = v));
        Row(backups, "Keep backups for", null, Choice(new[] { ("7", "7 days"), ("14", "14 days"), ("30", "30 days"), ("90", "90 days") },
            _s.BackupKeepDays.ToString(CultureInfo.InvariantCulture), v => _s.BackupKeepDays = int.Parse(v, CultureInfo.InvariantCulture)));
        var backupPath = UiKit.Text(_s.BackupFolderOrDefault, 12, "Brush.TextMuted", wrap: true);
        Row(backups, "Backup folder", null, ActionButtons(("Change", () =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose where your backups go", InitialDirectory = _s.BackupFolderOrDefault };
            if (dialog.ShowDialog(this) != true) return;
            _s.BackupFolder = dialog.FolderName;
            backupPath.Text = _s.BackupFolderOrDefault;
            Changed();
        }), ("Open", () => OpenFolder(_s.BackupFolderOrDefault))), extra: backupPath);
        Row(backups, "Restore from a backup", "Swaps your diary for a backup. What you have now is zipped up first, just in case",
            ActionButton("Restore...", RestoreFromBackup));
        Row(backups, "Back up now", null, ActionButton("Back up now", () =>
        {
            try
            {
                var file = BackupService.RunNow(_store.Root, _s.BackupFolderOrDefault, _s.BackupKeepDays);
                MessageBox.Show(this, $"Backup saved:\n{file}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"The backup didn't work.\n\n{ex.Message}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }));

        var folder = Section("\uE8B7", "Your diary folder", "Where everything you write is kept.");
        Row(folder, "Diary folder", null, ActionButton("Open", () => OpenFolder(_store.Root)), extra: UiKit.Text(_store.Root, 12, "Brush.TextMuted", wrap: true));
        Row(folder, "Deleted days", "Days you delete from the calendar are moved here, not wiped", ActionButton("Open", () =>
        {
            Directory.CreateDirectory(_store.DeletedFolder);
            OpenFolder(_store.DeletedFolder);
        }));

        var exportStatus = UiKit.Text("", 12, "Brush.TextMuted", wrap: true);
        exportStatus.Visibility = Visibility.Collapsed;
        Row(folder, "Export your diary", "Your words are yours to keep. Make a copy of everything as ordinary files: a web page, a text file for each day, and your pictures and voice notes. Or save it all as a PDF",
            ActionButtons(("Export to a folder", () => ExportDiary(exportStatus)), ("Save as PDF", () =>
            {
                Log.Info("Export", "Opening print for everything, to save as a PDF");
                Close();
                _printEverything?.Invoke();
            })), extra: exportStatus);

        // I work out the space used in the background so settings opens straight away even with a big diary
        var space = UiKit.Text("Working it out...", 12, "Brush.TextMuted", wrap: true);
        Row(folder, "Space used", "How much room your diary, backups and logs take up on this PC", ActionButton("Check again", () => MeasureSpace(space)), extra: space);
        MeasureSpace(space);

        var about = Section("\uE946", "About Kaydence", "");
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var site = new Button { Content = "Made by KaydeeCodes, visit kaydee.codes", Style = (Style)FindResource("LinkButton") };
        site.Click += (_, _) => Links.Open(Links.Website);
        Row(about, $"Kaydence {version?.ToString(3)}", "Your private diary", new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Kaydence.png")),
            Width = 40,
            Height = 40
        }, extra: site);

        var guide = Section("\uE897", "Help and feedback", "Everything explained, and a way to report problems or suggest ideas.");
        Row(guide, "Kaydence guide", "Opens in your web browser. You can also press F1 anywhere in Kaydence", ActionButton("Open the guide", GuideService.Open));
        Row(guide, "Welcome tour", "The first launch walkthrough, if you want to see it again", ActionButton("Show it again", () =>
        {
            new WelcomeWindow(this, _s, _applied).ShowDialog();
            Changed();
        }));
        Row(guide, "Make a bug report", "Zips the last few days of logs and a summary of your settings, so a problem is easy to track down. Nothing from your diary is ever included",
            ActionButton("Make a bug report", MakeBugReport));
        Row(guide, "Report a problem", "Opens a new issue on GitHub with your Kaydence and Windows version filled in. Attach the bug report zip to it",
            ActionButton("Report a problem", () => Links.NewIssue(true)));
        Row(guide, "Logs", "A record of what Kaydence does and any errors, with times and your PC's details. Never anything you wrote",
            ActionButton("Open log folder", () => OpenFolder(Log.Folder)));
        Row(guide, "Suggest a feature", "Got an idea? It would be lovely to hear it", ActionButton("Suggest a feature", () => Links.NewIssue(false)));
        Row(guide, "Check for updates automatically", "Once a day Kaydence asks GitHub for the latest version number. That's the only thing it ever sends",
            MakeSwitch(_s.CheckForUpdates, v => _s.CheckForUpdates = v));
        Row(guide, "Check for updates now", $"You have Kaydence {UpdateService.Current.ToString(3)}", ActionButtons(
            ("Check now", () => UpdateService.CheckNow(this, _s)),
            ("All releases", () => Links.Open(Links.Releases))));
    }

    // I list the Today panel sections in their current order, each with arrows to move it and a switch to show or hide it
    private void FillSections(StackPanel list)
    {
        list.Children.Clear();
        var order = CheckInPanel.Ordered(_s.SectionOrder);
        foreach (var key in order)
        {
            var label = CheckInPanel.Sections.First(s => s.Key == key).Label;
            var index = order.IndexOf(key);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(Arrow("\uE70E", $"Move {label} up", index > 0, () => MoveSection(list, key, -1)));
            controls.Children.Add(Arrow("\uE70D", $"Move {label} down", index < order.Count - 1, () => MoveSection(list, key, 1)));
            var toggle = MakeSwitch(!_s.HiddenSections.Contains(key), v =>
            {
                _s.HiddenSections.Remove(key);
                if (!v) _s.HiddenSections.Add(key);
            });
            toggle.Margin = new Thickness(10, 0, 0, 0);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(toggle);
            Row(list, label, null, controls);
        }
    }

    private Button Arrow(string glyph, string tip, bool enabled, Action click)
    {
        var button = new Button { Content = glyph, Style = (Style)FindResource("IconButton"), ToolTip = tip, IsEnabled = enabled, Opacity = enabled ? 1 : 0.3 };
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private void MoveSection(StackPanel list, string key, int step)
    {
        var order = CheckInPanel.Ordered(_s.SectionOrder);
        var at = order.IndexOf(key);
        var to = Math.Clamp(at + step, 0, order.Count - 1);
        if (to == at) return;
        order.RemoveAt(at);
        order.Insert(to, key);
        _s.SectionOrder = order;
        Changed();
        FillSections(list);
    }

    private async void ExportDiary(TextBlock status)
    {
        if (EncryptionService.IsEncrypted(_s))
        {
            var sure = MessageBox.Show(this,
                "Your diary is encrypted, but the export won't be. Anyone who can open the export folder will be able to read it.\n\nExport anyway?",
                "Export your diary", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (sure != MessageBoxResult.Yes) return;
        }
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to put the export",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true) return;

        status.Visibility = Visibility.Visible;
        status.Text = "Getting started...";
        IsEnabled = false;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => status.Text = $"Exporting day {p.Done} of {p.Total}...");
            var result = await ExportService.RunAsync(_store, dialog.FolderName, _s.WeightUnit, progress);
            status.Text = $"Exported {result.Days} days, {result.Pictures} pictures and {result.VoiceNotes} voice notes" +
                          (result.Problems > 0 ? $". {result.Problems} things couldn't be copied, the log has the details." : ".");
            OpenFolder(result.Folder);
        }
        catch (Exception ex)
        {
            Log.Error("Export", "The export didn't work", ex);
            status.Text = "The export didn't work.";
            MessageBox.Show(this, $"The export didn't work.\n\n{ex.Message}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void MakeBugReport()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save the bug report",
            FileName = BugReport.SuggestedName,
            Filter = "Zip file|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FileName;
        Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            var root = _store.Root;
            await Task.Run(() => BugReport.Create(path, _s, root));
        }
        catch (Exception ex)
        {
            Log.Error("BugReport", "Couldn't make the bug report", ex);
            MessageBox.Show(this, $"The bug report couldn't be saved.\n\n{ex.Message}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            Cursor = null;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("BugReport", "Couldn't show the zip in Explorer", ex);
        }
        var answer = MessageBox.Show(this, "The bug report is saved.\n\nOpen a new problem report on GitHub now? You can drag the zip file into it.",
            "Kaydence", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) Links.NewIssue(true);
    }

    private async void MeasureSpace(TextBlock target)
    {
        target.Text = "Working it out...";
        try
        {
            var root = _store.Root;
            var backups = _s.BackupFolderOrDefault;
            var usage = await Task.Run(() => DiskUsage.Measure(root, backups));
            target.Text = DiskUsage.Describe(usage);
        }
        catch (Exception ex)
        {
            Log.Warn("Space", "Couldn't work out the space used", ex);
            target.Text = "The space used couldn't be worked out this time.";
        }
    }

    private bool HasPassword => _s.PasswordEnabled && !string.IsNullOrEmpty(_s.PasswordHash);

    // I grey out the lock options until I have a password, and only show the days box when it matters
    private void UpdatePrivacyRows()
    {
        foreach (var row in _needsPassword)
        {
            row.IsEnabled = HasPassword;
            row.Opacity = HasPassword ? 1 : 0.45;
        }
        if (_daysRow != null) _daysRow.Visibility = _s.LockMode == "AfterDays" ? Visibility.Visible : Visibility.Collapsed;
        if (_recoveryStatus != null) _recoveryStatus.Text = RecoveryText();
    }

    private string RecoveryText() => RecoveryService.HasRecovery(_s)
        ? $"Saved on {_s.RecoveryCreated?.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) ?? "an earlier day"}. Making a new one stops the old file working."
        : "You haven't saved one yet";

    private void MakeRecoveryFile()
    {
        if (RecoveryService.HasRecovery(_s))
        {
            var answer = MessageBox.Show(this, "Make a new recovery file? Your old recovery file will stop working.", "Kaydence",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        RecoveryService.CreateAndSave(this, _s);
        UpdatePrivacyRows();
    }

    private void RestoreFromBackup()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the backup to restore",
            Filter = "Kaydence backup|Kaydence-*.zip|Zip files|*.zip",
            InitialDirectory = Directory.Exists(_s.BackupFolderOrDefault) ? _s.BackupFolderOrDefault : ""
        };
        if (dialog.ShowDialog(this) != true) return;

        var answer = MessageBox.Show(this,
            $"Restore your diary from this backup?\n\n{Path.GetFileName(dialog.FileName)}\n\n" +
            "Everything will go back to how it was in the backup. Your diary as it is right now will be zipped into your backups folder first, so you can undo this later if you need to.",
            "Restore from a backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _restore(dialog.FileName);
    }

    private StackPanel Section(string glyph, string title, string subtitle)
    {
        var heading = new TextBlock { Text = title, Style = (Style)FindResource("SettingsHeading") };
        Body.Children.Add(heading);
        if (subtitle.Length > 0) Body.Children.Add(UiKit.Text(subtitle, 13, "Brush.TextMuted"));

        var rows = new StackPanel();
        var card = UiKit.Card(rows, new Thickness(18, 2, 18, 2));
        card.Margin = new Thickness(0, 10, 0, 0);
        Body.Children.Add(card);

        var link = new Button { Content = title, Tag = glyph, Style = (Style)FindResource("NavLink") };
        link.Click += (_, _) =>
        {
            var top = heading.TranslatePoint(new Point(0, 0), Body).Y;
            Scroller.ScrollToVerticalOffset(Math.Max(0, top - 8));
        };
        Nav.Children.Add(link);
        return rows;
    }

    // I return the whole row, line and all, so I can hide or grey it out later
    private static FrameworkElement Row(StackPanel rows, string title, string? description, FrameworkElement control, bool below = false, FrameworkElement? extra = null)
    {
        var wrapper = new StackPanel();
        if (rows.Children.Count > 0)
        {
            var line = new Border { Height = 1 };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
            wrapper.Children.Add(line);
        }

        var grid = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Text(title, 13.5, "Brush.Text", FontWeights.SemiBold, wrap: true));
        if (!string.IsNullOrEmpty(description))
        {
            var small = UiKit.Text(description, 12, "Brush.TextMuted", wrap: true);
            small.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(small);
        }
        if (extra != null)
        {
            extra.Margin = new Thickness(0, 4, 0, 0);
            text.Children.Add(extra);
        }
        grid.Children.Add(text);

        if (below)
        {
            control.Margin = new Thickness(0, 10, 0, 0);
            text.Children.Add(control);
        }
        else
        {
            control.Margin = new Thickness(16, 0, 0, 0);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        wrapper.Children.Add(grid);
        rows.Children.Add(wrapper);
        return wrapper;
    }

    private static void Note(StackPanel rows, string text)
    {
        var note = UiKit.Text(text, 12, "Brush.TextMuted", wrap: true);
        note.Margin = new Thickness(0, 4, 0, 14);
        rows.Children.Add(note);
    }

    private CheckBox MakeSwitch(bool value, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = value, Style = (Style)FindResource("Switch") };
        box.Click += (_, _) =>
        {
            set(box.IsChecked == true);
            Changed();
        };
        return box;
    }

    private FrameworkElement Choice(IEnumerable<(string Value, string Label)> options, string selected, Action<string> set) =>
        UiKit.ChoiceChips(options, selected, v =>
        {
            set(v);
            Changed();
        });

    private Button ActionButton(string label, Action click)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("SoftButton") };
        button.Click += (_, _) => click();
        return button;
    }

    private FrameworkElement ActionButtons(params (string Label, Action Click)[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, click) in buttons)
        {
            var button = ActionButton(label, click);
            button.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(button);
        }
        return panel;
    }

    private TextBox NumberBox(int value, int min, int max, Action<int> set)
    {
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Center };
        box.TextChanged += (_, _) =>
        {
            if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number)) return;
            set(Math.Clamp(number, min, max));
            Changed();
        };
        return box;
    }

    private void TogglePassword(bool turnOn, CheckBox toggle)
    {
        if (turnOn)
        {
            var dialog = new PasswordDialog(this, _s, true);
            if (dialog.ShowDialog() == true && dialog.NewPassword != null)
            {
                EncryptionService.PasswordChanged(_s, dialog.NewPassword);
                EncryptionService.Enable(this, _s, dialog.NewPassword);
            }
            else
            {
                toggle.IsChecked = false;
                _s.PasswordEnabled = false;
            }
            return;
        }

        // I have to prove it's me before I can turn the lock off
        var check = new PasswordDialog(this, _s, false);
        if (check.ShowDialog() == true)
        {
            var answer = MessageBox.Show(this,
                "Switch the password off?\n\nYour diary will be decrypted back into normal files, so anyone using this PC could read them.",
                "Kaydence", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                toggle.IsChecked = true;
                return;
            }
            EncryptionService.Disable(this, _s);
            _s.PasswordEnabled = false;
            _s.PasswordHash = null;
            _s.PasswordSalt = null;
            RecoveryService.Clear(_s);
        }
        else
        {
            toggle.IsChecked = true;
        }
    }

    private void ChangePassword()
    {
        if (_s.PasswordEnabled && !string.IsNullOrEmpty(_s.PasswordHash))
        {
            var check = new PasswordDialog(this, _s, false);
            if (check.ShowDialog() != true) return;
        }
        var dialog = new PasswordDialog(this, _s, true);
        if (dialog.ShowDialog() != true || dialog.NewPassword == null) return;
        var wasOn = HasPassword;
        EncryptionService.PasswordChanged(_s, dialog.NewPassword);
        if (!wasOn) EncryptionService.Enable(this, _s, dialog.NewPassword);
        Changed();
        if (wasOn && !RecoveryService.HasRecovery(_s)) RecoveryService.OfferAfterNewPassword(this, _s);
        else if (wasOn) MessageBox.Show(this, "Your new password is saved. Your recovery file still works.", "Kaydence",
            MessageBoxButton.OK, MessageBoxImage.Information);
        UpdatePrivacyRows();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // I just leave it if Explorer won't open
        }
    }
}

// I ask for a new password twice, or check my current one
public sealed class PasswordDialog : Window
{
    private readonly PasswordBox _first = new();
    private readonly PasswordBox _second = new();
    private readonly TextBlock _error;
    private readonly AppSettings _settings;
    private readonly bool _create;

    public string? NewPassword { get; private set; }

    // I keep what I typed when checking my password too, so encryption can use it
    public string? EnteredPassword { get; private set; }

    public PasswordDialog(Window owner, AppSettings settings, bool create)
    {
        Owner = owner;
        _settings = settings;
        _create = create;
        Title = create ? "Set a password" : "Enter your password";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(UiKit.Text(create ? "Choose a password" : "Enter your current password", 16, "Brush.Text", FontWeights.SemiBold));
        if (create)
        {
            var hint = UiKit.Text("Your diary gets encrypted with this password. If you ever forget it, your recovery file is the only way back in, so save one somewhere safe.",
                12, "Brush.TextMuted", wrap: true);
            hint.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(hint);
        }

        stack.Children.Add(MakeLabel("Password"));
        stack.Children.Add(_first);
        if (create)
        {
            stack.Children.Add(MakeLabel("Type it again"));
            stack.Children.Add(_second);
        }

        _error = UiKit.Text("", 12, "Brush.Text", wrap: true);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x5A));
        _error.Margin = new Thickness(2, 8, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        stack.Children.Add(_error);

        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("SoftButton"), IsCancel = true, Height = 38 };
        var ok = new Button { Content = create ? "Save" : "OK", Style = (Style)FindResource("AccentButton"), IsDefault = true, Height = 38 };
        ok.Click += (_, _) => Confirm();
        Grid.SetColumn(ok, 2);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        Content = stack;
        Loaded += (_, _) => _first.Focus();
    }

    private static TextBlock MakeLabel(string text)
    {
        var label = UiKit.Text(text, 12, "Brush.TextMuted");
        label.Margin = new Thickness(2, 14, 0, 6);
        return label;
    }

    private void Confirm()
    {
        if (_create)
        {
            if (_first.Password.Length < 4) ShowError("Use at least 4 characters");
            else if (_first.Password != _second.Password) ShowError("The two passwords don't match");
            else
            {
                NewPassword = _first.Password;
                DialogResult = true;
            }
            return;
        }

        if (PasswordService.Verify(_first.Password, _settings.PasswordHash, _settings.PasswordSalt))
        {
            EnteredPassword = _first.Password;
            DialogResult = true;
        }
        else ShowError("That password isn't right");
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
