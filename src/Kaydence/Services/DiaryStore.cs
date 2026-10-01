using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using Kaydence.Models;

namespace Kaydence.Services;

// I keep every day in its own folder: entries\2026\2026-09-30\day.json plus ink and pictures
public sealed class DiaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff" };

    private readonly Dictionary<DateOnly, DaySummary> _index;

    public string Root { get; }

    private string EntriesRoot => Path.Combine(Root, "entries");
    private string IndexPath => Path.Combine(Root, "index.json");
    private string MedicationsPath => Path.Combine(Root, "medications.json");
    private string CountersPath => Path.Combine(Root, "counters.json");

    public DiaryStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(EntriesRoot);
        _index = LoadIndex();
        SaveIndex();
        Log.Info("Store", $"Diary opened at {root}: {_index.Count} days, writes {(CryptoService.SealWrites ? "encrypted" : "plain")}");
    }

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public string DayFolder(DateOnly day) =>
        Path.Combine(EntriesRoot, day.Year.ToString("0000", CultureInfo.InvariantCulture), Key(day));

    public string ImagePath(DateOnly day, string fileName) => Path.Combine(DayFolder(day), fileName);

    public DaySummary? GetSummary(DateOnly day) => _index.TryGetValue(day, out var summary) ? summary : null;

    public IReadOnlyCollection<DateOnly> Days => _index.Keys;

    public string DeletedFolder => Path.Combine(Root, "deleted");

    public DayEntry LoadDay(DateOnly day)
    {
        var file = Path.Combine(DayFolder(day), "day.json");
        if (File.Exists(file))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<DayEntry>(CryptoService.ReadText(file), JsonOptions);
                if (entry != null)
                {
                    entry.Date = Key(day);
                    return entry;
                }
                Log.Warn("Store", $"{Key(day)} day.json was empty");
            }
            catch (Exception ex)
            {
                // I keep a copy of a broken file so nothing I wrote is ever thrown away
                Log.Error("Store", $"Couldn't read {Key(day)} ({Log.Describe(file)}), keeping a broken copy. Key loaded: {CryptoService.Key != null}", ex);
                File.Copy(file, $"{file}.broken-{DateTime.Now:yyyyMMddHHmmss}", true);
            }
        }
        return new DayEntry { Date = Key(day) };
    }

    public byte[]? LoadInk(DateOnly day)
    {
        var file = Path.Combine(DayFolder(day), "ink.isf");
        return File.Exists(file) ? CryptoService.ReadBytes(file) : null;
    }

    public void SaveDay(DayEntry entry, byte[]? ink, bool hasContent)
    {
        var day = DateOnly.ParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var folder = DayFolder(day);
        var dayFile = Path.Combine(folder, "day.json");
        var inkFile = Path.Combine(folder, "ink.isf");

        if (!hasContent)
        {
            Log.Info("Store", $"{entry.Date} is now empty, removing its day file and drawings");
            // I keep the pictures for now so undo can bring them back, TidyDay clears them when I leave the day
            if (Directory.Exists(folder))
            {
                DeleteIfExists(dayFile);
                DeleteIfExists(inkFile);
            }
            if (_index.Remove(day)) SaveIndex();
            return;
        }

        Directory.CreateDirectory(folder);
        WriteAtomic(dayFile, JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions));
        if (ink != null) WriteAtomic(inkFile, ink);
        else DeleteIfExists(inkFile);

        _index[day] = DaySummary.From(entry);
        SaveIndex();
        Log.Debug("Store", $"Saved {entry.Date}: {DescribeItems(entry.Items)}, drawings {(ink == null ? "none" : $"{ink.Length:N0} bytes")}, check-in {(entry.CheckIn.HasAnyData() ? "filled" : "empty")}");
    }

    // I count what's on a page by kind, never what it says
    public static string DescribeItems(IEnumerable<PageItem> items)
    {
        var groups = items.GroupBy(i => i.Kind).Select(g => $"{g.Count()} {g.Key}").ToList();
        return groups.Count == 0 ? "nothing on the page" : string.Join(", ", groups);
    }

    // I clear out pictures I deleted from a page once I've moved on from that day
    public void TidyDay(DateOnly day, DayEntry entry)
    {
        var folder = DayFolder(day);
        if (!Directory.Exists(folder)) return;
        try
        {
            RemoveUnusedImages(folder, entry);
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
                Log.Info("Store", $"Removed the empty folder for {Key(day)}");
            }
        }
        catch (IOException ex)
        {
            // I'll try again another time if something still has a file open
            Log.Warn("Store", $"Couldn't tidy {Key(day)} yet", ex);
        }
    }

    // I move a deleted day into a deleted folder instead of wiping it, just in case I change my mind
    public void DeleteDay(DateOnly day)
    {
        var folder = DayFolder(day);
        if (Directory.Exists(folder))
        {
            Directory.CreateDirectory(DeletedFolder);
            var target = Path.Combine(DeletedFolder, $"{Key(day)} deleted {DateTime.Now:yyyy-MM-dd HHmmss}");
            Directory.Move(folder, target);
            Log.Info("Store", $"Moved {Key(day)} into the deleted folder");
        }
        if (_index.Remove(day)) SaveIndex();
    }

    public string SaveImage(DateOnly day, BitmapSource image)
    {
        var folder = DayFolder(day);
        Directory.CreateDirectory(folder);
        var name = NewImageName(".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        WriteAtomic(Path.Combine(folder, name), bytes);
        Log.Info("Store", $"Saved picture {name} for {Key(day)}: {image.PixelWidth} x {image.PixelHeight}, format {image.Format}, {bytes.Length:N0} bytes as PNG, now {Log.Describe(Path.Combine(folder, name))}");
        return name;
    }

    // I copy a picture in as a clean copy, turned upright and without the location or camera details phones hide inside photos
    public string ImportImage(DateOnly day, string sourcePath)
    {
        var folder = DayFolder(day);
        Directory.CreateDirectory(folder);
        var original = File.ReadAllBytes(sourcePath);
        var (bytes, extension) = ImageHelper.CleanForDiary(original, Path.GetExtension(sourcePath).ToLowerInvariant());
        var name = NewImageName(extension);
        WriteAtomic(Path.Combine(folder, name), bytes);
        Log.Info("Store", $"Added a {Path.GetExtension(sourcePath)} picture as {name} for {Key(day)}: {original.Length:N0} bytes in, {bytes.Length:N0} bytes kept, starts {ImageHelper.Signature(bytes)}, now {Log.Describe(Path.Combine(folder, name))}");
        return name;
    }

    // I keep voice notes next to the pictures for that day, sealed like everything else when my diary is encrypted
    public string SaveAudio(DateOnly day, byte[] wav)
    {
        var folder = DayFolder(day);
        Directory.CreateDirectory(folder);
        var name = $"voice-{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.wav";
        WriteAtomic(Path.Combine(folder, name), wav);
        Log.Info("Store", $"Saved voice note {name} for {Key(day)}: {wav.Length:N0} bytes");
        return name;
    }

    public List<Medication> LoadMedications()
    {
        try
        {
            if (File.Exists(MedicationsPath))
                return JsonSerializer.Deserialize<List<Medication>>(CryptoService.ReadText(MedicationsPath), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            // I start an empty list rather than crash if the file is damaged
            Log.Error("Store", "My medications list couldn't be read", ex);
        }
        return new();
    }

    public void SaveMedications(List<Medication> medications) =>
        WriteAtomic(MedicationsPath, JsonSerializer.SerializeToUtf8Bytes(medications, JsonOptions));

    // I keep my days since counters in their own little file next to my medications
    public List<Counter> LoadCounters()
    {
        try
        {
            if (File.Exists(CountersPath))
                return JsonSerializer.Deserialize<List<Counter>>(CryptoService.ReadText(CountersPath), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            // I start with no counters rather than crash if the file gets damaged
            Log.Error("Store", "My counters couldn't be read", ex);
        }
        return new();
    }

    public void SaveCounters(List<Counter> counters) =>
        WriteAtomic(CountersPath, JsonSerializer.SerializeToUtf8Bytes(counters, JsonOptions));

    // I find the last day I had tasks so I can carry over anything I didn't finish
    public (DateOnly Date, List<TaskItem> Tasks)? FindUnfinishedTasks(DateOnly before)
    {
        foreach (var day in _index.Keys.Where(d => d < before && d >= before.AddDays(-14)).OrderByDescending(d => d))
        {
            var tasks = LoadDay(day).CheckIn.Tasks;
            if (tasks.Count == 0) continue;
            var unfinished = tasks.Where(t => !t.Done && !string.IsNullOrWhiteSpace(t.Text)).ToList();
            return unfinished.Count > 0 ? (day, unfinished) : null;
        }
        return null;
    }

    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NewImageName(string extension) =>
        $"img-{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}{extension}";

    private static void RemoveUnusedImages(string folder, DayEntry entry)
    {
        if (!Directory.Exists(folder)) return;
        var used = entry.Items.Select(i => i.Image ?? i.Audio).Where(n => n != null).Select(n => n!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(folder, "img-*").Concat(Directory.GetFiles(folder, "voice-*")))
        {
            if (used.Contains(Path.GetFileName(file))) continue;
            try
            {
                File.Delete(file);
                Log.Info("Store", $"Tidied away {Path.GetFileName(file)}, it's no longer on the page");
            }
            catch (IOException ex)
            {
                // I leave a picture for next time if Windows still has it open
                Log.Warn("Store", $"Couldn't tidy {Path.GetFileName(file)} yet", ex);
            }
        }
    }

    private Dictionary<DateOnly, DaySummary> LoadIndex()
    {
        try
        {
            if (File.Exists(IndexPath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, DaySummary>>(CryptoService.ReadText(IndexPath), JsonOptions);
                if (raw != null)
                    return raw.ToDictionary(p => DateOnly.ParseExact(p.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), p => p.Value);
            }
        }
        catch (Exception ex)
        {
            // I rebuild the index from the day files if it's missing or damaged
            Log.Warn("Store", "The index couldn't be read, rebuilding it from the day files", ex);
        }
        using (Log.Time("Store", "Rebuilding the index"))
        {
            return RebuildIndex();
        }
    }

    private Dictionary<DateOnly, DaySummary> RebuildIndex()
    {
        var map = new Dictionary<DateOnly, DaySummary>();
        foreach (var file in Directory.EnumerateFiles(EntriesRoot, "day.json", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";
            if (!DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<DayEntry>(CryptoService.ReadText(file), JsonOptions);
                if (entry != null) map[day] = DaySummary.From(entry);
            }
            catch (Exception ex)
            {
                // I skip a broken day here and let LoadDay keep a copy of it
                Log.Warn("Store", $"Skipped {name} while rebuilding the index", ex);
            }
        }
        return map;
    }

    private void SaveIndex()
    {
        var raw = _index.OrderBy(p => p.Key).ToDictionary(p => Key(p.Key), p => p.Value);
        WriteAtomic(IndexPath, JsonSerializer.SerializeToUtf8Bytes(raw, JsonOptions));
    }

    // I write to a temp file first so a crash mid save can never leave half a file, sealed if my diary is encrypted
    private static void WriteAtomic(string path, byte[] data)
    {
        try
        {
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, CryptoService.Prepare(data));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Log.Error("Store", $"Couldn't write {Path.GetFileName(path)} ({data.Length:N0} bytes)", ex);
            throw;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
