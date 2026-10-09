using System.Globalization;
using Kaydence.Models;

namespace Kaydence.Services;

// I look through my depression and anxiety ratings for waves and patterns, in plain words my doctor can read
public static class MindPatterns
{
    // I count 7 or more out of 10 as a high day
    public const int High = 7;

    // I need a few days on each side before I compare anything, so one odd day doesn't make a pattern
    private const int MinDays = 3;

    // I only mention a difference if it's at least this big on the 1 to 10 scale
    private const double MinDifference = 1.0;

    public static readonly (string Name, Func<CheckIn, int?> Pick)[] Measures =
    {
        ("Depression", c => c.Depression.Level),
        ("Anxiety", c => c.Anxiety.Level)
    };

    public sealed record Spell(DateOnly Start, DateOnly End, int Peak)
    {
        public int Days => End.DayNumber - Start.DayNumber + 1;
    }

    public static bool HasAny(IEnumerable<(DateOnly Day, CheckIn CheckIn)> days) =>
        days.Any(d => d.CheckIn.Depression.Level.HasValue || d.CheckIn.Anxiety.Level.HasValue);

    // I find runs of high days, letting one day without a rating sit in the middle without breaking the run
    public static List<Spell> Spells(List<(DateOnly Day, int Level)> ratings)
    {
        var spells = new List<Spell>();
        DateOnly? start = null;
        DateOnly last = default;
        var peak = 0;
        foreach (var (day, level) in ratings.OrderBy(r => r.Day))
        {
            var high = level >= High;
            if (start != null && (!high || day.DayNumber - last.DayNumber > 2))
            {
                if (last.DayNumber - start.Value.DayNumber >= 1) spells.Add(new Spell(start.Value, last, peak));
                start = null;
            }
            if (!high) continue;
            if (start == null)
            {
                start = day;
                peak = 0;
            }
            last = day;
            peak = Math.Max(peak, level);
        }
        if (start != null && last.DayNumber - start.Value.DayNumber >= 1) spells.Add(new Spell(start.Value, last, peak));
        return spells;
    }

    public static List<string> Describe(List<(DateOnly Day, CheckIn CheckIn)> days)
    {
        var lines = new List<string>();
        var culture = CultureInfo.CurrentCulture;
        string N(double v) => v.ToString("0.#", culture);
        string Date(DateOnly d) => d.ToString("d MMM", culture);

        foreach (var (name, pick) in Measures)
        {
            var ratings = days.Where(d => pick(d.CheckIn).HasValue).Select(d => (d.Day, Level: pick(d.CheckIn)!.Value)).ToList();
            if (ratings.Count == 0) continue;
            var lower = name.ToLowerInvariant();
            var highDays = ratings.Count(r => r.Level >= High);
            lines.Add($"{name}: rated on {ratings.Count} {(ratings.Count == 1 ? "day" : "days")}, average {N(ratings.Average(r => r.Level))} out of 10, "
                      + (highDays == 0 ? $"never {High} or more." : $"{High} or more on {highDays} {(highDays == 1 ? "day" : "days")}."));

            var spells = Spells(ratings);
            if (spells.Count > 0)
            {
                var longest = spells.OrderByDescending(s => s.Days).First();
                var text = spells.Count == 1
                    ? $"One high spell of {longest.Days} days, {Date(longest.Start)} to {Date(longest.End)}, peaking at {longest.Peak}."
                    : $"{spells.Count} high spells of {High} or more for 2 days or longer. The longest was {longest.Days} days, {Date(longest.Start)} to {Date(longest.End)}.";
                if (spells.Count >= 2)
                {
                    var gaps = spells.Zip(spells.Skip(1), (a, b) => b.Start.DayNumber - a.Start.DayNumber).ToList();
                    text += $" They started about {N(Math.Round(gaps.Average()))} days apart.";
                }
                lines.Add(text);
            }

            var overall = ratings.Average(r => r.Level);
            var weekday = ratings.GroupBy(r => r.Day.DayOfWeek)
                .Where(g => g.Count() >= 2)
                .Select(g => (Day: g.Key, Average: g.Average(r => r.Level)))
                .OrderByDescending(g => g.Average)
                .ToList();
            if (weekday.Count >= 3 && weekday[0].Average - overall >= MinDifference)
                lines.Add($"Your {lower} is usually highest on {culture.DateTimeFormat.GetDayName(weekday[0].Day)}s, {N(weekday[0].Average)} on average.");

            void Compare(string when, Func<CheckIn, bool?> test)
            {
                var yes = days.Where(d => pick(d.CheckIn).HasValue && test(d.CheckIn) == true).Select(d => pick(d.CheckIn)!.Value).ToList();
                var no = days.Where(d => pick(d.CheckIn).HasValue && test(d.CheckIn) == false).Select(d => pick(d.CheckIn)!.Value).ToList();
                if (yes.Count < MinDays || no.Count < MinDays) return;
                var a = yes.Average();
                var b = no.Average();
                if (Math.Abs(a - b) < MinDifference) return;
                lines.Add($"{when}, your {lower} averaged {N(a)}, compared with {N(b)} on other days.");
            }

            // I only test things that were actually logged that day, so a blank sleep box doesn't count as no sleep
            Compare("After under 6 hours of sleep", c => c.Sleep.Hours is { } h ? h < 6 : null);
            Compare("On period days", c => CheckIn.Has(c.Cycle.Flow) ? c.Cycle.IsPeriod : null);
            Compare("On days with pain of 5 or more", c => c.Symptoms.Pain is { } p ? p >= 5 : null);
            Compare("On days you exercised", c => c.Fitness.HasData ? c.Fitness.Minutes is not 0 : null);
        }

        // I check whether the two tend to move together
        var pairs = days.Where(d => d.CheckIn.Depression.Level.HasValue && d.CheckIn.Anxiety.Level.HasValue)
            .Select(d => (X: (double)d.CheckIn.Depression.Level!.Value, Y: (double)d.CheckIn.Anxiety.Level!.Value)).ToList();
        if (pairs.Count >= 7 && Correlation(pairs) is { } r)
        {
            if (r >= 0.5) lines.Add("Your depression and anxiety tend to rise and fall together.");
            else if (r <= -0.3) lines.Add("When your depression is high, your anxiety tends to be lower, and the other way round.");
            else if (Math.Abs(r) < 0.2) lines.Add("Your depression and anxiety mostly move on their own, separately from each other.");
        }
        return lines;
    }

    private static double? Correlation(List<(double X, double Y)> pairs)
    {
        var mx = pairs.Average(p => p.X);
        var my = pairs.Average(p => p.Y);
        var sxy = pairs.Sum(p => (p.X - mx) * (p.Y - my));
        var sxx = pairs.Sum(p => (p.X - mx) * (p.X - mx));
        var syy = pairs.Sum(p => (p.Y - my) * (p.Y - my));
        if (sxx < 0.0001 || syy < 0.0001) return null;
        return sxy / Math.Sqrt(sxx * syy);
    }
}
