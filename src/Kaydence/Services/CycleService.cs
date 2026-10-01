using System.Globalization;
using Kaydence.Models;

namespace Kaydence.Services;

// I work out my cycle from the days I logged a period, and make a rough guess at the next one
public static class CycleService
{
    public const int DefaultCycle = 28;
    public const int DefaultPeriod = 5;

    public sealed record Cycle(DateOnly Start, int PeriodDays, int? Length);

    public sealed record Summary(
        List<Cycle> Cycles,
        double AverageCycle,
        double AveragePeriod,
        DateOnly? NextStart,
        int? DayOfCycle,
        bool OnPeriod,
        HashSet<DateOnly> Predicted);

    // I can take the day I'm editing as an override, so the guess updates before it's saved
    public static Summary Analyse(DiaryStore store, DateOnly today, (DateOnly Day, string? Flow)? editing = null)
    {
        static bool IsPeriod(string? flow) => flow is "Light" or "Medium" or "Heavy";
        var periodDays = store.Days
            .Where(d => d <= today && (editing is { } e && e.Day == d ? IsPeriod(e.Flow) : IsPeriod(store.GetSummary(d)?.Flow)))
            .ToList();
        if (editing is { } edited && edited.Day <= today && IsPeriod(edited.Flow) && !periodDays.Contains(edited.Day)) periodDays.Add(edited.Day);
        periodDays.Sort();

        // I group period days into periods, letting one missed day in the middle still count as the same period
        var periods = new List<(DateOnly Start, DateOnly End)>();
        foreach (var day in periodDays)
        {
            if (periods.Count > 0 && day.DayNumber - periods[^1].End.DayNumber <= 2) periods[^1] = (periods[^1].Start, day);
            else periods.Add((day, day));
        }

        var cycles = new List<Cycle>();
        for (var i = 0; i < periods.Count; i++)
        {
            int? length = i + 1 < periods.Count ? periods[i + 1].Start.DayNumber - periods[i].Start.DayNumber : null;
            cycles.Add(new Cycle(periods[i].Start, periods[i].End.DayNumber - periods[i].Start.DayNumber + 1, length));
        }

        // I only trust sensible cycle lengths and the last six of them, bodies change over time
        var lengths = cycles.Where(c => c.Length is >= 15 and <= 60).Select(c => c.Length!.Value).TakeLast(6).ToList();
        var averageCycle = lengths.Count > 0 ? lengths.Average() : DefaultCycle;
        var periodLengths = cycles.Select(c => c.PeriodDays).Where(p => p <= 12).TakeLast(6).ToList();
        var averagePeriod = periodLengths.Count > 0 ? periodLengths.Average() : DefaultPeriod;

        DateOnly? next = null;
        int? dayOfCycle = null;
        var onPeriod = false;
        var predicted = new HashSet<DateOnly>();
        if (cycles.Count > 0)
        {
            var last = cycles[^1];
            dayOfCycle = today.DayNumber - last.Start.DayNumber + 1;
            onPeriod = today.DayNumber - periods[^1].End.DayNumber <= 1 && dayOfCycle <= 12;
            next = last.Start.AddDays((int)Math.Round(averageCycle));
            for (var round = 0; round < 3; round++)
            {
                var start = next.Value.AddDays((int)Math.Round(averageCycle * round));
                for (var d = 0; d < Math.Round(averagePeriod); d++) predicted.Add(start.AddDays(d));
            }
        }

        return new Summary(cycles, averageCycle, averagePeriod, next, dayOfCycle, onPeriod, predicted);
    }

    // I say where I am in my cycle in plain words for the check-in panel
    public static string Describe(Summary summary, DateOnly today)
    {
        if (summary.Cycles.Count == 0 || summary.NextStart is not { } next || summary.DayOfCycle is not { } day)
            return "Log your flow on period days and Kaydence will start guessing when your next one is due.";
        var culture = CultureInfo.CurrentCulture;
        var when = next.ToString("d MMMM", culture);
        if (summary.OnPeriod) return $"On your period, day {day}. Next one due around {when}.";
        var until = next.DayNumber - today.DayNumber;
        if (until > 1) return $"Day {day} of your cycle. Next period due in about {until} days, around {when}.";
        if (until is 0 or 1) return $"Day {day} of your cycle. Your period is due around now.";
        return $"Day {day} of your cycle. Your period was due around {when}, {-until} {(until == -1 ? "day" : "days")} ago.";
    }
}
