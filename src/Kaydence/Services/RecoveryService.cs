using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Kaydence.Services;

// I make and check the recovery file, a spare key for when a password is forgotten
public static class RecoveryService
{
    public const string Extension = ".kaydencekey";

    private sealed class RecoveryFile
    {
        public string App { get; set; } = "Kaydence";
        public int Version { get; set; } = 1;
        public DateTime Created { get; set; }
        public string Key { get; set; } = "";
        public string Note { get; set; } = "This is a Kaydence recovery file. Keep it somewhere safe, like a USB stick. Anyone who has it and a copy of the diary can open it.";
    }

    // I ask the lock file when my diary is encrypted, because that's what the recovery file really has to open
    public static bool HasRecovery(AppSettings settings) => EncryptionService.IsEncrypted(settings)
        ? KeyStore.HasRecovery(settings.DataFolder)
        : !string.IsNullOrEmpty(settings.RecoveryHash);

    // I ask where to save the file, write a brand new key into it, and keep only a hash of that key
    public static bool CreateAndSave(Window owner, AppSettings settings)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save your recovery file, the spare key for your diary",
            FileName = $"Kaydence recovery {DateTime.Now:yyyy-MM-dd}",
            DefaultExt = Extension,
            AddExtension = true,
            Filter = $"Kaydence recovery file|*{Extension}"
        };
        if (dialog.ShowDialog(owner) != true)
        {
            Log.Info("Recovery", "Saving a recovery file was cancelled");
            return false;
        }

        try
        {
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var file = new RecoveryFile { Created = DateTime.Now, Key = key };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
            (settings.RecoveryHash, settings.RecoverySalt) = PasswordService.Create(key);
            if (EncryptionService.IsEncrypted(settings) && CryptoService.Key != null) KeyStore.SetRecovery(settings.DataFolder, CryptoService.Key, key);
            settings.RecoveryCreated = DateTime.Now;
            SettingsService.Save(settings);
            Log.Info("Recovery", "Saved a new recovery file");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Recovery", "The recovery file couldn't be saved", ex);
            MessageBox.Show(owner, $"The recovery file couldn't be saved there.\n\n{ex.Message}", "Kaydence",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public static void ShowSaved(Window owner) =>
        MessageBox.Show(owner, "Your recovery file is saved. Keep it somewhere safe and private, anyone who has it can reset your password.",
            "Recovery file saved", MessageBoxButton.OK, MessageBoxImage.Information);

    // I accept either a whole recovery file or just the key on its own
    public static string? KeyFrom(string text)
    {
        text = text.Trim();
        if (text.StartsWith('{'))
        {
            try
            {
                return JsonSerializer.Deserialize<RecoveryFile>(text)?.Key;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        try
        {
            return Convert.FromBase64String(text).Length == 32 ? text : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // I let someone choose their recovery file, then check it matches this diary
    public static bool TryUse(Window owner, AppSettings settings, out string problem, out string? key)
    {
        problem = "";
        key = null;
        var dialog = new OpenFileDialog
        {
            Title = "Choose your Kaydence recovery file",
            Filter = $"Kaydence recovery file|*{Extension}|All files|*.*"
        };
        if (dialog.ShowDialog(owner) != true) return false;

        string? found;
        try
        {
            found = KeyFrom(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            Log.Warn("Recovery", "The chosen file couldn't be read", ex);
            found = null;
        }
        if (string.IsNullOrWhiteSpace(found))
        {
            problem = "That file isn't a Kaydence recovery file.";
            return false;
        }

        var matches = EncryptionService.IsEncrypted(settings)
            ? KeyStore.UnlockWithRecovery(settings.DataFolder, found) != null
            : PasswordService.Verify(found, settings.RecoveryHash, settings.RecoverySalt);
        Log.Info("Recovery", $"Recovery file checked, matches: {matches}");
        if (matches)
        {
            key = found;
            return true;
        }
        problem = "That recovery file doesn't match this diary. It might be an older one from before a new file was made.";
        return false;
    }

    public static void Clear(AppSettings settings)
    {
        settings.RecoveryHash = null;
        settings.RecoverySalt = null;
        settings.RecoveryCreated = null;
    }

    // I offer to save a recovery file straight after I set a password
    public static void OfferAfterNewPassword(Window owner, AppSettings settings)
    {
        var answer = MessageBox.Show(owner,
            "Would you like to save a recovery file now?\n\nIf you ever forget your password, this file is the only way to set a new one and open your diary. " +
            "Keep it somewhere safe and private, like a USB stick.",
            "Save a recovery file", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        if (CreateAndSave(owner, settings)) ShowSaved(owner);
    }
}
