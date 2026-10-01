using System.Windows.Media;

namespace Kaydence.Models;

public sealed record MoodLevel(int Value, string Name, Color Color);

// I store moods as 1 to 5 where 5 is my best kind of day
public static class Moods
{
    public static readonly IReadOnlyList<MoodLevel> All = new[]
    {
        new MoodLevel(5, "Great", Color.FromRgb(0x3F, 0xB9, 0x7C)),
        new MoodLevel(4, "Good", Color.FromRgb(0x9C, 0xCB, 0x4A)),
        new MoodLevel(3, "Okay", Color.FromRgb(0xF2, 0xC1, 0x4E)),
        new MoodLevel(2, "Bad", Color.FromRgb(0xF2, 0x8C, 0x4A)),
        new MoodLevel(1, "Awful", Color.FromRgb(0xE5, 0x4B, 0x5A))
    };

    public static MoodLevel? Get(int? value) => All.FirstOrDefault(m => m.Value == value);
}
