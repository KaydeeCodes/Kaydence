using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Kaydence.Models;
using Kaydence.Services;
using Microsoft.Win32;

namespace Kaydence.Controls;

// I'm a voice note on the page: play, pause, and click the waveform to jump about
public partial class PageVoiceItem : UserControl, IPageElement
{
    public const int Bars = 48;
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";

    private readonly string _path;
    private readonly double _seconds;
    private readonly byte[] _wave;
    private VoicePlayer? _player;

    public event Action? Changed;
    public event Action<UIElement>? RemoveRequested;
    public event Action<UIElement, bool>? OrderRequested;

    public PageVoiceItem(string path, string fileName, double seconds, byte[]? wave)
    {
        InitializeComponent();
        _path = path;
        FileName = fileName;
        _seconds = seconds;
        _wave = wave is { Length: Bars } ? wave : Enumerable.Repeat((byte)90, Bars).ToArray();

        PlayButton.Content = PlayGlyph;
        foreach (var height in _wave)
        {
            var bar = new Rectangle { Width = 3, Height = 3 + height / 255.0 * 26, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(0, 0, 2, 0) };
            WaveBars.Children.Add(bar);
        }
        ShowProgress(0);

        MoveThumb.DragStarted += (_, _) => Keyboard.Focus(this);
        MoveThumb.DragDelta += (_, e) =>
        {
            Canvas.SetLeft(this, Math.Max(0, Canvas.GetLeft(this) + e.HorizontalChange));
            Canvas.SetTop(this, Math.Max(0, Canvas.GetTop(this) + e.VerticalChange));
        };
        MoveThumb.DragCompleted += (_, _) => Changed?.Invoke();

        PreviewMouseLeftButtonDown += (_, _) => Keyboard.Focus(this);
        MouseEnter += (_, _) => UpdateChrome();
        MouseLeave += (_, _) => UpdateChrome();
        IsKeyboardFocusWithinChanged += (_, _) => UpdateChrome();
        KeyDown += OnKeyDown;
        Unloaded += (_, _) => _player?.Stop();
    }

    public string FileName { get; }

    public bool IsBlank => false;

    public string PlainText => $"(Voice note, {FormatTime(_seconds)})";

    public PageItem ToItem(double x, double y, int z) => new()
    {
        Kind = "voice",
        X = x,
        Y = y,
        Z = z,
        Width = double.IsNaN(Width) ? 330 : Width,
        Audio = FileName,
        Duration = Math.Round(_seconds, 1),
        Wave = Convert.ToBase64String(_wave)
    };

    public static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    public void TogglePlay()
    {
        if (!EnsurePlayer()) return;
        if (_player!.IsPlaying)
        {
            _player.Pause();
            PlayButton.Content = PlayGlyph;
            return;
        }
        if (_player.Play() is { } problem) Warn(problem);
        else PlayButton.Content = PauseGlyph;
    }

    // I load the sound only when I first press play, unsealing it in memory if my diary is encrypted
    private bool EnsurePlayer()
    {
        if (_player != null) return true;
        try
        {
            _player = new VoicePlayer(CryptoService.ReadBytes(_path));
        }
        catch (Exception ex)
        {
            Log.Error("Audio", $"Couldn't open voice note {FileName}", ex);
            Warn($"This voice note couldn't be opened.\n\n{ex.Message}");
            return false;
        }
        _player.Progress += ShowProgress;
        _player.Finished += () => PlayButton.Content = PlayGlyph;
        return true;
    }

    private void ShowProgress(double fraction)
    {
        var played = (int)Math.Round(fraction * Bars);
        for (var i = 0; i < WaveBars.Children.Count; i++)
            ((Rectangle)WaveBars.Children[i]).SetResourceReference(Shape.FillProperty, i < played ? "Brush.Accent" : "Brush.TextFaint");
        TimeText.Text = fraction > 0 ? FormatTime(fraction * _seconds) : FormatTime(_seconds);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.Focus(this);
        TogglePlay();
    }

    private void Wave_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!EnsurePlayer()) return;
        var fraction = e.GetPosition(WaveBars).X / Math.Max(1, WaveBars.ActualWidth);
        if (_player!.Play(Math.Clamp(fraction, 0, 1)) is { } problem) Warn(problem);
        else PlayButton.Content = PauseGlyph;
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            _player?.Stop();
            RemoveRequested?.Invoke(this);
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            TogglePlay();
            e.Handled = true;
        }
    }

    private void UpdateChrome()
    {
        var active = IsMouseOver || IsKeyboardFocusWithin;
        MoveThumb.Opacity = active ? 1 : 0;
        Frame.Opacity = IsKeyboardFocusWithin ? 1 : 0;
    }

    private void Warn(string message) =>
        MessageBox.Show(Window.GetWindow(this)!, message, "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);

    // I can save a normal copy of a voice note, for example to send to my doctor
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save a copy of this voice note",
            FileName = $"Kaydence voice note {DateTime.Now:yyyy-MM-dd HHmm}",
            DefaultExt = ".wav",
            Filter = "Sound file|*.wav"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, CryptoService.ReadBytes(_path));
        }
        catch (Exception ex)
        {
            Warn($"The copy couldn't be saved there.\n\n{ex.Message}");
        }
    }

    private void Front_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, true);

    private void Back_Click(object sender, RoutedEventArgs e) => OrderRequested?.Invoke(this, false);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        _player?.Stop();
        RemoveRequested?.Invoke(this);
    }
}
