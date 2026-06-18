using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SPRecorder.Overlay;

/// <summary>
/// A transparent, topmost, click-through caption pinned to a chosen monitor that
/// shows the keys being pressed (KeyCastr-style). Fed by a global low-level
/// keyboard hook. Active only while a screen recording with keystrokes is on; it
/// never persists keystrokes. Because it is layered + topmost and does NOT set
/// WDA_EXCLUDEFROMCAPTURE, the screen recorder captures it into the video.
/// </summary>
public sealed class InputHighlightOverlay : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly LowLevelKeyboardProc _proc;   // kept alive for the hook
    private IntPtr _hookId = IntPtr.Zero;
    private CaptionForm? _form;
    private readonly System.Windows.Forms.Timer _fade = new() { Interval = 250 };
    private DateTime _lastKey = DateTime.MinValue;

    public InputHighlightOverlay() => _proc = HookCallback;

    public void Show(Screen monitor)
    {
        System.Diagnostics.Debug.Assert(System.Windows.Forms.Application.MessageLoop,
            "InputHighlightOverlay.Show must be called on the UI thread.");

        _form = new CaptionForm();
        _form.Bounds = monitor.Bounds;           // virtual-screen coords (may be negative)
        _form.Show();
        _form.DpiChanged += (_, _) => { if (_form != null) _form.Bounds = monitor.Bounds; };

        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(), "Failed to install keyboard hook for the key caster.");

        _fade.Tick += (_, _) =>
        {
            if (_form != null && (DateTime.UtcNow - _lastKey).TotalMilliseconds > 1500)
                _form.SetText("");
        };
        _fade.Start();
    }

    public void HideAndDispose()
    {
        _fade.Stop();
        if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
        _form?.Close();
        _form?.Dispose();
        _form = null;
    }

    public void Dispose()
    {
        HideAndDispose();
        _fade.Dispose();
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var module = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            var label = KeyLabels.Describe((Keys)vk);
            _lastKey = DateTime.UtcNow;
            _form?.AppendKey(label);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ---- transparent click-through caption window ----
    private sealed class CaptionForm : Form
    {
        private readonly Label _label;

        public CaptionForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = System.Drawing.Color.Magenta;       // chroma key
            TransparencyKey = System.Drawing.Color.Magenta; // make background see-through
            _label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 120,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(40, 40, 40),
                Font = new System.Drawing.Font("Segoe UI", 28f, System.Drawing.FontStyle.Bold),
            };
            Controls.Add(_label);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void AppendKey(string label)
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                var existing = _label.Text;
                var combined = string.IsNullOrEmpty(existing) ? label : existing + "  " + label;
                if (combined.Length > 40) combined = combined[^40..];
                _label.Text = combined;
            });
        }

        public void SetText(string text)
        {
            if (IsDisposed) return;
            BeginInvoke(() => _label.Text = text);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

internal static class KeyLabels
{
    public static string Describe(Keys key)
    {
        var mods = "";
        if ((Control.ModifierKeys & Keys.Control) != 0) mods += "Ctrl+";
        if ((Control.ModifierKeys & Keys.Alt) != 0)     mods += "Alt+";
        if ((Control.ModifierKeys & Keys.Shift) != 0)   mods += "Shift+";

        var name = key switch
        {
            Keys.Space => "Space",
            Keys.Return => "Enter",
            Keys.Escape => "Esc",
            Keys.Tab => "Tab",
            Keys.Back => "Backspace",
            Keys.Delete => "Del",
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.A and <= Keys.Z => key.ToString(),
            >= Keys.F1 and <= Keys.F12 => key.ToString(),
            Keys.Up => "↑", Keys.Down => "↓", Keys.Left => "←", Keys.Right => "→",
            Keys.LControlKey or Keys.RControlKey or Keys.ControlKey => "",
            Keys.LMenu or Keys.RMenu or Keys.Menu => "",
            Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey => "",
            Keys.LWin or Keys.RWin => "",
            _ => key.ToString(),
        };
        // For a bare modifier press, show just the modifier chord (drop trailing +).
        if (name.Length == 0) return mods.TrimEnd('+');
        return mods + name;
    }
}
