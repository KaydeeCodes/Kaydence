using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kaydence.Models;

namespace Kaydence.Services;

// I copy the whole diary out into ordinary files anyone can open: a web page, a text file per day, and the pictures and voice notes
public static class ExportService
{
    public sealed record Result(string Folder, int Days, int Pictures, int VoiceNotes, int Problems);

    // I run on my own STA thread because reading pages and drawing ink both need WPF objects
    public static Task<Result> RunAsync(DiaryStore store, string parentFolder, string weightUnit, IProgress<(int Done, int Total)> progress)
    {
        var finished = new TaskCompletionSource<Result>();
        var thread = new Thread(() =>
        {
            try
            {
                finished.SetResult(Run(store, parentFolder, weightUnit, progress));
            }
            catch (Exception ex)
            {
                finished.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Kaydence export"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task;
    }

    private static Result Run(DiaryStore store, string parentFolder, string weightUnit, IProgress<(int Done, int Total)> progress)
    {
        var folder = Path.Combine(parentFolder, $"Kaydence export {DateTime.Now:yyyy-MM-dd HHmm}");
        Directory.CreateDirectory(folder);
        var days = store.Days.OrderBy(d => d).ToList();
        Log.Info("Export", $"Exporting {days.Count} days");

        var html = new StringBuilder();
        html.Append(PageStart(days));
        int pictures = 0, voice = 0, problems = 0;
        var options = new ReportOptions { Writing = true, Pictures = true, Mood = true, CheckIn = true, Health = true, Transition = true };
        var currentMonth = "";

        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var relative = Path.Combine(day.Year.ToString("0000", CultureInfo.InvariantCulture), key);
            var dayFolder = Path.Combine(folder, relative);
            try
            {
                var entry = store.LoadDay(day);
                Directory.CreateDirectory(dayFolder);
                var month = day.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
                if (month != currentMonth)
                {
                    html.Append($"<h2 id=\"m{day:yyyy-MM}\">{Html(month)}</h2>\n");
                    currentMonth = month;
                }

                var text = new StringBuilder();
                var title = day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
                text.AppendLine(title);
                text.AppendLine(new string('=', title.Length));
                html.Append($"<article id=\"d{key}\"><header><h3>{Html(title)}</h3>");

                if (Moods.Get(entry.CheckIn.Mood) is { } mood)
                {
                    text.AppendLine($"Mood: {mood.Name}");
                    html.Append($"<span class=\"mood\" style=\"--c:#{mood.Color.R:X2}{mood.Color.G:X2}{mood.Color.B:X2}\">{Html(mood.Name)}</span>");
                }
                if (entry.CheckIn.Transition.IsMilestone) html.Append("<span class=\"star\">Milestone</span>");
                html.Append("</header>\n");

                var writing = DayText.Get(entry).Replace("\r\n", "\n").Trim();
                if (writing.Length > 0)
                {
                    text.AppendLine();
                    text.AppendLine(writing);
                    foreach (var paragraph in writing.Split('\n').Where(p => p.Trim().Length > 0))
                        html.Append($"<p>{Html(paragraph.Trim())}</p>\n");
                }

                foreach (var name in entry.Items.Where(x => x.Kind == "image" && !string.IsNullOrEmpty(x.Image)).Select(x => x.Image!))
                {
                    if (CopyOut(store.ImagePath(day, name), Path.Combine(dayFolder, name)))
                    {
                        pictures++;
                        html.Append($"<img src=\"{Url(relative, name)}\" alt=\"A picture from this day\" loading=\"lazy\">\n");
                    }
                    else problems++;
                }

                foreach (var item in entry.Items.Where(x => x.Kind == "voice" && !string.IsNullOrEmpty(x.Audio)))
                {
                    if (CopyOut(store.ImagePath(day, item.Audio!), Path.Combine(dayFolder, item.Audio!)))
                    {
                        voice++;
                        html.Append($"<p class=\"voice\">Voice note, {TimeSpan.FromSeconds(item.Duration ?? 0):m\\:ss}<br><audio controls preload=\"none\" src=\"{Url(relative, item.Audio!)}\"></audio></p>\n");
                    }
                    else problems++;
                }

                if (SaveDrawings(store, day, Path.Combine(dayFolder, "drawings.png")))
                    html.Append($"<img class=\"ink\" src=\"{Url(relative, "drawings.png")}\" alt=\"Drawings from this day\" loading=\"lazy\">\n");

                var facts = ReportBuilder.Facts(entry.CheckIn, options, weightUnit);
                if (facts.Count > 0)
                {
                    text.AppendLine();
                    html.Append("<dl>");
                    foreach (var (label, value) in facts)
                    {
                        text.AppendLine($"{label}: {value}");
                        html.Append($"<dt>{Html(label)}</dt><dd>{Html(value)}</dd>");
                    }
                    html.Append("</dl>\n");
                }
                html.Append("</article>\n");
                File.WriteAllText(Path.Combine(dayFolder, key + ".txt"), text.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                problems++;
                Log.Error("Export", $"Couldn't export {key}", ex);
                html.Append($"<p class=\"problem\">{Html(key)} couldn't be exported.</p>\n");
            }
            progress.Report((i + 1, days.Count));
        }

        html.Append("</main></body></html>\n");
        File.WriteAllText(Path.Combine(folder, "Kaydence diary.html"), html.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "Read me.txt"), ReadMe(), new UTF8Encoding(true));
        Log.Info("Export", $"Export finished: {days.Count} days, {pictures} pictures, {voice} voice notes, {problems} problems");
        return new Result(folder, days.Count, pictures, voice, problems);
    }

    // I write a plain, unencrypted copy so the file opens anywhere
    private static bool CopyOut(string source, string target)
    {
        try
        {
            if (!File.Exists(source)) return false;
            File.WriteAllBytes(target, CryptoService.ReadBytes(source));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Export", $"Couldn't copy {Path.GetFileName(source)}", ex);
            return false;
        }
    }

    // I turn the pen and highlighter strokes into a picture on white, twice the size so it stays sharp
    private static bool SaveDrawings(DiaryStore store, DateOnly day, string target)
    {
        try
        {
            var ink = store.LoadInk(day);
            if (ink == null || ink.Length == 0) return false;
            using var stream = new MemoryStream(ink);
            var strokes = new StrokeCollection(stream);
            if (strokes.Count == 0) return false;
            var bounds = strokes.GetBounds();
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return false;

            const double pad = 12;
            const double scale = 2;
            var width = Math.Min(8000, (int)Math.Ceiling((bounds.Width + pad * 2) * scale));
            var height = Math.Min(8000, (int)Math.Ceiling((bounds.Height + pad * 2) * scale));
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(scale, scale));
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, bounds.Width + pad * 2, bounds.Height + pad * 2));
                context.PushTransform(new TranslateTransform(pad - bounds.X, pad - bounds.Y));
                strokes.Draw(context);
                context.Pop();
                context.Pop();
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(target);
            encoder.Save(file);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Export", $"Couldn't save the drawings for {day:yyyy-MM-dd}", ex);
            return false;
        }
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    private static string Url(string relative, string name) =>
        string.Join("/", relative.Split(Path.DirectorySeparatorChar).Append(name).Select(Uri.EscapeDataString));

