using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Kaydence.Controls;
using Kaydence.Services;

namespace Kaydence;

// I record a voice note like WhatsApp does: a red dot, a timer, live bars, and a five minute limit
public sealed class VoiceRecorderWindow : Window
{
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);
    private const int LiveBars = 44;

    private readonly VoiceRecorder _recorder = new();
    private readonly TextBlock _time;
    private readonly StackPanel _bars = new() { Orientation = Orientation.Horizontal, Height = 44, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _hint;
    private readonly Button _privacy;
    private bool _finished;

    public VoiceRecorderWindow(Window owner)
    {
        Owner = owner;
        Title = "Voice note";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };

        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), VerticalAlignment = VerticalAlignment.Center };
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        top.Children.Add(dot);
        _time = UiKit.Text("0:00", 28, "Brush.Text", FontWeights.SemiBold);
        _time.Margin = new Thickness(12, 0, 0, 0);
        top.Children.Add(_time);
        var limit = UiKit.Text(" / 5:00", 15, "Brush.TextFaint");
        limit.VerticalAlignment = VerticalAlignment.Bottom;
        limit.Margin = new Thickness(4, 0, 0, 6);
        top.Children.Add(limit);
        stack.Children.Add(top);

        for (var i = 0; i < LiveBars; i++)
        {
            var bar = new Rectangle { Width = 4, Height = 3, RadiusX = 2, RadiusY = 2, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.SetResourceReference(Shape.FillProperty, "Brush.Accent");
            _bars.Children.Add(bar);
        }
        _bars.Margin = new Thickness(0, 14, 0, 6);
        stack.Children.Add(_bars);

        _hint = UiKit.Text("Recording from your default microphone", 12, "Brush.TextMuted", wrap: true);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.TextAlignment = TextAlignment.Center;
        stack.Children.Add(_hint);

        _privacy = new Button
        {
            Content = "Open Windows microphone settings",
            Style = (Style)FindResource("LinkButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _privacy.Click += (_, _) => Links.Open("ms-settings:privacy-microphone");
        stack.Children.Add(_privacy);

        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("SoftButton"), IsCancel = true, Height = 40 };
        var save = new Button { Content = "Stop and save", Style = (Style)FindResource("AccentButton"), IsDefault = true, Height = 40 };
        save.Click += (_, _) => Finish(true);
        Grid.SetColumn(save, 2);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        stack.Children.Add(buttons);
        Content = stack;

        _recorder.Level += OnLevel;
        Loaded += (_, _) => Begin();
        Closed += (_, _) =>
        {
            if (!_finished) _recorder.Dispose();
        };
    }

    public byte[]? Audio { get; private set; }
    public double Seconds { get; private set; }
    public byte[] Waveform { get; private set; } = Array.Empty<byte>();

    private void Begin()
    {
        var problem = _recorder.Start();
        if (problem == null) return;
        Log.Warn("Audio", $"Recorder couldn't start: {problem}");
        _hint.Text = problem + " If the microphone is blocked, switch on \"Let desktop apps access your microphone\" in Windows settings.";
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _privacy.Visibility = Visibility.Visible;
        _time.Text = "0:00";
    }

    private void OnLevel(double level)
    {
        // I scroll the live bars along like a heartbeat
        var levels = _recorder.Levels;
        for (var i = 0; i < LiveBars; i++)
        {
            var index = levels.Count - LiveBars + i;
            var value = index >= 0 ? levels[index] : 0;
            ((Rectangle)_bars.Children[i]).Height = 3 + Math.Sqrt(value) * 38;
        }

        var elapsed = _recorder.Elapsed;
        _time.Text = elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);

        // I warn myself if I've heard nothing at all for a couple of seconds, that's usually the privacy switch
        if (levels.Count == 25 && levels.All(l => l < 0.003))
        {
            Log.Warn("Audio", "Nothing but silence for the first 2.5 seconds");
            _hint.Text = "Kaydence can't hear anything. Is the microphone muted, or blocked in Windows privacy settings?";
            _privacy.Visibility = Visibility.Visible;
        }
        // I finish after this update has run, never from inside it
        if (elapsed >= Limit) Dispatcher.InvokeAsync(() => Finish(true));
    }

    private void Finish(bool keep)
    {
        if (_finished) return;
        _finished = true;
        var wav = _recorder.Stop();
        Log.Info("Audio", $"Recorder finished, keep: {keep}, {_recorder.Levels.Count / 10.0:0.0} seconds");
        if (keep && _recorder.Levels.Count >= 5)
        {
            Audio = wav;
            Seconds = Math.Min(Limit.TotalSeconds, _recorder.Levels.Count / 10.0);
            Waveform = Wav.Shape(_recorder.Levels, PageVoiceItem.Bars);
            DialogResult = true;
        }
        else
        {
            DialogResult = false;
        }
    }
}
