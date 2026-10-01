using System.IO;
using System.Windows;

namespace Kaydence.Services;

// I switch encryption on and off with my password, and finish any job that got interrupted
public static class EncryptionService
{
    // I tell the main window to stop saving while I'm rewriting every file
    public static event Action? Migrating;
    public static event Action? Migrated;

    public static bool IsEncrypted(AppSettings settings) => KeyStore.Exists(settings.DataFolder);

    private static bool HasPassword(AppSettings settings) => settings.PasswordEnabled && !string.IsNullOrEmpty(settings.PasswordHash);

    public static void UseKey(byte[] key)
    {
        CryptoService.Key = key;
        CryptoService.SealWrites = true;
    }

    // I open the diary without asking if Windows is keeping a copy of the key for me
    public static bool TryDeviceUnlock(AppSettings settings)
    {
        var key = DeviceKey.Unprotect(settings.DeviceKey);
        if (key == null)
        {
            Log.Info("Crypto", settings.DeviceKey == null ? "No key kept by Windows, I'll need my password" : "Windows couldn't give back the kept key");
            return false;
        }
        if (!KeyWorks(settings.DataFolder, key))
        {
            Log.Warn("Crypto", "The key Windows kept doesn't open this diary any more, forgetting it");
            settings.DeviceKey = null;
            SettingsService.Save(settings);
            return false;
        }
        UseKey(key);
        return true;
    }

    // I only keep the Windows copy when I've chosen to be asked for my password after a while, not every time
    public static void UpdateDeviceKey(AppSettings settings)
    {
        settings.DeviceKey = IsEncrypted(settings) && CryptoService.Key != null && HasPassword(settings) && settings.LockMode == "AfterDays"
            ? DeviceKey.Protect(CryptoService.Key)
            : null;
        Log.Info("Crypto", settings.DeviceKey != null ? "Windows is keeping a protected copy of the diary key" : "Windows isn't keeping a copy of the diary key");
        SettingsService.Save(settings);
    }

    // I set or change my password, and rewrap the diary key with it if my diary is encrypted
    public static void PasswordChanged(AppSettings settings, string password)
    {
        (settings.PasswordHash, settings.PasswordSalt) = PasswordService.Create(password);
        settings.PasswordEnabled = true;
        settings.LastUsed = DateTime.Now;
        if (IsEncrypted(settings) && CryptoService.Key != null) KeyStore.SetPassword(settings.DataFolder, CryptoService.Key, password);
        SettingsService.Save(settings);
    }

    // I lock every file in my diary with a brand new key that only my password and recovery file can open
    public static void Enable(Window owner, AppSettings settings, string password)
    {
        if (IsEncrypted(settings) && CryptoService.Key != null)
        {
            KeyStore.SetPassword(settings.DataFolder, CryptoService.Key, password);
            UpdateDeviceKey(settings);
            return;
        }

        Log.Info("Crypto", "Switching encryption on");
        var hadOldBackups = Directory.Exists(settings.BackupFolderOrDefault)
                            && Directory.EnumerateFiles(settings.BackupFolderOrDefault, "Kaydence-*.zip").Any();

        UseKey(KeyStore.CreateNew(settings.DataFolder, password));
        RecoveryService.Clear(settings);
        SettingsService.Save(settings);
        Run(owner, settings.DataFolder, true);
        UpdateDeviceKey(settings);

        MessageBox.Show(owner,
            "Your diary is now encrypted. Nobody can read it without your password, even by opening the files directly.\n\n" +
            "Your password is now the only way in, so next you'll save a recovery file. It's your spare key if you ever forget your password. " +
            "Any older recovery file won't work any more.",
            "Kaydence", MessageBoxButton.OK, MessageBoxImage.Information);
        if (!RecoveryService.CreateAndSave(owner, settings))
        {
            MessageBox.Show(owner, "No recovery file was saved. You can make one any time in Settings, Privacy and lock.\n\nWithout one, a forgotten password means your diary can't be opened.",
                "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (hadOldBackups) OfferToReplaceOldBackups(owner, settings);
    }

    // I put every file back to normal and throw the lock away when I switch my password off
    public static void Disable(Window owner, AppSettings settings)
    {
        if (!IsEncrypted(settings) || CryptoService.Key == null) return;
        Log.Info("Crypto", "Switching encryption off");
        CryptoService.SealWrites = false;
        Run(owner, settings.DataFolder, false);
        KeyStore.Delete(settings.DataFolder);
        CryptoService.Forget();
        settings.DeviceKey = null;
        SettingsService.Save(settings);
    }

    // I finish anything a crash or power cut interrupted, so my diary is never left half and half
    public static void FinishPending(Window owner, AppSettings settings)
    {
        if (!IsEncrypted(settings) || CryptoService.Key == null) return;
        if (!HasPassword(settings))
        {
            Log.Warn("Crypto", "The diary is encrypted but there's no password, finishing a switch off that was interrupted");
            Disable(owner, settings);
            return;
        }
        var pending = MigrationWindow.CountPending(settings.DataFolder, true);
        Log.Info("Crypto", $"Files still waiting to be encrypted: {pending}");
        if (pending > 0) Run(owner, settings.DataFolder, true);
    }

    private static void Run(Window owner, string root, bool encrypt)
    {
        Migrating?.Invoke();
        try
        {
            new MigrationWindow(owner, root, encrypt).ShowDialog();
        }
        finally
        {
            Migrated?.Invoke();
        }
    }

    // I check a key really opens this diary before trusting it
    private static bool KeyWorks(string root, byte[] key)
    {
        var sample = Path.Combine(root, "index.json");
        if (!File.Exists(sample) || !CryptoService.IsSealedFile(sample)) return true;
        try
        {
            CryptoService.Open(File.ReadAllBytes(sample), key);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // I make a fresh encrypted backup and offer to delete the old ones that anyone could still read
    private static void OfferToReplaceOldBackups(Window owner, AppSettings settings)
    {
        var answer = MessageBox.Show(owner,
            "Your older backups were made before encryption, so anyone with access to this PC could still open them.\n\n" +
            "Make a new encrypted backup now and delete the old ones?",
            "Old backups", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            var fresh = BackupService.RunNow(settings.DataFolder, settings.BackupFolderOrDefault, settings.BackupKeepDays);
            foreach (var old in Directory.GetFiles(settings.BackupFolderOrDefault, "Kaydence-*.zip"))
            {
                if (!string.Equals(old, fresh, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Crypto", "Replacing the old backups went wrong", ex);
            MessageBox.Show(owner, $"Something went wrong tidying the backups, so some old ones may still be there.\n\n{ex.Message}",
                "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
