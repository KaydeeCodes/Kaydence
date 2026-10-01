using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kaydence.Controls;
using Kaydence.Services;
using Microsoft.Win32;

namespace Kaydence;

// I let someone pick their recovery file, or paste the recovery key if they kept it in a password manager
public sealed class RecoveryPromptWindow : Window
{
    private readonly TextBox _pasted = new() { Tag = "Paste the recovery key here", Height = 38 };
    private readonly TextBlock _error;

    public string? Key { get; private set; }

    public RecoveryPromptWindow(Window owner)
    {
        Owner = owner;
        Title = "Use your recovery file";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(UiKit.Text("Use your recovery file", 16, "Brush.Text", FontWeights.SemiBold));
        var hint = UiKit.Text("Choose the recovery file you saved, or paste the recovery key if you copied it into a password manager.", 12, "Brush.TextMuted", wrap: true);
        hint.Margin = new Thickness(0, 6, 0, 16);
        stack.Children.Add(hint);

        var choose = new Button { Content = "Choose the recovery file...", Style = (Style)FindResource("AccentButton"), Height = 38 };
        choose.Click += (_, _) => ChooseFile();
        stack.Children.Add(choose);

        var or = UiKit.Text("Or paste the recovery key", 12, "Brush.TextMuted");
        or.Margin = new Thickness(2, 18, 0, 6);
        stack.Children.Add(or);
        stack.Children.Add(_pasted);

        _error = UiKit.Text("", 12, "Brush.Text", wrap: true);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x5A));
        _error.Margin = new Thickness(2, 8, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        stack.Children.Add(_error);

        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("SoftButton"), IsCancel = true, Height = 38 };
        var use = new Button { Content = "Use this key", Style = (Style)FindResource("SoftButton"), IsDefault = true, Height = 38 };
        use.Click += (_, _) => UsePasted();
        Grid.SetColumn(use, 2);
        buttons.Children.Add(cancel);
        buttons.Children.Add(use);
        stack.Children.Add(buttons);
        Content = stack;
    }

    private void ChooseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose your Kaydence recovery file",
            Filter = $"Kaydence recovery file|*{RecoveryService.Extension}|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Finish(RecoveryService.KeyFrom(File.ReadAllText(dialog.FileName)), false);
        }
        catch (Exception ex)
        {
            Log.Warn("Recovery", "The chosen file couldn't be read", ex);
            ShowError("That file isn't a Kaydence recovery file.");
        }
    }

    private void UsePasted()
    {
        var text = _pasted.Text.Trim();
        if (text.Length == 0)
        {
            ShowError("Paste the recovery key first, or choose the recovery file.");
            return;
        }
        Finish(RecoveryService.KeyFrom(text), true);
    }

    private void Finish(string? key, bool pasted)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowError(pasted ? "That doesn't look like a Kaydence recovery key." : "That file isn't a Kaydence recovery file.");
            return;
        }
        if (pasted) ClipboardGuard.ClearIfHolding(_pasted.Text);
        Log.Info("Recovery", $"Recovery key given by {(pasted ? "pasting it" : "choosing the file")}");
        Key = key;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
