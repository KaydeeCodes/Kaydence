using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Kaydence.Models;
using Kaydence.Services;

namespace Kaydence.Controls;

// I'm the optional check-in sections on the right, everything folds away until I need it
public partial class CheckInPanel : UserControl
{
    // I use these names in settings to hide sections I don't need
    public static readonly (string Key, string Label)[] Sections =
    {
        ("Feelings", "Feelings"), ("Note", "Note"), ("Depression", "Depression"), ("Anxiety", "Anxiety"), ("Medications", "Medications taken"), ("Transition", "Transition"),
        ("Symptoms", "Symptoms and pain"), ("Cycle", "Period and cycle"), ("Sleep", "Sleep"), ("Fitness", "Fitness"), ("BloodPressure", "Blood pressure"),
        ("Weight", "Weight"), ("Water", "Water"), ("Caffeine", "Caffeine"), ("Smoking", "Smoking"), ("Alcohol", "Alcohol"), ("Tasks", "Tasks"), ("Struggles", "Struggles"), ("Wins", "Wins and good things")
    };

    private static readonly (string Value, string Label)[] WeightUnits =
    {
        ("kg", "kg"), ("st", "stone and pounds"), ("lb", "pounds")
    };

    private readonly ObservableCollection<MedRow> _medRows = new();
    private AppSettings? _settings;
    private bool _weightLoading;
    private DiaryStore? _store;
    private CheckIn? _checkIn;
    private DateOnly _day;
    private bool _loading;
    private List<TaskItem> _carryTasks = new();
    private readonly BodyMap _painMap = new(editable: true);

    public event Action? Changed;
    public event Action? SettingsChanged;
    public event Action<List<string>>? OrderChanged;
    public event Action<string>? HideRequested;

    public CheckInPanel()
    {
        InitializeComponent();
        MedList.ItemsSource = _medRows;
        PainMapHost.Content = _painMap;
        _painMap.AreasChanged += areas =>
        {
            if (_checkIn == null) return;
            _checkIn.Symptoms.Areas = areas;
            Log.Debug("CheckIn", $"Pain areas marked: {areas.Count}");
        };
        Mood.ValueChanged += value =>
        {
            if (_checkIn != null) _checkIn.Mood = value;
        };
        BuildChips(PainChips, Enumerable.Range(0, 11).Select(i => i.ToString(CultureInfo.InvariantCulture)), value =>
        {
            if (_checkIn != null) _checkIn.Symptoms.Pain = value == null ? null : int.Parse(value, CultureInfo.InvariantCulture);
        });
        var scale = Enumerable.Range(1, 10).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        BuildChips(DepressionChips, scale, value =>
        {
            if (_checkIn != null) _checkIn.Depression.Level = value == null ? null : int.Parse(value, CultureInfo.InvariantCulture);
        });
        BuildChips(AnxietyChips, scale, value =>
        {
            if (_checkIn != null) _checkIn.Anxiety.Level = value == null ? null : int.Parse(value, CultureInfo.InvariantCulture);
        });
        BuildChips(FlowChips, CycleInfo.Flows, value =>
        {
            if (_checkIn != null) _checkIn.Cycle.Flow = value;
        });
        foreach (var feeling in CycleInfo.Feelings)
        {
            var chip = new ToggleButton { Content = feeling, Tag = feeling, Style = (Style)Application.Current.FindResource("Chip") };
            chip.Click += (_, _) =>
            {
                if (_checkIn == null) return;
                var picked = CycleFeelingChips.Children.OfType<ToggleButton>().Where(t => t.IsChecked == true).Select(t => (string)t.Tag);
                var joined = string.Join(", ", picked);
                _checkIn.Cycle.Symptoms = joined.Length > 0 ? joined : null;
            };
            CycleFeelingChips.Children.Add(chip);
        }
    }

