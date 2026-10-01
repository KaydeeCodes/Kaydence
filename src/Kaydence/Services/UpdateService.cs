using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Kaydence.Services;

// I ask GitHub if there's a newer Kaydence, the only thing I send is a request for the latest version number
public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public sealed record Release(Version Version, string Name, string Url, string Notes);

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kaydence/" + Current.ToString(3));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<Release?> GetLatestAsync()
    {
        var url = $"https://api.github.com/repos/{Links.GitHubOwner}/{Links.GitHubRepo}/releases/latest";
        using var response = await Http.GetAsync(url);
        Log.Info("Updates", $"GitHub answered {(int)response.StatusCode} {response.StatusCode}");
        if (!response.IsSuccessStatusCode) return null;

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
        {
            Log.Warn("Updates", $"The latest release tag '{tag}' isn't a version number");
            return null;
        }
        Log.Info("Updates", $"Latest release is {version}, I have {Current}");

        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
        return new Release(version, string.IsNullOrWhiteSpace(name) ? $"Kaydence {version.ToString(3)}" : name!, page ?? Links.Releases, notes ?? "");
    }

    private static bool IsNewer(Version latest)
    {
        var current = Current;
        return new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build))
               > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
    }

    // I check quietly once a day when Kaydence starts, and only speak up if there's something new
    public static async void CheckInBackground(Window owner, AppSettings settings)
    {
        if (!settings.CheckForUpdates) return;
        if (settings.LastUpdateCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(20)) return;
        settings.LastUpdateCheck = DateTime.Now;
        SettingsService.Save(settings);
        try
        {
            var release = await GetLatestAsync();
            if (release == null || !IsNewer(release.Version)) return;
            if (settings.SkippedVersion == release.Version.ToString(3)) return;
            Offer(owner, settings, release);
        }
        catch (Exception ex)
        {
            // I stay quiet if I'm offline, this was only a friendly check
            Log.Warn("Updates", "The daily update check didn't work", ex);
        }
    }

    // I check straight away when I press the button in settings, and always tell myself the answer
    public static async void CheckNow(Window owner, AppSettings settings)
    {
        try
        {
            var release = await GetLatestAsync();
            settings.LastUpdateCheck = DateTime.Now;
            SettingsService.Save(settings);
            if (release != null && IsNewer(release.Version))
            {
                Offer(owner, settings, release);
                return;
            }
            MessageBox.Show(owner, $"You're up to date. This is Kaydence {Current.ToString(3)}.", "Kaydence",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Check now didn't work", ex);
            MessageBox.Show(owner, "Kaydence couldn't reach GitHub to check. Are you online?", "Kaydence",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void Offer(Window owner, AppSettings settings, Release release)
    {
        var notes = release.Notes.Length > 600 ? release.Notes[..600].TrimEnd() + "..." : release.Notes;
        var answer = MessageBox.Show(owner,
            $"{release.Name} is out. You have {Current.ToString(3)}.\n\n{notes}\n\nOpen the download page now?\n\nYes opens it, No skips this version, Cancel reminds you later.",
            "A new Kaydence is ready", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) Links.Open(release.Url);
        else if (answer == MessageBoxResult.No)
        {
            settings.SkippedVersion = release.Version.ToString(3);
            SettingsService.Save(settings);
        }
    }
}
