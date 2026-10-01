using System.Windows;

namespace Kaydence.Services;

// I'm the little Kaydence icon in the notification area next to the clock
public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _lockItem;

    public event Action? OpenRequested;
    public event Action? LockRequested;
    public event Action? ExitRequested;
    public event Action? NotificationClicked;

    public TrayService()
    {
        _icon = new System.Windows.Forms.NotifyIcon { Text = "Kaydence", Visible = false };

        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Kaydence.ico"));
        if (resource != null)
        {
            using var stream = resource.Stream;
            _icon.Icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Kaydence", null, (_, _) => OpenRequested?.Invoke());
        _lockItem = new System.Windows.Forms.ToolStripMenuItem("Lock now", null, (_, _) => LockRequested?.Invoke());
        menu.Items.Add(_lockItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit Kaydence", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;

        _icon.BalloonTipClicked += (_, _) =>
        {
            NoteFinished();
            NotificationClicked?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => NoteFinished();

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    private bool _wanted;
    private bool _showingNote;

    public bool Visible
    {
        get => _icon.Visible;
        set
        {
            _wanted = value;
            if (!_showingNote) _icon.Visible = value;
        }
    }

    // I pop up a little Windows notification, even if I normally keep the icon hidden
    public void Notify(string title, string text)
    {
        Log.Info("Tray", $"Showing a notification: {title}");
        _showingNote = true;
        _icon.Visible = true;
        _icon.ShowBalloonTip(10000, title, text, System.Windows.Forms.ToolTipIcon.None);
    }

    private void NoteFinished()
    {
        if (!_showingNote) return;
        _showingNote = false;
        _icon.Visible = _wanted;
    }

    public bool CanLock
    {
        set => _lockItem.Visible = value;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