    public void Initialise(DiaryStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        BuildSectionMenus();
        foreach (var (value, label) in WeightUnits)
        {
            var chip = new ToggleButton { Content = label, Tag = value, Style = (Style)Application.Current.FindResource("Chip") };
            chip.Click += (_, _) =>
            {
                if (_settings == null) return;
                _settings.WeightUnit = value;
                SettingsChanged?.Invoke();
                ShowWeight();
                RefreshSummaries();
            };
            WeightUnitChips.Children.Add(chip);
        }
    }

    private Dictionary<string, FrameworkElement>? _map;

    // I look up each section's card by the name settings uses for it
    private Dictionary<string, FrameworkElement> Map => _map ??= new Dictionary<string, FrameworkElement>
    {
        ["Feelings"] = FeelingsSection, ["Note"] = NoteSection, ["Depression"] = DepressionSection, ["Anxiety"] = AnxietySection, ["Medications"] = MedsSection,
        ["Transition"] = TransitionSection, ["Symptoms"] = SymptomsSection, ["Cycle"] = CycleSection, ["Sleep"] = SleepSection,
        ["Fitness"] = FitnessSection, ["BloodPressure"] = BpSection, ["Weight"] = WeightSection,
        ["Water"] = WaterSection, ["Caffeine"] = CaffeineSection, ["Smoking"] = SmokingSection, ["Alcohol"] = AlcoholSection,
        ["Tasks"] = TasksSection, ["Struggles"] = StrugglesSection, ["Wins"] = WinsSection
    };

    public void SetHiddenSections(ICollection<string> hidden)
    {
        foreach (var (key, element) in Map)
            element.Visibility = hidden.Contains(key) ? Visibility.Collapsed : Visibility.Visible;
    }

    // I turn a saved order into a full one, keeping any sections the saved list doesn't mention next to where they usually sit
    public static List<string> Ordered(IEnumerable<string>? saved)
    {
        var known = Sections.Select(s => s.Key).ToList();
        var order = (saved ?? Enumerable.Empty<string>()).Where(known.Contains).Distinct().ToList();
        for (var i = 0; i < known.Count; i++)
        {
            if (order.Contains(known[i])) continue;
            // I slot a section that's new since the order was saved in just after its usual neighbour
            var after = i == 0 ? -1 : order.IndexOf(known[i - 1]);
            order.Insert(after + 1, known[i]);
        }
        return order;
    }

    // I lay the sections out in the order chosen in settings
    public void SetOrder(IEnumerable<string>? saved)
    {
        var order = Ordered(saved);
        var current = SectionsHost.Children.OfType<FrameworkElement>().ToList();
        var wanted = order.Select(k => Map[k]).ToList();
        if (current.SequenceEqual(wanted)) return;
        SectionsHost.Children.Clear();
        foreach (var element in wanted) SectionsHost.Children.Add(element);
        Log.Debug("CheckIn", $"Section order: {string.Join(", ", order)}");
    }

