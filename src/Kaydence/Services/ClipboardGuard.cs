using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Kaydence.Services;

// I copy secret text for a short while only, then wipe it from the clipboard if it's still sitting there
public static class ClipboardGuard
{
    private static DispatcherTimer? _timer;
    private static string? _secret;

    public static bool CopySecret(string text, TimeSpan keepFor)
    {
        try
        {
            // I ask Windows to keep the secret out of clipboard history and cloud clipboard sync
            var data = new DataObject();
            data.SetText(text);
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
            Clipboard.SetDataObject(data, true);
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard", "Couldn't copy, another app is holding the clipboard", ex);
            return false;
        }
        _secret = text;
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = keepFor };
        _timer.Tick += (_, _) => ClearNow("time is up");
        _timer.Start();
        Log.Info("Clipboard", $"Copied a secret, clearing it in {keepFor.TotalSeconds:0} seconds");
        return true;
    }

    // I only clear the clipboard if it still holds the secret, so I never wipe something else copied since
    public static void ClearNow(string why)
    {
        _timer?.Stop();
        _timer = null;
        if (_secret == null) return;
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == _secret)
            {
                Clipboard.Clear();
                Log.Info("Clipboard", $"Cleared the secret from the clipboard ({why})");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard", "Couldn't check the clipboard to clear it", ex);
        }
        _secret = null;
    }

    // I clear a secret that was pasted in from somewhere else once it's been used
    public static void ClearIfHolding(string text)
    {
        try
        {
            if (text.Length > 0 && Clipboard.ContainsText() && Clipboard.GetText().Trim() == text.Trim())
            {
                Clipboard.Clear();
                Log.Info("Clipboard", "Cleared a pasted recovery key from the clipboard");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard", "Couldn't check the clipboard to clear it", ex);
        }
    }
}
