using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Kaydence.Models;

// I keep every check-in section as real values so my doctor reports can use them later
public sealed class CheckIn : Observable
{
    private int? _mood;
    private string? _moodNote;
    private string? _note;
    private string? _medsOther;
    private double? _weightKg;
    private string? _struggles;
    private string? _wins;
    private bool _tracking;

    public int? Mood { get => _mood; set => Set(ref _mood, value); }
    public string? MoodNote { get => _moodNote; set => Set(ref _moodNote, value); }
    public string? Note { get => _note; set => Set(ref _note, value); }
    public ObservableCollection<MedDose> Medications { get; set; } = new();
    public string? MedsOther { get => _medsOther; set => Set(ref _medsOther, value); }
    public TransitionInfo Transition { get; set; } = new();
    public SymptomsInfo Symptoms { get; set; } = new();
    public ScaleInfo Depression { get; set; } = new();
    public ScaleInfo Anxiety { get; set; } = new();
    public SleepInfo Sleep { get; set; } = new();
    public FitnessInfo Fitness { get; set; } = new();
    public ObservableCollection<BpReading> BloodPressure { get; set; } = new();
    public double? WeightKg { get => _weightKg; set => Set(ref _weightKg, value); }
    public HabitsInfo Habits { get; set; } = new();
    public CycleInfo Cycle { get; set; } = new();
    public ObservableCollection<TaskItem> Tasks { get; set; } = new();
    public string? Struggles { get => _struggles; set => Set(ref _struggles, value); }
    public string? Wins { get => _wins; set => Set(ref _wins, value); }

    public event Action? Changed;

    // I call this once after loading so any edit anywhere in the check-in tells the app to save
    public void StartTracking()
    {
        if (_tracking) return;
        _tracking = true;
        PropertyChanged += (_, _) => Changed?.Invoke();
        Hook(Transition);
        Hook(Symptoms);
        Hook(Depression);
        Hook(Anxiety);
        Hook(Sleep);
        Hook(Fitness);
        Hook(Habits);
        Hook(Cycle);
        HookList(Medications);
        HookList(BloodPressure);
        HookList(Tasks);
    }

    public bool HasAnyData() =>
        Mood.HasValue || Has(MoodNote) || Has(Note) || Medications.Count > 0 || Has(MedsOther)
        || Transition.HasData || Symptoms.HasData || Depression.HasData || Anxiety.HasData || Sleep.HasData || Fitness.HasData
        || Habits.HasData || Cycle.HasData || BloodPressure.Any(b => b.HasData) || WeightKg.HasValue || Tasks.Any(t => Has(t.Text))
        || Has(Struggles) || Has(Wins);

    internal static bool Has(string? text) => !string.IsNullOrWhiteSpace(text);

    private void Hook(INotifyPropertyChanged item) => item.PropertyChanged += (_, _) => Changed?.Invoke();

    private void HookList<T>(ObservableCollection<T> list) where T : INotifyPropertyChanged
    {
        foreach (var item in list) Hook(item);
        list.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (INotifyPropertyChanged item in e.NewItems) Hook(item);
            Changed?.Invoke();
        };
    }
}

public sealed class MedDose : Observable
{
    public string MedId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Dose { get; set; }
    public string? Time { get; set; }
}

public sealed class TransitionInfo : Observable
{
    private string? _hrt;
    private string? _injectionSite;
    private bool _isMilestone;
    private string? _milestone;

    public string? Hrt { get => _hrt; set => Set(ref _hrt, value); }
    public string? InjectionSite { get => _injectionSite; set => Set(ref _injectionSite, value); }
    public bool IsMilestone { get => _isMilestone; set => Set(ref _isMilestone, value); }
    public string? Milestone { get => _milestone; set => Set(ref _milestone, value); }

    [JsonIgnore]
    public bool HasData => CheckIn.Has(Hrt) || CheckIn.Has(InjectionSite) || IsMilestone || CheckIn.Has(Milestone);
}

public sealed class SymptomsInfo : Observable
{
    private int? _pain;
    private string? _notes;
    private Dictionary<string, int>? _areas;

    public int? Pain { get => _pain; set => Set(ref _pain, value); }
    public string? Notes { get => _notes; set => Set(ref _notes, value); }