    private static string PageStart(List<DateOnly> days)
    {
        var culture = CultureInfo.CurrentCulture;
        var range = days.Count == 0 ? "Nothing written yet"
            : $"{days[0].ToString("d MMMM yyyy", culture)} to {days[^1].ToString("d MMMM yyyy", culture)}, {days.Count} {(days.Count == 1 ? "day" : "days")}";
        var months = days.Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct()
            .Select(m => $"<a href=\"#m{m:yyyy-MM}\">{Html(m.ToString("MMM yyyy", culture))}</a>");
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Kaydence diary</title>
            <style>
            :root { --ink: #2a2438; --muted: #6f6784; --accent: #7b4fb8; --band: #f3edfb; --page: #ffffff; --back: #faf8fd; }
            @media (prefers-color-scheme: dark) { :root { --ink: #ece8f5; --muted: #a59cba; --accent: #b892f0; --band: #2a2238; --page: #1c1826; --back: #14111c; } }
            body { margin: 0; background: var(--back); color: var(--ink); font: 16px/1.6 "Segoe UI", system-ui, sans-serif; }
            main { max-width: 760px; margin: 0 auto; padding: 32px 20px 80px; }
            h1 { margin: 0; font-size: 30px; }
            .range { color: var(--muted); margin: 4px 0 18px; }
            nav { display: flex; flex-wrap: wrap; gap: 6px 12px; font-size: 14px; margin-bottom: 24px; }
            nav a { color: var(--accent); }
            h2 { margin: 40px 0 8px; font-size: 20px; color: var(--accent); }
            article { background: var(--page); border-left: 4px solid var(--accent); border-radius: 10px; padding: 14px 18px; margin: 14px 0; }
            article header { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
            h3 { margin: 0; font-size: 17px; }
            .mood { font-size: 13px; padding: 1px 10px; border-radius: 10px; background: var(--c); color: #1b1b1b; }
            .star { font-size: 13px; color: var(--accent); }
            p { margin: 8px 0; white-space: pre-wrap; }
            img { display: block; max-width: 100%; max-height: 480px; border-radius: 8px; margin: 10px 0; }
            img.ink { background: #fff; }
            .voice { color: var(--muted); font-size: 14px; }
            dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 14px; margin: 12px 0 0; font-size: 14px; }
            dt { font-weight: 700; }
            dd { margin: 0; }
            .problem { color: #c0392b; }
            </style>
            </head>
            <body><main>
            <h1>Kaydence diary</h1>
            <p class="range">{{Html(range)}}</p>
            <nav>{{string.Join(" ", months)}}</nav>

            """;
    }

    private static string ReadMe() =>
        "This is a copy of a Kaydence diary, saved as ordinary files.\r\n\r\n" +
        "Open \"Kaydence diary.html\" in any web browser to read every day with its pictures and voice notes.\r\n\r\n" +
        "Each day also has its own folder, sorted by year, holding a text file with the writing and check-in, " +
        "plus the pictures, voice notes and a picture of any drawings.\r\n\r\n" +
        "These files are NOT encrypted. Anyone who can open this folder can read them, so keep it somewhere private.\r\n";
}
