using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Kaydence.Controls;
using Kaydence.Models;
using Kaydence.Services;
using Microsoft.Win32;

namespace Kaydence;

// I'm the main window: the icon rail on the left and whichever screen I'm looking at
public partial class MainWindow : Window
{
    private readonly DiaryStore _store;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _idleTimer;
    private readonly TrayService _tray;
    private readonly HotkeyService _hotkey = new();
    private string _writingLook = "";
    private readonly bool _startedHidden;
    private DateOnly _day;
    private DayEntry _entry = new();
    private DateOnly _lastKnownToday;
    private DateTime _lastInput = DateTime.Now;
    private DateTime? _awaySince;
    private string _currentView = "Day";
    private bool _dirty;
    private bool _loading;
    private bool _locked;
    private bool _exiting;
    private bool _suspendSaving;
    private DateOnly _countersDay = DateOnly.FromDateTime(DateTime.Now);

    public MainWindow(DiaryStore store, AppSettings settings, bool startHidden)
    {
        _store = store;
        _settings = settings;
        _startedHidden = startHidden;
        InitializeComponent();

        // I save a moment after I stop typing so there's never a save button to remember
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _saveTimer.Tick += (_, _) => SaveNow();

        // I keep an eye on the clock so the today marker moves over at midnight
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _clockTimer.Tick += (_, _) =>
        {
            SideCalendar.Refresh();
            if (_countersDay != Today)
            {
                _countersDay = Today;
                CountersView.Render();
            }
            CheckReminder();
        };
        _clockTimer.Start();

        // I check every so often whether I've stepped away long enough to lock
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _idleTimer.Tick += (_, _) => CheckIdleLock();
        _idleTimer.Start();
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is KeyEventArgs or MouseButtonEventArgs or MouseWheelEventArgs or StylusDownEventArgs)
                _lastInput = DateTime.Now;
        };

        DayPage.Initialise(_store);
        CountersView.Initialise(_store);
        CheckInView.Initialise(_store, _settings);
        WeekScreen.Initialise(_store, _settings);
        MonthScreen.Initialise(_store, _settings);
        MemoriesScreen.Initialise(_store);
        InsightsScreen.Initialise(_store, _settings);
        SearchScreen.Initialise(_store);
        PrintScreen.Initialise(_store, _settings);

        WeekScreen.OpenDay += OpenDayFromScreen;
        MonthScreen.OpenDay += OpenDayFromScreen;
        MemoriesScreen.OpenDay += OpenDayFromScreen;
        InsightsScreen.OpenDay += OpenDayFromScreen;
        SearchScreen.OpenDay += OpenDayFromScreen;

        SideCalendar.SummaryProvider = _store.GetSummary;
        DayPage.Changed += MarkDirty;
        DayPage.ZoomChanged += () => ZoomText.Text = $"{Math.Round(DayPage.Zoom * 100)}%";
        CheckInView.Changed += MarkDirty;
        CheckInView.SettingsChanged += () => SettingsService.Save(_settings);
        CheckInView.OrderChanged += order =>
        {
            _settings.SectionOrder = order;
            SettingsService.Save(_settings);
        };
        CheckInView.HideRequested += key =>
        {
            if (!_settings.HiddenSections.Contains(key)) _settings.HiddenSections.Add(key);
            SettingsService.Save(_settings);
            Log.Info("CheckIn", $"Hid the {key} section from its menu");
            ApplySettings();
            ShowHint("Hidden. Switch it back on in Settings, Today panel");
        };
        ThemeService.ThemeChanged += UpdateIcons;

        _tray = new TrayService();
        _tray.OpenRequested += ShowFromTray;
        _tray.LockRequested += () =>
        {
            if (CanLock) Lock();
        };
        _tray.ExitRequested += ExitApp;
        _tray.NotificationClicked += () =>
        {
            ShowFromTray();
            if (_locked) return;
            SelectView("Day");
            GoTo(Today);
        };
        _hotkey.Pressed += ShowFromTray;
        EncryptionService.Migrating += OnMigrating;
        EncryptionService.Migrated += OnMigrated;

        BuildMoodKey();
        RestoreWindow();
        ApplySettings();

        _lastKnownToday = Today;
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        Loaded += (_, _) =>
        {
            ShowDay(Today);
            ApplyHotkey();
            if (_locked) FocusUnlockBox();
            else DayPage.FocusForWriting(true);
            Dispatcher.InvokeAsync(AfterFirstShow, DispatcherPriority.ApplicationIdle);
        };
        Activated += (_, _) =>
        {
            JumpToNewDayIfNeeded();
            CheckAwayLock();
        };
        Deactivated += (_, _) => _awaySince = DateTime.Now;
        StateChanged += Window_StateChanged;
        DpiChanged += (_, e) => Log.Info("Window", $"Moved to a monitor scaled at {e.NewDpi.DpiScaleX * 100:0}% (was {e.OldDpi.DpiScaleX * 100:0}%)");
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += Window_Closing;

        if (ShouldLockAtStart()) Lock();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private bool CanLock => _settings.PasswordEnabled && !string.IsNullOrEmpty(_settings.PasswordHash);

    // I apply everything from my settings, and run again whenever the settings window changes something
    public void ApplySettings()
    {
        ThemeService.ApplyAccent(_settings.Accent);
        DayPage.PageStyle = _settings.PageStyle;
        SyncPaperChoice();
        PageTextItem.DefaultFontSize = _settings.TextSize;
        PageTextItem.SpellCheckOn = _settings.SpellCheck;
        PageTextItem.WritingFont = new FontFamily(_settings.WritingFont);
        PageTextItem.Roomy = _settings.RoomySpacing;
        MonthCalendar.MondayFirst = _settings.WeekStartsMonday;
        RefreshCycle();
        SideCalendar.Refresh();
        MoodKeyPanel.Visibility = _settings.ShowMoodKey ? Visibility.Visible : Visibility.Collapsed;
        CountersView.Visibility = _settings.ShowCounters ? Visibility.Visible : Visibility.Collapsed;
        App.ShowTooltips = _settings.ShowTooltips;
        CheckInView.SetHiddenSections(_settings.HiddenSections);
        CheckInView.SetOrder(_settings.SectionOrder);
        CheckInView.ShowWeight();
        ApplyCheckInVisibility();
        LockButton.Visibility = CanLock ? Visibility.Visible : Visibility.Collapsed;
        _tray.CanLock = CanLock;
        _tray.Visible = _settings.ShowTrayIcon || _settings.CloseToTray || _settings.MinimiseToTray || _startedHidden;
        if (IsLoaded) ApplyHotkey();

        // I reload the page if I changed how my writing looks, so I can see it straight away
        var look = $"{_settings.WritingFont}|{_settings.RoomySpacing}|{_settings.TextSize}";
        if (IsLoaded && _writingLook.Length > 0 && look != _writingLook && !_locked) ShowDay(_day);
        _writingLook = look;
    }

    private void ApplyHotkey()
    {
        var ok = _hotkey.Apply(this, _settings.Hotkey);
        Log.Info("Hotkey", $"Shortcut {HotkeyService.Describe(_settings.Hotkey)}: {(ok ? "ready" : "already used by another app")}");
        if (!ok) ShowHint($"{HotkeyService.Describe(_settings.Hotkey)} is already used by another app");
    }

    // I run once the window is up: the welcome on my very first go, then a quiet update check
    private void AfterFirstShow()
    {
        Log.Info("App", $"Window shown. Locked: {_locked}, first run: {!_settings.FirstRunDone}");
        if (!_settings.FirstRunDone && !_locked && IsVisible)
        {
            Log.Info("App", "Showing the welcome tour");
            new WelcomeWindow(this, _settings, ApplySettings).ShowDialog();
            DayPage.FocusForWriting(true);
        }
        if (!_locked)
        {
            EncryptionService.FinishPending(this, _settings);
            OfferEncryption();
        }
        MaybeCheckForUpdates();
        CheckReminder();
    }

    // I stop saving while every file is being rewritten, then load the page fresh afterwards
    private void OnMigrating()
    {
        Log.Info("Crypto", "Pausing saves while files are rewritten");
        SaveNow();
        _saveTimer.Stop();
        _suspendSaving = true;
    }

    private void OnMigrated()
    {
        _suspendSaving = false;
        _dirty = false;
        Log.Info("Crypto", "Saves are back on, reloading the page");
        ShowDay(_day);
    }

    // I offer to encrypt a diary that got its password before encryption existed
    private void OfferEncryption()
    {
        if (!CanLock || EncryptionService.IsEncrypted(_settings) || _settings.EncryptionOfferDeclined == Today) return;
        var answer = MessageBox.Show(this,
            "Kaydence can now encrypt your diary with your password, so nobody can read it even by opening the files directly.\n\n" +
            "It takes a moment, and afterwards your password or recovery file is the only way in. Switch it on now?",
            "Encrypt your diary", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            _settings.EncryptionOfferDeclined = Today;
            SettingsService.Save(_settings);
            Log.Info("Crypto", "I said not now to encrypting");
            return;
        }
        var check = new PasswordDialog(this, _settings, false);
        if (check.ShowDialog() != true || check.EnteredPassword == null) return;
        EncryptionService.Enable(this, _settings, check.EnteredPassword);
    }

    private void MaybeCheckForUpdates()
    {
        if (IsVisible && !_locked) UpdateService.CheckInBackground(this, _settings);
    }

    // I nudge myself to write at my reminder time, once a day, and not if I've already written something
    private void CheckReminder()
    {
        if (!_settings.ReminderEnabled || _settings.LastReminder == Today) return;
        if (!TimeOnly.TryParseExact(_settings.ReminderTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return;
        if (TimeOnly.FromDateTime(DateTime.Now) < at) return;

        _settings.LastReminder = Today;
        SettingsService.Save(_settings);
        if (IsActive)
        {
            Log.Info("Reminder", "Reminder time, but Kaydence is already open in front of me");
            return;
        }
        var wroteToday = _store.GetSummary(Today) != null || (_day == Today && _dirty);
        if (_settings.ReminderOnlyIfEmpty && wroteToday)
        {
            Log.Info("Reminder", "Reminder time, but I've already written today");
            return;
        }
        Log.Info("Reminder", "Showing today's reminder");
        _tray.Notify("Time for your diary", "How was today? Click here to write a few words.");
    }

    private void ShowDay(DateOnly day)
    {
        Log.Info("Day", $"Opening {day:yyyy-MM-dd} (was {_day:yyyy-MM-dd})");
        SaveNow();
        if (_entry.Date.Length > 0) _store.TidyDay(_day, _entry);

        _loading = true;
        try
        {
            _day = day;
            _entry = _store.LoadDay(day);
            DayPage.Load(_entry, day, _store.LoadInk(day));
            CheckInView.Load(_entry.CheckIn, day);
            SideCalendar.SelectedDate = day;
            UpdateHeader();
        }
        finally
        {
            _loading = false;
        }
        _dirty = false;
        SaveStatus.Text = "";
    }

    private void GoTo(DateOnly day)
    {
        if (day != _day) ShowDay(day);
        DayPage.FocusForWriting(day == Today);
    }

    private void OpenDayFromScreen(DateOnly day)
    {
        SelectView("Day");
        GoTo(day);
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        SaveStatus.Text = "Saving";
        UpdateHeaderMood();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        if (!_dirty || _suspendSaving) return;
        try
        {
            _entry.Items = DayPage.CollectItems();
            _entry.Text = DayPage.GetPlainText();
            var ink = DayPage.GetInk();
            var hasContent = _entry.Items.Count > 0 || ink != null || _entry.CheckIn.HasAnyData();
            _entry.Updated = DateTime.Now;
            using (Log.Time("Save", $"Saving {_entry.Date}"))
            {
                _store.SaveDay(_entry, ink, hasContent);
            }
            _dirty = false;
            SaveStatus.Text = $"Saved {DateTime.Now:HH:mm}";
            RefreshCycle();
            SideCalendar.Refresh();
        }
        catch (Exception ex)
        {
            SaveStatus.Text = "Not saved";
            Log.Error("Save", $"Couldn't save {_entry.Date}", ex);
            MessageBox.Show(this, $"Kaydence couldn't save this day.\n\n{ex.Message}", "Kaydence",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // I work out my next period again so the calendar can show the guess
    private void RefreshCycle()
    {
        var on = !_settings.HiddenSections.Contains("Cycle");
        MonthCalendar.ShowCycle = on;
        MonthCalendar.PredictedPeriod = on ? CycleService.Analyse(_store, Today).Predicted : new HashSet<DateOnly>();
    }

    // I switch between Day, Week, Month, Memories, Insights, Search and Print
    private void ShowView(string name)
    {
        Log.Info("View", $"Switching to {name}");
        SaveNow();
        _currentView = name;
        DayScreen.Visibility = name == "Day" ? Visibility.Visible : Visibility.Collapsed;
        WeekScreen.Visibility = name == "Week" ? Visibility.Visible : Visibility.Collapsed;
        MonthScreen.Visibility = name == "Month" ? Visibility.Visible : Visibility.Collapsed;
        MemoriesScreen.Visibility = name == "Memories" ? Visibility.Visible : Visibility.Collapsed;
        InsightsScreen.Visibility = name == "Insights" ? Visibility.Visible : Visibility.Collapsed;
        SearchScreen.Visibility = name == "Search" ? Visibility.Visible : Visibility.Collapsed;
        PrintScreen.Visibility = name == "Print" ? Visibility.Visible : Visibility.Collapsed;

        switch (name)
        {
            case "Week":
                WeekScreen.ShowWeekOf(_day);
                break;
            case "Month":
                MonthScreen.ShowMonthOf(_day);
                break;
            case "Memories":
                MemoriesScreen.Refresh();
                break;
            case "Insights":
                InsightsScreen.Refresh();
                break;
            case "Search":
                SearchScreen.FocusSearch();
                break;
            case "Print":
                PrintScreen.Refresh();
                break;
            default:
                DayPage.FocusForWriting(false);
                break;
        }
    }

    private void SelectView(string name)
    {
        var button = name switch
        {
            "Week" => NavWeek,
            "Month" => NavMonth,
            "Memories" => NavMemories,
            "Insights" => NavInsights,
            "Search" => NavSearch,
            "Print" => NavPrint,
            _ => NavDay
        };
        if (button.IsChecked == true) ShowView(name);
        else button.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // I get called while the window is still being built, so I wait until the screens exist
        if (DayScreen == null || WeekScreen == null || PrintScreen == null) return;
        if (sender is ContentControl { Content: string name }) ShowView(name);
    }

    // I jump to the new day if I left the app open overnight on yesterday's page
    private void JumpToNewDayIfNeeded()
    {
        var today = Today;
        if (today == _lastKnownToday) return;
        var wasOnToday = _day == _lastKnownToday;
        _lastKnownToday = today;
        Log.Info("Day", "It's a new day since I last looked");
        if (wasOnToday && _currentView == "Day") GoTo(today);
        else SideCalendar.Refresh();
    }

    private void UpdateHeader()
    {
        var culture = CultureInfo.CurrentCulture;
        var dayName = _day.ToString("dddd", culture);
        DayName.Text = _day == Today ? $"Today, {dayName}"
            : _day == Today.AddDays(-1) ? $"Yesterday, {dayName}"
            : dayName;
        DayTitle.Text = _day.ToString("d MMMM yyyy", culture);
        if (!_locked) Title = $"Kaydence  |  {_day.ToString("dddd d MMMM yyyy", culture)}";
        UpdateHeaderMood();
    }

    private void UpdateHeaderMood()
    {
        var mood = _entry.CheckIn.Mood;
        HeaderMood.Visibility = mood.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (mood.HasValue) HeaderMood.Level = mood.Value;
    }

    private void BuildMoodKey()
    {
        foreach (var mood in Moods.All)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 8) };
            row.Children.Add(new MoodFace { Level = mood.Value, Width = 16, Height = 16 });
            var label = new TextBlock { Text = mood.Name, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            row.Children.Add(label);
            MoodKey.Children.Add(row);
        }
    }

    private void UpdateIcons()
    {
        ThemeButton.Content = ThemeService.IsDark ? "\uE706" : "\uE708";
        CheckInToggle.Content = _settings.CheckInOpen ? "\uE89F" : "\uE8A0";
    }

    private void ApplyCheckInVisibility()
    {
        CheckInHost.Visibility = _settings.CheckInOpen ? Visibility.Visible : Visibility.Collapsed;
        CheckInColumn.Width = _settings.CheckInOpen ? new GridLength(360) : new GridLength(0);
        UpdateIcons();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_locked) return;
        var modifiers = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = modifiers == ModifierKeys.Control;
        var ctrlShift = modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

        if (ctrl && key == Key.F)
        {
            SelectView("Search");
            e.Handled = true;
            return;
        }
        if (ctrl && key == Key.P)
        {
            SelectView("Print");
            e.Handled = true;
            return;
        }
        if (ctrl && key == Key.L && CanLock)
        {
            Lock();
            e.Handled = true;
            return;
        }
        if (key == Key.F1 && modifiers == ModifierKeys.None)
        {
            GuideService.Open();
            e.Handled = true;
            return;
        }
        if (ctrl && key == Key.T)
        {
            SelectView("Day");
            GoTo(Today);
            e.Handled = true;
            return;
        }

        if (_currentView != "Day") return;
        var inCheckIn = CheckInView.IsKeyboardFocusWithin;

        if ((modifiers == ModifierKeys.Alt && key == Key.Left) || (ctrl && key == Key.PageUp))
        {
            GoTo(_day.AddDays(-1));
            e.Handled = true;
        }
        else if ((modifiers == ModifierKeys.Alt && key == Key.Right) || (ctrl && key == Key.PageDown))
        {
            GoTo(_day.AddDays(1));
            e.Handled = true;
        }
        else if (inCheckIn)
        {
            // I leave normal typing keys alone while I'm filling in my check-in
        }
        else if (ctrl && key is Key.OemPlus or Key.Add)
        {
            DayPage.ZoomBy(1.1);
            e.Handled = true;
        }
        else if (ctrl && key is Key.OemMinus or Key.Subtract)
        {
            DayPage.ZoomBy(1 / 1.1);
            e.Handled = true;
        }
        else if (ctrl && key is Key.D0 or Key.NumPad0)
        {
            DayPage.SetZoom(1, null);
            e.Handled = true;
        }
        else if (ctrl && key == Key.V)
        {
            if (DayPage.TryPaste()) e.Handled = true;
        }
        else if (ctrl && key == Key.Z)
        {
            DayPage.Undo();
            e.Handled = true;
        }
        else if ((ctrl && key == Key.Y) || (ctrlShift && key == Key.Z))
        {
            DayPage.Redo();
            e.Handled = true;
        }
        else if ((key == Key.Delete || key == Key.Back) && modifiers == ModifierKeys.None)
        {
            if (DayPage.DeleteSelection(false)) e.Handled = true;
        }
        else if (key == Key.Escape && ToolType.IsChecked != true)
        {
            ToolType.IsChecked = true;
            e.Handled = true;
        }
    }

    private void SideCalendar_DateSelected(DateOnly date) => GoTo(date);

    // I right clicked a day on the calendar and asked to delete it
    private void SideCalendar_DeleteRequested(DateOnly day)
    {
        var name = day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
        var answer = MessageBox.Show(this,
            $"Delete everything you wrote on {name}?\n\nIt gets moved into the \"deleted\" folder inside your diary folder, so it can still be rescued by hand.",
            "Delete this day", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (day == _day)
        {
            _saveTimer.Stop();
            _dirty = false;
        }
        try
        {
            _store.DeleteDay(day);
        }
        catch (Exception ex)
        {
            Log.Error("Day", $"Couldn't delete {day:yyyy-MM-dd}", ex);
            MessageBox.Show(this, $"Kaydence couldn't delete that day.\n\n{ex.Message}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (day == _day)
        {
            _entry = new DayEntry();
            ShowDay(day);
        }
        SideCalendar.Refresh();
    }

    private void Today_Click(object sender, RoutedEventArgs e) => GoTo(Today);

    private void Website_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Links.Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void PrevDay_Click(object sender, RoutedEventArgs e) => GoTo(_day.AddDays(-1));

    private void NextDay_Click(object sender, RoutedEventArgs e) => GoTo(_day.AddDays(1));

    private void Undo_Click(object sender, RoutedEventArgs e) => DayPage.Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => DayPage.Redo();

    private void DeleteSelection_Click(object sender, RoutedEventArgs e) => DayPage.DeleteSelection(true);

    private void ToggleCheckIn_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckInOpen = !_settings.CheckInOpen;
        ApplyCheckInVisibility();
        SettingsService.Save(_settings);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = ThemeService.IsDark ? "Light" : "Dark";
        ThemeService.Apply(_settings.Theme);
        SettingsService.Save(_settings);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        Log.Info("Settings", "Opening settings");
        var printEverything = false;
        var window = new SettingsWindow(_settings, _store, ApplySettings, RestoreBackup, () => printEverything = true) { Owner = this };
        window.ShowDialog();
        Log.Info("Settings", "Closed settings");
        if (_exiting) return;
        ApplySettings();
        if (!printEverything) return;
        SelectView("Print");
        PrintScreen.ShowEverything();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.Root}\"") { UseShellExecute = true });
    }

    private void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add pictures to this day",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        Log.Info("Pictures", $"Picked {dialog.FileNames.Length} picture files: {string.Join(", ", dialog.FileNames.Select(f => Path.GetExtension(f)))}");
        DayPage.AddImageFiles(dialog.FileNames);
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        // I get called while the window is still being built, so I wait until the page exists
        if (DayPage == null || PenColors == null || HighlightColors == null) return;
        if (sender is not FrameworkElement { Tag: string name }) return;

        PageTool tool;
        if (name.StartsWith("Shape:", StringComparison.Ordinal) && Enum.TryParse<ShapeKind>(name[6..], out var shape))
        {
            DayPage.Shape = shape;
            tool = PageTool.Shape;
        }
        else if (!Enum.TryParse(name, out tool))
        {
            return;
        }

        DayPage.Tool = tool;
        PenColors.Visibility = tool is PageTool.Pen or PageTool.Shape ? Visibility.Visible : Visibility.Collapsed;
        HighlightColors.Visibility = tool == PageTool.Highlighter ? Visibility.Visible : Visibility.Collapsed;
    }

    // I switch the ribbon over, and go back to typing when I leave the Draw tab so a click always writes
    private void RibbonTab_Checked(object sender, RoutedEventArgs e)
    {
        if (HomeBar == null || InsertBar == null || DrawBar == null || ViewBar == null) return;
        if (sender is not FrameworkElement { Tag: string tab }) return;

        HomeBar.Visibility = tab == "Home" ? Visibility.Visible : Visibility.Collapsed;
        InsertBar.Visibility = tab == "Insert" ? Visibility.Visible : Visibility.Collapsed;
        DrawBar.Visibility = tab == "Draw" ? Visibility.Visible : Visibility.Collapsed;
        ViewBar.Visibility = tab == "View" ? Visibility.Visible : Visibility.Collapsed;
        if (tab is "Home" or "Insert" && ToolType.IsChecked != true) ToolType.IsChecked = true;
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action }) ApplyFormat(action, null);
    }

    private void TextColour_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        var colour = control.Tag as string == "auto" ? (Color?)null : (control.Background as SolidColorBrush)?.Color;
        ApplyFormat("colour", colour);
    }

    private void TextHighlight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Control control) return;
        // I make text highlights see through so the writing stays readable in light and dark
        Color? colour = control.Tag as string == "none" || control.Background is not SolidColorBrush brush
            ? null
            : Color.FromArgb(0x99, brush.Color.R, brush.Color.G, brush.Color.B);
        ApplyFormat("highlight", colour);
    }

    private void ClearFormat_Click(object sender, RoutedEventArgs e) => ApplyFormat("clear", null);

    private void ApplyFormat(string action, Color? colour)
    {
        if (!DayPage.Format(action, colour)) ShowHint("Click into some writing first");
    }

    // I borrow the save status spot for a quick hint, it gets replaced on the next save
    private void ShowHint(string text) => SaveStatus.Text = text;

    private void AddTextBox_Click(object sender, RoutedEventArgs e) => DayPage.AddTextBox();

    private void AddChecklist_Click(object sender, RoutedEventArgs e) => DayPage.AddChecklist();

    private void TimeStamp_Click(object sender, RoutedEventArgs e) => DayPage.InsertTimeStamp();

    private void AddVoice_Click(object sender, RoutedEventArgs e) => DayPage.AddVoiceNote();

    private void AddSticky_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex }) DayPage.AddSticky(hex);
    }

    private void ImageEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action } && !DayPage.EditSelectedImage(action))
            ShowHint("Click a picture first");
    }

    private void PenSize_Checked(object sender, RoutedEventArgs e)
    {
        if (DayPage != null && sender is FrameworkElement { Tag: string size }
            && double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
        {
            DayPage.PenWidth = width;
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => DayPage.ZoomBy(1.1);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => DayPage.ZoomBy(1 / 1.1);

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => DayPage.FitToView();

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => DayPage.SetZoom(1, null);

    private bool _syncingPaper;

    private void SyncPaperChoice()
    {
        _syncingPaper = true;
        var choice = _settings.PageStyle switch
        {
            "Lined" => PaperLined,
            "Grid" => PaperGrid,
            "Dots" => PaperDots,
            _ => PaperPlain
        };
        choice.IsChecked = true;
        _syncingPaper = false;
    }

    private void Paper_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingPaper || _settings == null || DayPage == null || sender is not FrameworkElement { Tag: string style }) return;
        _settings.PageStyle = style;
        DayPage.PageStyle = style;
        SettingsService.Save(_settings);
    }

    private void PenColor_Checked(object sender, RoutedEventArgs e)
    {
        if (DayPage != null && sender is Control { Background: SolidColorBrush brush }) DayPage.PenColor = brush.Color;
    }

    private void HighlightColor_Checked(object sender, RoutedEventArgs e)
    {
        if (DayPage != null && sender is Control { Background: SolidColorBrush brush }) DayPage.HighlightColor = brush.Color;
    }

    // I decide at start up whether to ask for my password first
    private bool ShouldLockAtStart()
    {
        if (!CanLock || App.UnlockedAtStart) return false;
        if (_settings.LockMode != "AfterDays" || _settings.LastUsed == null) return true;
        return (DateTime.Now - _settings.LastUsed.Value).TotalDays >= Math.Max(1, _settings.LockAfterDays);
    }

    private void Lock()
    {
        if (!CanLock || _locked) return;
        Log.Info("Lock", "Locking");
        SaveNow();
        _locked = true;
        _settings.LastUsed = _lastInput;
        SettingsService.Save(_settings);
        Shell.Visibility = Visibility.Hidden;
        LockOverlay.Visibility = Visibility.Visible;
        UnlockBox.Password = "";
        UnlockError.Visibility = Visibility.Collapsed;
        RecoverButton.Visibility = RecoveryService.HasRecovery(_settings) ? Visibility.Visible : Visibility.Collapsed;
        Title = "Kaydence";
        FocusUnlockBox();
    }

    private void FocusUnlockBox() => Dispatcher.InvokeAsync(() =>
    {
        UnlockBox.Focus();
        Keyboard.Focus(UnlockBox);
    }, DispatcherPriority.Input);

    private void Unlock()
    {
        if (!PasswordService.Verify(UnlockBox.Password, _settings.PasswordHash, _settings.PasswordSalt))
        {
            Log.Info("Lock", "Wrong password on the lock screen");
            UnlockError.Text = "That password isn't right, try again";
            UnlockError.Visibility = Visibility.Visible;
            UnlockBox.SelectAll();
            return;
        }
        CompleteUnlock();
    }

    // I use my recovery file to prove it's me, then pick a brand new password
    private void Recover_Click(object sender, RoutedEventArgs e)
    {
        if (!RecoveryService.TryUse(this, _settings, out var problem, out _))
        {
            if (problem.Length > 0)
            {
                UnlockError.Text = problem;
                UnlockError.Visibility = Visibility.Visible;
            }
            return;
        }

        var dialog = new PasswordDialog(this, _settings, true);
        if (dialog.ShowDialog() != true || dialog.NewPassword == null) return;
        EncryptionService.PasswordChanged(_settings, dialog.NewPassword);
        MessageBox.Show(this, "Your new password is set. Your recovery file still works, so keep it somewhere safe.",
            "Kaydence", MessageBoxButton.OK, MessageBoxImage.Information);
        CompleteUnlock();
    }

    private void CompleteUnlock()
    {
        Log.Info("Lock", "Unlocked");
        _locked = false;
        UnlockBox.Password = "";
        LockOverlay.Visibility = Visibility.Collapsed;
        Shell.Visibility = Visibility.Visible;
        _lastInput = DateTime.Now;
        _awaySince = null;
        _settings.LastUsed = DateTime.Now;
        SettingsService.Save(_settings);
        UpdateHeader();
        if (_currentView == "Day") DayPage.FocusForWriting(_day == Today);
        Dispatcher.InvokeAsync(() =>
        {
            if (_locked) return;
            EncryptionService.FinishPending(this, _settings);
            OfferEncryption();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => Unlock();

    private void UnlockBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Unlock();
        e.Handled = true;
    }

    private void Lock_Click(object sender, RoutedEventArgs e) => Lock();

    private void CheckIdleLock()
    {
        if (_locked || !CanLock || _settings.IdleLockMinutes <= 0) return;
        if ((DateTime.Now - _lastInput).TotalMinutes < _settings.IdleLockMinutes) return;
        Log.Info("Lock", $"No input for {_settings.IdleLockMinutes} minutes");
        Lock();
    }

    // I lock when I come back after being away longer than my settings allow
    private void CheckAwayLock()
    {
        var away = _awaySince;
        _awaySince = null;
        if (_locked || !CanLock || _settings.LockMode != "AfterDays" || away == null) return;
        if ((DateTime.Now - away.Value).TotalDays < Math.Max(1, _settings.LockAfterDays)) return;
        Log.Info("Lock", "Away for longer than my lock setting allows");
        Lock();
    }

    public void ShowFromTray()
    {
        Log.Info("Window", "Bringing the window to the front");
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _settings.Maximized ? WindowState.Maximized : WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        MaybeCheckForUpdates();
    }

    private void HideToTray()
    {
        Log.Info("Window", "Tucking away by the clock");
        SaveNow();
        _tray.Visible = true;
        _awaySince = DateTime.Now;
        _settings.LastUsed = _lastInput;
        SettingsService.Save(_settings);
        Hide();
        if (_settings.LockWhenHidden) Lock();
    }

    private void ExitApp()
    {
        Log.Info("Window", "Exit chosen from the clock icon");
        _exiting = true;
        Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimiseToTray) HideToTray();
    }

    private void RestoreWindow()
    {
        if (_settings.Width is double width and > 400 && _settings.Height is double height and > 300)
        {
            Width = width;
            Height = height;
            if (_settings.Left is { } left && _settings.Top is { } top && IsOnScreen(left, top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 20
        && top >= SystemParameters.VirtualScreenTop - 20
        && left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
        && top + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    // I restore a backup zip, then restart so everything loads fresh from the restored files
    public void RestoreBackup(string zipPath)
    {
        SaveNow();
        _suspendSaving = true;
        Log.Info("Backup", $"Restoring {Path.GetFileName(zipPath)}");
        try
        {
            BackupService.Restore(zipPath, _store.Root, _settings.BackupFolderOrDefault);
        }
        catch (Exception ex)
        {
            Log.Error("Backup", "Restore failed, nothing was changed", ex);
            _suspendSaving = false;
            MessageBox.Show(this, $"That backup couldn't be restored, your diary hasn't been changed.\n\n{ex.Message}", "Kaydence",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(this, "Your diary has been restored from the backup. Kaydence will now restart.", "Kaydence",
            MessageBoxButton.OK, MessageBoxImage.Information);
        _exiting = true;
        ((App)Application.Current).Restart();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        Log.Info("Window", $"Closing (exiting: {_exiting}, close to tray: {_settings.CloseToTray})");
        SaveNow();
        if (!_exiting && _settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_entry.Date.Length > 0 && !_suspendSaving) _store.TidyDay(_day, _entry);
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.Left = bounds.Left;
            _settings.Top = bounds.Top;
            _settings.Width = bounds.Width;
            _settings.Height = bounds.Height;
            _settings.Maximized = WindowState == WindowState.Maximized;
        }
        if (!_locked) _settings.LastUsed = DateTime.Now;
        SettingsService.Save(_settings);
        _hotkey.Dispose();
        EncryptionService.Migrating -= OnMigrating;
        EncryptionService.Migrated -= OnMigrated;
        _tray.Dispose();
    }
}