    // I keep where it hurts as body area keys with 1 for mild, 2 for moderate and 3 for severe, and I always swap in a new one so the change is noticed
    public Dictionary<string, int>? Areas { get => _areas; set => Set(ref _areas, value is { Count: > 0 } ? value : null); }

    [JsonIgnore]
    public bool HasData => Pain.HasValue || CheckIn.Has(Notes) || Areas is { Count: > 0 };
}

// I rate my depression or anxiety from 1 to 10 each day with a note, so my doctor and I can see the waves
public sealed class ScaleInfo : Observable
{
    private int? _level;
    private string? _notes;

    public int? Level { get => _level; set => Set(ref _level, value is >= 1 and <= 10 ? value : null); }
    public string? Notes { get => _notes; set => Set(ref _notes, value); }

    [JsonIgnore]
    public bool HasData => Level.HasValue || CheckIn.Has(Notes);
}

public sealed class SleepInfo : Observable
{
    private double? _hours;
    private string? _notes;

    public double? Hours { get => _hours; set => Set(ref _hours, value); }
    public string? Notes { get => _notes; set => Set(ref _notes, value); }

    [JsonIgnore]
    public bool HasData => Hours.HasValue || CheckIn.Has(Notes);
}

// I note my period flow and how my cycle makes me feel, only used if I switch the tracker on
public sealed class CycleInfo : Observable
{
    public static readonly string[] Flows = { "Spotting", "Light", "Medium", "Heavy" };

    public static readonly string[] Feelings =
    {
        "Cramps", "Bloating", "Headache", "Tender breasts", "Back ache", "Mood swings", "Tired", "Spots", "Cravings", "Nausea"
    };

    private string? _flow;
    private string? _symptoms;
    private string? _notes;

    public string? Flow { get => _flow; set => Set(ref _flow, value); }

    // I keep the feelings as one comma separated line so it stays easy to read in the file
    public string? Symptoms { get => _symptoms; set => Set(ref _symptoms, value); }
    public string? Notes { get => _notes; set => Set(ref _notes, value); }

    [JsonIgnore]
    public bool IsPeriod => Flow is "Light" or "Medium" or "Heavy";

    [JsonIgnore]
    public bool HasData => CheckIn.Has(Flow) || CheckIn.Has(Symptoms) || CheckIn.Has(Notes);
}

public sealed class FitnessInfo : Observable
{
    private string? _activity;
    private int? _minutes;

    public string? Activity { get => _activity; set => Set(ref _activity, value); }
    public int? Minutes { get => _minutes; set => Set(ref _minutes, value); }

    [JsonIgnore]
    public bool HasData => CheckIn.Has(Activity) || Minutes.HasValue;
}

// I keep my habit counters together: water, caffeine, smoking and alcohol
public sealed class HabitsInfo : Observable
{
    private int? _water;
    private int? _caffeine;
    private bool _smoked;
    private int? _cigarettes;
    private bool _drank;
    private double? _alcohol;

    public int? Water { get => _water; set => Set(ref _water, value); }
    public int? Caffeine { get => _caffeine; set => Set(ref _caffeine, value); }
    public bool Smoked { get => _smoked; set => Set(ref _smoked, value); }
    public int? Cigarettes { get => _cigarettes; set => Set(ref _cigarettes, value); }
    public bool Drank { get => _drank; set => Set(ref _drank, value); }
    public double? Alcohol { get => _alcohol; set => Set(ref _alcohol, value); }

    [JsonIgnore]
    public bool HasData => Water > 0 || Caffeine > 0 || Smoked || Cigarettes.HasValue || Drank || Alcohol.HasValue;
}

public sealed class BpReading : Observable
{
    private string? _time;
    private int? _systolic;
    private int? _diastolic;
    private int? _pulse;

    public string? Time { get => _time; set => Set(ref _time, value); }
    public int? Systolic { get => _systolic; set => Set(ref _systolic, value); }
    public int? Diastolic { get => _diastolic; set => Set(ref _diastolic, value); }
    public int? Pulse { get => _pulse; set => Set(ref _pulse, value); }

    [JsonIgnore]
    public bool HasData => Systolic.HasValue || Diastolic.HasValue || Pulse.HasValue;
}

public sealed class TaskItem : Observable
{
    private string? _text;
    private bool _done;

    public string? Text { get => _text; set => Set(ref _text, value); }
    public bool Done { get => _done; set => Set(ref _done, value); }
}
