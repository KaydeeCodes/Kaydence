using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Kaydence.Services;

// I listen for my shortcut anywhere in Windows, like Win+Shift+K, and bring Kaydence to the front
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4B44;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint KeyK = 0x4B;

    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static readonly IReadOnlyList<(string Value, string Label)> Choices = new List<(string, string)>
    {
        ("Off", "Off"),
        ("WinShiftK", "Win+Shift+K"),
        ("CtrlAltK", "Ctrl+Alt+K"),
        ("CtrlShiftK", "Ctrl+Shift+K")
    };

    public static string Describe(string value) => Choices.FirstOrDefault(c => c.Value == value).Label ?? "Off";

    // I swap to a new shortcut, and tell myself if another app already has it
    public bool Apply(Window window, string choice)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return true;

        if (_source == null)
        {
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WndProc);
        }

        if (_registered)
        {
            UnregisterHotKey(handle, HotkeyId);
            _registered = false;
        }

        var modifiers = choice switch
        {
            "WinShiftK" => ModWin | ModShift,
            "CtrlAltK" => ModControl | ModAlt,
            "CtrlShiftK" => ModControl | ModShift,
            _ => 0u
        };
        if (modifiers == 0) return true;

        _registered = RegisterHotKey(handle, HotkeyId, modifiers | ModNoRepeat, KeyK);
        if (!_registered) Log.Warn("Hotkey", $"Windows refused {Describe(choice)}, error {Marshal.GetLastWin32Error()}");
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Log.Info("Hotkey", "Shortcut pressed");
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source != null && _registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _source?.RemoveHook(WndProc);
        _registered = false;
    }
}
