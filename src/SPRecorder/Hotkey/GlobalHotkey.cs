using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SPRecorder.Hotkey;

public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 9000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HotkeyWindow _window;
    private readonly bool _registered;

    public bool IsRegistered => _registered;
    public event Action? Pressed;

    public GlobalHotkey(ParsedHotkey hotkey)
    {
        _window = new HotkeyWindow();
        _window.HotkeyPressed += () => Pressed?.Invoke();
        _registered = RegisterHotKey(_window.Handle, HotkeyId, (uint)hotkey.Modifiers, (uint)hotkey.Key);
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_window.Handle, HotkeyId);
        _window.DestroyHandle();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        public event Action? HotkeyPressed;

        public HotkeyWindow() => CreateHandle(new CreateParams());

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) HotkeyPressed?.Invoke();
            base.WndProc(ref m);
        }
    }
}
