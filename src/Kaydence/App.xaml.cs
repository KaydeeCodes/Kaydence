using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kaydence.Services;

namespace Kaydence;

public partial class App : Application
{
    private const string InstanceName = "KaydeeCodes.Kaydence.Instance";
    private const string ShowSignalName = "KaydeeCodes.Kaydence.Show";

    // I switch hover tips on and off from settings
    public static bool ShowTooltips { get; set; } = true;

    // I remember that I already typed my password at start up, so the main window doesn't ask again
    public static bool UnlockedAtStart { get; private set; }

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private Action? _showRequested;
    private bool _listening;
    private TrayService? _waitingTray;

    public App()
    {
        // I turn this off so number boxes don't fight me while I'm typing decimals like 7.5
        FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty = false;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // I switch to another folder first when started with --home, so a demo diary keeps its own settings, logs and backups
        var home = HomeFrom(e.Args);
        if (home != null) SettingsService.UseFolder(home);
        var suffix = home == null ? "" : "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(SettingsService.AppFolder.ToLowerInvariant())))[..12];

        // I only ever want one copy open per folder so two windows can't overwrite each other's saves
        _instanceMutex = new Mutex(true, InstanceName + suffix, out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName + suffix);
        if (!isFirst)
        {
            Log.Info("App", "Kaydence is already running, bringing that copy to the front and closing this one");
            _showSignal.Set();
            Shutdown();
            return;
        }

        Log.StartSession(e.Args);
        if (home != null) Log.Info("App", $"Using the folder given with --home: {SettingsService.AppFolder}");
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Crash", $"Unhandled error, closing: {args.IsTerminating}", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background", "A background job failed quietly", args.Exception);
            args.SetObserved();
        };
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
            new ToolTipEventHandler((_, args) =>
            {
                if (!ShowTooltips) args.Handled = true;
            }));

        var settings = SettingsService.Load();
        Log.Info("App", $"Settings loaded: {SettingsService.Summary(settings)}");
        ThemeService.Apply(settings.Theme);

        var launchedAtSignIn = e.Args.Contains(StartupService.StartupArgument);
        var startHidden = launchedAtSignIn && settings.StartHidden;
        Log.Info("App", $"Signed in start: {launchedAtSignIn}, starting hidden: {startHidden}");

        // I need the password before I can read anything if the diary is encrypted, unless Windows is keeping the key
        var encrypted = KeyStore.Exists(settings.DataFolder);
        Log.Info("App", $"Diary encrypted: {encrypted}");
        if (encrypted && !EncryptionService.TryDeviceUnlock(settings))
        {
            if (startHidden)
            {
                // I wait quietly by the clock instead of popping up a password box the moment Windows starts
                WaitForUnlockByTheClock(settings);
                return;
            }
            if (!AskForPassword(settings))
            {
                Log.Info("App", "Unlock window closed without unlocking, shutting down");
                Shutdown();
                return;
            }
        }
        else if (encrypted)
        {
            Log.Info("App", "Opened the encrypted diary with the key Windows keeps");
        }

        OpenDiary(settings, startHidden);
    }

    private bool AskForPassword(AppSettings settings)
    {
        Log.Info("App", "Asking for the password before opening the encrypted diary");
        var mode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new UnlockWindow(settings) { Topmost = true };
        // I pop it in front of everything once, then let it behave like a normal window
        window.ContentRendered += (_, _) => window.Topmost = false;
        var unlocked = window.ShowDialog() == true;
        ShutdownMode = mode;
        if (!unlocked) return false;
        UnlockedAtStart = true;
        Log.Info("App", "Unlocked at start up");
        return true;
    }

    // I sit as an icon by the clock until someone clicks it, then ask for the password and open the diary
    private void WaitForUnlockByTheClock(AppSettings settings)
    {
        Log.Info("App", "Started with Windows while encrypted, waiting by the clock until someone opens Kaydence");
        var mode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var tray = new TrayService { CanLock = false, Visible = true };
        _waitingTray = tray;
        var asking = false;

        void Open()
        {
            if (asking || _waitingTray == null) return;
            asking = true;
            try
            {
                if (!AskForPassword(settings))
                {
                    Log.Info("App", "Unlock cancelled, still waiting by the clock");
                    return;
                }
                _waitingTray = null;
                tray.Dispose();
                ShutdownMode = mode;
                OpenDiary(settings, false);
            }
            finally
            {
                asking = false;
            }
        }

        tray.OpenRequested += Open;
        tray.NotificationClicked += Open;
        tray.ExitRequested += () =>
        {
            Log.Info("App", "Exit chosen while waiting by the clock");
            _waitingTray = null;
            tray.Dispose();
            Shutdown();
        };
        _showRequested = Open;
        ListenForShowSignal();
    }

    private void OpenDiary(AppSettings settings, bool startHidden)
    {
        DiaryStore store;
        try
        {
            using (Log.Time("App", "Opening the diary"))
            {
                store = new DiaryStore(settings.DataFolder);
            }
        }
        catch (Exception ex)
        {
            Log.Error("App", $"Couldn't open the diary folder {settings.DataFolder}", ex);
            MessageBox.Show($"Kaydence couldn't open the diary folder:\n{settings.DataFolder}\n\n{ex.Message}",
                "Kaydence", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (settings.AutoBackup) BackupService.RunDailyInBackground(store.Root, settings.BackupFolderOrDefault, settings.BackupKeepDays);

        // I refresh the sign in entry in case the app has moved folders since it was switched on
        if (settings.StartWithWindows) StartupService.Apply(true);

        MainWindow window;
        using (Log.Time("App", "Building the main window"))
        {
            window = new MainWindow(store, settings, startHidden);
        }
        MainWindow = window;
        if (!startHidden) window.Show();
        _showRequested = window.ShowFromTray;
        ListenForShowSignal();
        Log.Info("App", "Start up finished");
    }

    private static string? HomeFrom(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--home", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length && args[at + 1].Trim().Length > 0 ? args[at + 1].Trim().TrimEnd('\\', '/') : null;
    }

    // I let go of my single copy lock, start a fresh copy of myself, then close this one
    public void Restart()
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // I didn't own the mutex so there's nothing to release
        }
        _instanceMutex?.Dispose();
        _instanceMutex = null;

        Log.Info("App", "Restarting");
        if (Environment.ProcessPath is { } path)
        {
            // I pass my own arguments on so a restart stays in the same folder
            var start = new ProcessStartInfo(path) { UseShellExecute = false };
            foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
            Process.Start(start);
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ClipboardGuard.ClearNow("Kaydence is closing");
        Log.EndSession($"exit code {e.ApplicationExitCode}");
        _waitingTray?.Dispose();
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // I didn't own the mutex so there's nothing to release
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    // I bring the window to the front if Kaydence is opened again while it's already running
    private void ListenForShowSignal()
    {
        if (_listening) return;
        _listening = true;
        var signal = _showSignal!;
        var thread = new Thread(() =>
        {
            while (signal.WaitOne())
            {
                if (Dispatcher.HasShutdownStarted) return;
                Log.Info("App", "Another copy asked me to come to the front");
                Dispatcher.Invoke(() => _showRequested?.Invoke());
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Crash", "Something went wrong but I kept running", e.Exception);

        MessageBox.Show($"Something went wrong, but Kaydence is still running.\n\n{e.Exception.Message}",
            "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