    // I give every section a right click menu to move it up or down, or hide it
    private void BuildSectionMenus()
    {
        foreach (var (key, element) in Map)
        {
            var menu = new ContextMenu();
            void Item(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            Item("Move to the top", () => Move(key, int.MinValue));
            Item("Move up", () => Move(key, -1));
            Item("Move down", () => Move(key, 1));
            Item("Move to the bottom", () => Move(key, int.MaxValue));
            menu.Items.Add(new Separator());
            Item("Hide this section", () => HideRequested?.Invoke(key));
            menu.Opened += (_, _) =>
            {
                var visible = VisibleOrder();
                var at = visible.IndexOf(key);
                ((MenuItem)menu.Items[0]).IsEnabled = at > 0;
                ((MenuItem)menu.Items[1]).IsEnabled = at > 0;
                ((MenuItem)menu.Items[2]).IsEnabled = at >= 0 && at < visible.Count - 1;
                ((MenuItem)menu.Items[3]).IsEnabled = at >= 0 && at < visible.Count - 1;
            };
            element.ContextMenu = menu;
        }
    }

    private List<string> VisibleOrder() => SectionsHost.Children.OfType<FrameworkElement>()
        .Where(e => e.Visibility == Visibility.Visible)
        .Select(e => Map.First(p => ReferenceEquals(p.Value, e)).Key)
        .ToList();

    // I move a section past the visible ones only, so hidden sections never make a move look like it did nothing
    private void Move(string key, int step)
    {
        var all = SectionsHost.Children.OfType<FrameworkElement>().Select(e => Map.First(p => ReferenceEquals(p.Value, e)).Key).ToList();
        var visible = VisibleOrder();
        var at = visible.IndexOf(key);
        if (at < 0) return;
        var target = step == int.MinValue ? 0 : step == int.MaxValue ? visible.Count - 1 : Math.Clamp(at + step, 0, visible.Count - 1);
        if (target == at) return;

        var neighbour = visible[target];
        all.Remove(key);
        var index = all.IndexOf(neighbour);
        all.Insert(target > at ? index + 1 : index, key);
        SetOrder(all);
        Log.Info("CheckIn", $"Moved {key} {(target > at ? "down" : "up")}");
        OrderChanged?.Invoke(all);
    }

    // I show my weight in whichever unit I picked, but always store it in kg underneath
    public void ShowWeight()
    {
        var unit = _settings?.WeightUnit ?? "kg";
        foreach (var chip in WeightUnitChips.Children.OfType<ToggleButton>())
            chip.IsChecked = (string)chip.Tag == unit;

        _weightLoading = true;
        try
        {
            var kg = _checkIn?.WeightKg;
            WeightSingle.Visibility = unit == "st" ? Visibility.Collapsed : Visibility.Visible;
            WeightStone.Visibility = unit == "st" ? Visibility.Visible : Visibility.Collapsed;
            WeightUnitLabel.Text = unit == "lb" ? "lb" : "kg";

            if (unit == "st")
            {
                WeightBox.Text = "";
                if (kg is double stoneKg)
                {
                    var (stone, pounds) = Units.ToStone(stoneKg);
                    StoneBox.Text = stone.ToString(CultureInfo.CurrentCulture);
                    PoundsBox.Text = pounds.ToString("0.#", CultureInfo.CurrentCulture);
                }
                else
                {
                    StoneBox.Text = "";
                    PoundsBox.Text = "";
                }
            }
            else
            {
                StoneBox.Text = "";
                PoundsBox.Text = "";
                WeightBox.Text = kg is double plainKg
                    ? (unit == "lb" ? plainKg * Units.PoundsPerKg : plainKg).ToString("0.#", CultureInfo.CurrentCulture)
                    : "";
            }
        }
        finally
        {
            _weightLoading = false;
        }
    }

    private void Weight_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_weightLoading || _checkIn == null) return;
        var unit = _settings?.WeightUnit ?? "kg";

        if (unit == "st")
        {
            var stoneEmpty = string.IsNullOrWhiteSpace(StoneBox.Text);
            var poundsEmpty = string.IsNullOrWhiteSpace(PoundsBox.Text);
            var stoneOk = Units.TryParse(StoneBox.Text, out var stone);
            var poundsOk = Units.TryParse(PoundsBox.Text, out var pounds);
            if (stoneEmpty && poundsEmpty) _checkIn.WeightKg = null;
            else if ((stoneOk || stoneEmpty) && (poundsOk || poundsEmpty))
                _checkIn.WeightKg = Math.Round(Units.FromStone(stoneOk ? stone : 0, poundsOk ? pounds : 0), 2);
            return;
        }

