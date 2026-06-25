using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SPRecorder.Hotkey;

public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HotkeyWindow _window;
    private readonly bool _registered;
    private readonly int _id;

    public bool IsRegistered => _registered;
    public event Action? Pressed;

    public GlobalHotkey(ParsedHotkey hotkey, int id = 9000)
    {
        _id = id;
        _window = new HotkeyWindow();
        _window.HotkeyPressed += () => Pressed?.Invoke();
        _registered = RegisterHotKey(_window.Handle, _id, (uint)hotkey.Modifiers, (uint)hotkey.Key);
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_window.Handle, _id);
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
