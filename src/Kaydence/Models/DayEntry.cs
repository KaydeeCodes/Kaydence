namespace Kaydence.Models;

// I save one of these per day as day.json
public sealed class DayEntry
{
    public int Version { get; set; } = 1;
    public string Date { get; set; } = "";
    public CheckIn CheckIn { get; set; } = new();
    public List<PageItem> Items { get; set; } = new();
    public string? Text { get; set; }
    public DateTime? Updated { get; set; }
}

// I use Kind to tell text boxes and pictures apart on the page
public sealed class PageItem
{
    public string Kind { get; set; } = "text";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Z { get; set; }
    public string? Xaml { get; set; }
    public string? Image { get; set; }
    public string? Color { get; set; }
    public List<CheckEntry>? Checks { get; set; }
    public string? Audio { get; set; }
    public double? Duration { get; set; }
    public string? Wave { get; set; }
}

// I'm one line on a checklist I put on my page
public sealed class CheckEntry
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

// I count the days since something started, like HRT or going alcohol free
public sealed class Counter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateOnly Since { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

// I keep this tiny summary per day so the calendar can draw dots without opening every file
public sealed class DaySummary
{
    public int? Mood { get; set; }
    public bool Milestone { get; set; }
    public string? Flow { get; set; }

    public static DaySummary From(DayEntry entry) => new()
    {
        Mood = entry.CheckIn.Mood,
        Milestone = entry.CheckIn.Transition.IsMilestone,
        Flow = entry.CheckIn.Cycle.Flow
    };
}

// I keep my regular medications in one list so I only tick them each day
public sealed class Medication
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string? Dose { get; set; }
    public bool Active { get; set; } = true;
}