        if (string.IsNullOrWhiteSpace(WeightBox.Text)) _checkIn.WeightKg = null;
        else if (Units.TryParse(WeightBox.Text, out var value))
            _checkIn.WeightKg = Math.Round(unit == "lb" ? Units.FromPounds(value) : value, 2);
    }

    public void Load(CheckIn checkIn, DateOnly day)
    {
        _loading = true;
        try
        {
            if (_checkIn != null) _checkIn.Changed -= OnCheckInChanged;
            _checkIn = checkIn;
            _day = day;
            checkIn.StartTracking();
            checkIn.Changed += OnCheckInChanged;
            DataContext = checkIn;

            Mood.Value = checkIn.Mood;
            SetChips(PainChips, checkIn.Symptoms.Pain?.ToString(CultureInfo.InvariantCulture));
            SetChips(DepressionChips, checkIn.Depression.Level?.ToString(CultureInfo.InvariantCulture));
            SetChips(AnxietyChips, checkIn.Anxiety.Level?.ToString(CultureInfo.InvariantCulture));
            _painMap.Show(checkIn.Symptoms.Areas);
            SetChips(FlowChips, checkIn.Cycle.Flow);
            var feelings = (checkIn.Cycle.Symptoms ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var chip in CycleFeelingChips.Children.OfType<ToggleButton>()) chip.IsChecked = feelings.Contains((string)chip.Tag);
            NewTask.Text = "";
            NewMedName.Text = "";
            NewMedDose.Text = "";
            LoadMeds();
            ShowWeight();
            UpdateCarryTasks();
            RefreshSummaries();
        }
        finally
        {
            _loading = false;
        }
    }

    private bool IsToday => _day == DateOnly.FromDateTime(DateTime.Now);

    private string? NowTime => IsToday ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) : null;

    private void OnCheckInChanged()
    {
        RefreshSummaries();
        if (!_loading) Changed?.Invoke();
    }

    private void LoadMeds()
    {
        _medRows.Clear();
        if (_store == null || _checkIn == null) return;

        var saved = _store.LoadMedications();
        var taken = _checkIn.Medications;

        foreach (var med in saved.Where(m => m.Active || taken.Any(t => t.MedId == m.Id)))
            AddMedRow(med.Id, med.Name, med.Dose, taken.Any(t => t.MedId == med.Id));

        // I still show anything I took that day even if I've since removed it from my list
        foreach (var dose in taken.Where(t => saved.All(m => m.Id != t.MedId)))
            AddMedRow(dose.MedId, dose.Name, dose.Dose, true);

        UpdateNoMedsHint();
    }

    private MedRow AddMedRow(string id, string name, string? dose, bool taken)
    {
        var row = new MedRow(id, name, dose, taken);
        row.PropertyChanged += (_, _) => OnMedToggled(row);
        _medRows.Add(row);
        return row;
    }

    private void OnMedToggled(MedRow row)
    {
        if (_checkIn == null) return;
        var list = _checkIn.Medications;
        var existing = list.FirstOrDefault(m => m.MedId == row.Id);
        if (row.Taken && existing == null)
            list.Add(new MedDose { MedId = row.Id, Name = row.Name, Dose = row.Dose, Time = NowTime });
        else if (!row.Taken && existing != null)
            list.Remove(existing);
    }

    private void AddMed_Click(object sender, RoutedEventArgs e) => AddMedicationFromBoxes();

    private void NewMed_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddMedicationFromBoxes();
        e.Handled = true;
    }

    private void AddMedicationFromBoxes()
    {
        var name = NewMedName.Text.Trim();
        if (name.Length == 0 || _store == null) return;
        var dose = string.IsNullOrWhiteSpace(NewMedDose.Text) ? null : NewMedDose.Text.Trim();

        var medication = new Medication { Name = name, Dose = dose };
        var saved = _store.LoadMedications();
        saved.Add(medication);
        _store.SaveMedications(saved);

        // I tick it straight away because I'm usually adding something I've just taken
        var row = AddMedRow(medication.Id, medication.Name, medication.Dose, true);
        OnMedToggled(row);

        NewMedName.Text = "";
        NewMedDose.Text = "";
        UpdateNoMedsHint();
        NewMedName.Focus();
    }

    private void RemoveMed_Click(object sender, RoutedEventArgs e)
    {
        if (_store == null || sender is not FrameworkElement { DataContext: MedRow row }) return;

        var answer = MessageBox.Show(Window.GetWindow(this)!,
            $"Stop showing {row.Name} in your daily list?\n\nDays where you already ticked it keep their record.",
            "Kaydence", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var saved = _store.LoadMedications();
        var medication = saved.FirstOrDefault(m => m.Id == row.Id);
        if (medication != null)
        {
            medication.Active = false;
            _store.SaveMedications(saved);
        }
        if (!row.Taken) _medRows.Remove(row);
        UpdateNoMedsHint();
    }

    private void UpdateNoMedsHint() =>
        NoMedsHint.Visibility = _medRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void AddBp_Click(object sender, RoutedEventArgs e) =>
        _checkIn?.BloodPressure.Add(new BpReading { Time = NowTime });

    private void RemoveBp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BpReading reading }) _checkIn?.BloodPressure.Remove(reading);
    }

    private void NewTask_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _checkIn == null) return;
        var text = NewTask.Text.Trim();
        if (text.Length > 0) _checkIn.Tasks.Add(new TaskItem { Text = text });
        NewTask.Text = "";
        e.Handled = true;
    }

    private void RemoveTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItem task }) _checkIn?.Tasks.Remove(task);
    }

    private void UpdateCarryTasks()
    {
        _carryTasks = new List<TaskItem>();
        CarryTasksButton.Visibility = Visibility.Collapsed;
        if (_store == null || _checkIn == null) return;

        var found = _store.FindUnfinishedTasks(_day);
        if (found == null) return;

        var (date, tasks) = found.Value;
        _carryTasks = tasks.Where(t => _checkIn.Tasks.All(existing => existing.Text != t.Text)).ToList();
        if (_carryTasks.Count == 0) return;

        var noun = _carryTasks.Count == 1 ? "task" : "tasks";
        CarryTasksButton.Content = $"Bring over {_carryTasks.Count} unfinished {noun} from {date.ToString("dddd d MMM", CultureInfo.CurrentCulture)}";
        CarryTasksButton.Visibility = Visibility.Visible;
    }

    private void CarryTasks_Click(object sender, RoutedEventArgs e)
    {
        if (_checkIn == null) return;
        foreach (var task in _carryTasks) _checkIn.Tasks.Add(new TaskItem { Text = task.Text });
        _carryTasks.Clear();
        CarryTasksButton.Visibility = Visibility.Collapsed;
    }

    // I nudge my water and caffeine counts up or down with the plus and minus buttons
    private void Counter_Click(object sender, RoutedEventArgs e)
    {
        if (_checkIn == null || sender is not FrameworkElement { Tag: string tag }) return;
        var parts = tag.Split(':');
        var step = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var habits = _checkIn.Habits;
        if (parts[0] == "water") habits.Water = Math.Max(0, (habits.Water ?? 0) + step);
        else habits.Caffeine = Math.Max(0, (habits.Caffeine ?? 0) + step);
    }

    private static void BuildChips(Panel panel, IEnumerable<string> values, Action<string?> onChosen)
    {
        foreach (var value in values)
        {
            var chip = new ToggleButton
            {
                Content = value,
                Tag = value,
                Style = (Style)Application.Current.FindResource("Chip")
            };
            chip.Click += (_, _) =>
            {
                foreach (var other in panel.Children.OfType<ToggleButton>())
                    if (!ReferenceEquals(other, chip)) other.IsChecked = false;
                onChosen(chip.IsChecked == true ? value : null);
            };
            panel.Children.Add(chip);
        }
    }

    private static void SetChips(Panel panel, string? value)
    {
        foreach (var chip in panel.Children.OfType<ToggleButton>())
            chip.IsChecked = value != null && (string)chip.Tag == value;
    }

    private void RefreshSummaries()
    {
        var c = _checkIn;
        if (c == null) return;

        NoteHeader.Summary = Short(c.Note);
        DepressionHeader.Summary = c.Depression.Level is { } low ? $"{low}/10" : Short(c.Depression.Notes);
        AnxietyHeader.Summary = c.Anxiety.Level is { } worry ? $"{worry}/10" : Short(c.Anxiety.Notes);
        MedsHeader.Summary = c.Medications.Count > 0 ? $"{c.Medications.Count} taken" : CheckIn.Has(c.MedsOther) ? "Noted" : null;
        TransitionHeader.Summary = c.Transition.IsMilestone ? "Milestone" : CheckIn.Has(c.Transition.Hrt) ? "Noted" : Short(c.Transition.Milestone);

        var h = c.Habits;
        WaterCount.Text = (h.Water ?? 0).ToString(CultureInfo.CurrentCulture);
        CaffeineCount.Text = (h.Caffeine ?? 0).ToString(CultureInfo.CurrentCulture);
        WaterHeader.Summary = h.Water > 0 ? $"{h.Water} {(h.Water == 1 ? "glass" : "glasses")}" : null;
        CaffeineHeader.Summary = h.Caffeine > 0 ? $"{h.Caffeine} drinks" : null;
        SmokingHeader.Summary = h.Smoked ? (h.Cigarettes is int smoked ? $"{smoked} today" : "Yes") : null;
        AlcoholHeader.Summary = h.Drank ? (h.Alcohol is double drank ? $"{drank:0.#} today" : "Yes") : null;
        var areaCount = c.Symptoms.Areas?.Count(a => a.Value is >= 1 and <= 3) ?? 0;
        var areaWords = areaCount == 0 ? null : areaCount == 1 ? "1 area" : $"{areaCount} areas";
        SymptomsHeader.Summary = c.Symptoms.Pain is { } pain ? (areaWords == null ? $"Pain {pain}/10" : $"Pain {pain}/10, {areaWords}") : areaWords ?? Short(c.Symptoms.Notes);
        PainAreasText.Text = BodyMap.Describe(c.Symptoms.Areas);
        PainAreasText.Visibility = areaCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CycleHeader.Summary = c.Cycle.Flow ?? (CheckIn.Has(c.Cycle.Symptoms) ? "Noted" : Short(c.Cycle.Notes));
        if (_store != null && CycleSection.Visibility == Visibility.Visible)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            CycleStatus.Text = CycleService.Describe(CycleService.Analyse(_store, today, (_day, c.Cycle.Flow)), today);
        }
        SleepHeader.Summary = c.Sleep.Hours is { } hours ? $"{hours:0.#} h" : Short(c.Sleep.Notes);
        FitnessHeader.Summary = c.Fitness.Minutes is { } minutes ? $"{minutes} min" : Short(c.Fitness.Activity);

        var readings = c.BloodPressure.Where(b => b.Systolic.HasValue && b.Diastolic.HasValue).ToList();
        BpHeader.Summary = readings.Count == 0 ? null
            : readings.Count == 1 ? $"{readings[0].Systolic}/{readings[0].Diastolic}"
            : $"{readings[^1].Systolic}/{readings[^1].Diastolic} ({readings.Count} readings)";

        WeightHeader.Summary = c.WeightKg is { } kg ? Units.FormatWeight(kg, _settings?.WeightUnit ?? "kg") : null;
        TasksHeader.Summary = c.Tasks.Count > 0 ? $"{c.Tasks.Count(t => t.Done)} of {c.Tasks.Count} done" : null;
        StrugglesHeader.Summary = Short(c.Struggles);
        WinsHeader.Summary = Short(c.Wins);
    }

    private static string? Short(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Trim().ReplaceLineEndings(" ");
        return line.Length > 26 ? line[..26].TrimEnd() + "..." : line;
    }
}

// I pair a saved medication with whether I ticked it today
public sealed class MedRow : Observable
{
    private bool _taken;

    public MedRow(string id, string name, string? dose, bool taken)
    {
        Id = id;
        Name = name;
        Dose = dose;
        _taken = taken;
    }

    public string Id { get; }
    public string Name { get; }
    public string? Dose { get; }

    public bool Taken
    {
        get => _taken;
        set => Set(ref _taken, value);
    }
}
