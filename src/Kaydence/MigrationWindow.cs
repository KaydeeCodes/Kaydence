using System.IO;
using System.Windows;
using System.Windows.Controls;
using Kaydence.Controls;
using Kaydence.Services;

namespace Kaydence;

// I show progress while every file in my diary is locked or unlocked, and I can't be closed halfway
public sealed class MigrationWindow : Window
{
    private readonly string _root;
    private readonly bool _encrypt;
    private readonly TextBlock _status;
    private readonly Border _fill;
    private readonly Border _track;
    private bool _done;

    public MigrationWindow(Window owner, string root, bool encrypt)
    {
        Owner = owner.IsVisible ? owner : null;
        _root = root;
        _encrypt = encrypt;
        Title = encrypt ? "Encrypting your diary" : "Decrypting your diary";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ShowInTaskbar = Owner == null;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        var stack = new StackPanel { Margin = new Thickness(26, 24, 26, 26) };
        stack.Children.Add(UiKit.Text(encrypt ? "Locking every page with your password" : "Putting your diary back to normal files", 15, "Brush.Text", FontWeights.SemiBold, wrap: true));
        var note = UiKit.Text("Please don't close Kaydence or turn off the PC. If it does get interrupted, Kaydence carries on next time.", 12, "Brush.TextMuted", wrap: true);
        note.Margin = new Thickness(0, 6, 0, 16);
        stack.Children.Add(note);

        _track = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
        _track.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
        _fill = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _fill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        var bar = new Grid();
        bar.Children.Add(_track);
        bar.Children.Add(_fill);
        stack.Children.Add(bar);

        _status = UiKit.Text("Getting ready...", 12, "Brush.TextMuted");
        _status.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_status);
        Content = stack;

        Closing += (_, e) => e.Cancel = !_done;
        Loaded += async (_, _) =>
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                _fill.Width = p.Total == 0 ? _track.ActualWidth : _track.ActualWidth * p.Done / p.Total;
                _status.Text = $"{p.Done} of {p.Total} files";
            });
            string? problem = null;
            await Task.Run(() => problem = Process(_root, _encrypt, progress));
            _done = true;
            if (problem != null)
            {
                MessageBox.Show(this, $"A few files couldn't be changed this time, Kaydence will try them again next time it opens.\n\n{problem}",
                    "Kaydence", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Close();
        };
    }

    public static int CountPending(string root, bool encrypt) => Files(root).Count(f => CryptoService.IsSealedFile(f) != encrypt);

    // I skip the lock file itself and anything half written
    private static IEnumerable<string> Files(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        var keys = KeyStore.PathFor(root);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f, keys, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Process(string root, bool encrypt, IProgress<(int, int)> progress)
    {
        var key = CryptoService.Key;
        if (key == null) return "The diary key isn't available.";
        var todo = Files(root).Where(f => CryptoService.IsSealedFile(f) != encrypt).ToList();
        Log.Info("Crypto", $"{(encrypt ? "Encrypting" : "Decrypting")} {todo.Count} files");
        var failed = 0;
        string? problem = null;
        for (var i = 0; i < todo.Count; i++)
        {
            var file = todo[i];
            try
            {
                var data = File.ReadAllBytes(file);
                var changed = encrypt ? CryptoService.Seal(data, key) : CryptoService.Open(data, key);
                var temp = file + ".tmp";
                File.WriteAllBytes(temp, changed);
                File.Move(temp, file, true);
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error("Crypto", $"Couldn't {(encrypt ? "encrypt" : "decrypt")} {Path.GetFileName(file)}", ex);
                problem ??= $"{Path.GetFileName(file)}: {ex.Message}";
            }
            progress.Report((i + 1, todo.Count));
        }
        progress.Report((todo.Count, todo.Count));
        Log.Info("Crypto", $"Finished: {todo.Count - failed} done, {failed} failed");
        return problem;
    }
}
