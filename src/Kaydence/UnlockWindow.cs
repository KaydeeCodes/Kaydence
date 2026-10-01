using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kaydence.Controls;
using Kaydence.Services;

namespace Kaydence;

// I ask for my password before anything else when my diary is encrypted, because nothing can be read until I do
public sealed class UnlockWindow : Window
{
    private readonly AppSettings _settings;
    private readonly PasswordBox _password = new() { Height = 42, Padding = new Thickness(12, 0, 12, 0) };
    private readonly TextBlock _error;

    public UnlockWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "Kaydence";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Kaydence.ico"));
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        var stack = new StackPanel { Margin = new Thickness(34, 30, 34, 28) };
        stack.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Kaydence.png")),
            Width = 64,
            Height = 64
        });
        var title = UiKit.Text("Welcome back", 24, "Brush.Text", FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 16, 0, 2);
        stack.Children.Add(title);
        var sub = UiKit.Text("Your diary is encrypted and locked", 13, "Brush.TextMuted");
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.Margin = new Thickness(0, 0, 0, 20);
        stack.Children.Add(sub);

        _password.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            TryPassword();
            e.Handled = true;
        };
        stack.Children.Add(_password);

        _error = UiKit.Text("", 12, "Brush.Text", wrap: true);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x5A));
        _error.Margin = new Thickness(2, 8, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        stack.Children.Add(_error);

        var unlock = new Button { Content = "Unlock", Style = (Style)FindResource("AccentButton"), Height = 42, Margin = new Thickness(0, 14, 0, 0) };
        unlock.Click += (_, _) => TryPassword();
        stack.Children.Add(unlock);

        var recover = new Button
        {
            Content = "Forgot your password? Use your recovery file",
            Style = (Style)FindResource("LinkButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };
        recover.Click += (_, _) => TryRecovery();
        stack.Children.Add(recover);

        Content = stack;
        Loaded += (_, _) =>
        {
            Activate();
            _password.Focus();
        };
    }

    private void TryPassword()
    {
        Cursor = Cursors.Wait;
        var key = KeyStore.UnlockWithPassword(_settings.DataFolder, _password.Password);
        Cursor = null;
        if (key == null)
        {
            ShowError("That password isn't right, try again");
            _password.SelectAll();
            return;
        }

        // I keep the settings lock in step with the diary, in case I restored a backup made with an older password
        if (!PasswordService.Verify(_password.Password, _settings.PasswordHash, _settings.PasswordSalt))
        {
            (_settings.PasswordHash, _settings.PasswordSalt) = PasswordService.Create(_password.Password);
            _settings.PasswordEnabled = true;
        }
        Finish(key);
    }

    private void TryRecovery()
    {
        if (!RecoveryService.TryUse(this, _settings, out var problem, out var recoveryKey) || recoveryKey == null)
        {
            if (problem.Length > 0) ShowError(problem);
            return;
        }
        var key = KeyStore.UnlockWithRecovery(_settings.DataFolder, recoveryKey);
        if (key == null)
        {
            ShowError("That recovery file doesn't open this diary.");
            return;
        }

        var dialog = new PasswordDialog(this, _settings, true);
        if (dialog.ShowDialog() != true || dialog.NewPassword == null) return;
        EncryptionService.UseKey(key);
        EncryptionService.PasswordChanged(_settings, dialog.NewPassword);
        MessageBox.Show(this, "Your new password is set. Your recovery file still works, so keep it somewhere safe.",
            "Kaydence", MessageBoxButton.OK, MessageBoxImage.Information);
        Finish(key);
    }

    private void Finish(byte[] key)
    {
        EncryptionService.UseKey(key);
        _settings.LastUsed = DateTime.Now;
        EncryptionService.UpdateDeviceKey(_settings);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
