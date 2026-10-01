using System.IO;
using System.IO.Compression;
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
        UseKey(KeyStore.CreateNew(settings.DataFolder, password));
        RecoveryService.Clear(settings);
        SettingsService.Save(settings);
        Run(owner, settings.DataFolder, true);
        UpdateDeviceKey(settings);
        var oldBackups = TidyOldBackups(settings);

        // I go straight to saving the recovery file, then say it's all done in one message
        if (RecoveryService.CreateAndSave(owner, settings))
        {
            MessageBox.Show(owner,
                "Your diary is now locked with your password, and your recovery file is saved.\n\n" +
                "Keep the recovery file somewhere safe and private. It's the only way back in if you forget your password.",
                "Password set", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(owner,
                "Your diary is now locked with your password, but no recovery file was saved.\n\n" +
                "Without one, a forgotten password means your diary can't be opened. You can make one any time in Settings, Privacy and lock.",
                "Password set", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (oldBackups.Count > 0) OfferToDeleteOldBackups(owner, oldBackups);
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

    // I make a fresh locked backup, quietly remove old unlocked ones that hold no days, and hand back any that do hold days
    private static List<string> TidyOldBackups(AppSettings settings)
    {
        var folder = settings.BackupFolderOrDefault;
        try
        {
            if (!Directory.Exists(folder)) return new List<string>();
            var unlocked = Directory.GetFiles(folder, "Kaydence-*.zip").Where(f => !IsLockedBackup(f)).ToList();
            if (unlocked.Count == 0) return new List<string>();

            BackupService.RunNow(settings.DataFolder, folder, settings.BackupKeepDays);
            var withDays = new List<string>();
            foreach (var zip in unlocked)
            {
                if (HoldsDays(zip))
                {
                    withDays.Add(zip);
                    continue;
                }
                File.Delete(zip);
                Log.Info("Crypto", $"Removed {Path.GetFileName(zip)}, an unlocked backup with no days in it");
            }
            Log.Info("Crypto", $"Made a locked backup, {withDays.Count} older unlocked backups still hold days");
            return withDays;
        }
        catch (Exception ex)
        {
            Log.Error("Crypto", "Tidying the old backups went wrong", ex);
            return new List<string>();
        }
    }

    private static bool IsLockedBackup(string zip)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            return archive.Entries.Any(e => e.FullName.Equals("keys.json", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            // I treat a backup I can't open as unlocked, but I never delete it without asking
            return false;
        }
    }

    private static bool HoldsDays(string zip)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            return archive.Entries.Any(e => e.Name.Equals("day.json", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return true;
        }
    }

    // I only ask about old backups that really have diary days in them, because anyone using this PC could still open those
    private static void OfferToDeleteOldBackups(Window owner, List<string> backups)
    {
        var answer = MessageBox.Show(owner,
            $"You have {backups.Count} older {(backups.Count == 1 ? "backup" : "backups")} from before your password was set, so anyone using this PC could still read {(backups.Count == 1 ? "it" : "them")}. " +
            "A new locked backup has just been made.\n\nDelete the old unlocked ones?",
            "Old backups", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        foreach (var zip in backups)
        {
            try
            {
                File.Delete(zip);
            }
            catch (Exception ex)
            {
                Log.Warn("Crypto", $"Couldn't delete {Path.GetFileName(zip)}", ex);
            }
        }
        Log.Info("Crypto", $"Deleted {backups.Count} old unlocked backups");
    }
}
