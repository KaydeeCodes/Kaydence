using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Kaydence.Services;

// I carry my guide inside the app and open it in the web browser whenever I need help
public static class GuideService
{
    public static void Open()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Guide/guide.html", UriKind.Absolute));
            if (resource == null) return;

            // I copy the guide out fresh every time so it always matches the version I'm running
            var folder = Path.Combine(SettingsService.AppFolder, "Guide");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "Kaydence Guide.html");
            using (var input = resource.Stream)
            using (var output = File.Create(path))
            {
                input.CopyTo(output);
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Log.Info("Guide", "Opened the guide in the browser");
        }
        catch (Exception ex)
        {
            Log.Error("Guide", "The guide couldn't open", ex);
            MessageBox.Show($"The guide couldn't open.\n\n{ex.Message}", "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
