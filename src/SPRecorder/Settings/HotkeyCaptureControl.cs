using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SPRecorder.Hotkey;

namespace SPRecorder.Settings;

internal sealed class HotkeyCaptureControl : UserControl
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Label _display;
    private readonly Label _badge;
    private readonly Label _conflictHint;

    private string _hotkey = "Ctrl+Alt+R";
    private bool _capturing;
    private HotkeyModifiers _capturedMods;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            UpdateDisplay();
            CheckForConflict();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasConflict { get; private set; }

    public HotkeyCaptureControl()
    {
        Height = 50;
        TabStop = true;

        _display = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 90, 0),
            Font = new Font("Cascadia Code", 10f),
            Cursor = Cursors.Hand,
        };
        _display.Click += (_, _) => StartCapture();
        _display.GotFocus += (_, _) => { /* allow focus visible */ };

        _badge = new Label
        {
            Text = "click to change",
            BackColor = Color.FromArgb(230, 241, 251),
            ForeColor = Color.FromArgb(0, 120, 212),
            Font = new Font("Segoe UI", 8f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        _badge.Click += (_, _) => StartCapture();

        _conflictHint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 18,
            ForeColor = Color.DarkOrange,
            Font = new Font("Segoe UI", 8.25f),
            Text = "",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(_display);
        Controls.Add(_badge);
        Controls.Add(_conflictHint);
        Resize += (_, _) => LayoutBadge();

        UpdateDisplay();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LayoutBadge();
    }

    private void LayoutBadge()
    {
        const int badgeWidth = 96;
        const int badgeHeight = 20;
        _badge.Bounds = new Rectangle(_display.Right - badgeWidth - 6, _display.Top + 5,
                                      badgeWidth, badgeHeight);
    }

    private void StartCapture()
    {
        _capturing = true;
        _capturedMods = HotkeyModifiers.None;
        _display.BackColor = Color.FromArgb(230, 241, 251);
        _display.Text = "Press a key combo… (Esc to cancel)";
        _badge.Visible = false;
        Focus();
        KeyPreview(true);
    }

    private void StopCapture(bool committed)
    {
        _capturing = false;
        _display.BackColor = Color.White;
        _badge.Visible = true;
        UpdateDisplay();
        if (committed) CheckForConflict();
    }

    // Use a top-level form-attached key handler. Since UserControl can receive key events
    // when focused, override ProcessCmdKey to intercept all keys (incl. Tab/Esc/etc.).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape)
        {
            StopCapture(committed: false);
            return true;
        }

        // Skip lone modifier presses
        if (key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey ||
            key == Keys.LWin || key == Keys.RWin)
        {
            // Update modifier capture from the modifier bits in keyData
            if ((keyData & Keys.Control) != 0) _capturedMods |= HotkeyModifiers.Ctrl;
            if ((keyData & Keys.Alt)     != 0) _capturedMods |= HotkeyModifiers.Alt;
            if ((keyData & Keys.Shift)   != 0) _capturedMods |= HotkeyModifiers.Shift;
            return true;
        }

        // A real key arrived. Read modifiers from keyData (more reliable than tracked).
        var mods = HotkeyModifiers.None;
        if ((keyData & Keys.Control) != 0) mods |= HotkeyModifiers.Ctrl;
        if ((keyData & Keys.Alt)     != 0) mods |= HotkeyModifiers.Alt;
        if ((keyData & Keys.Shift)   != 0) mods |= HotkeyModifiers.Shift;

        if (mods == HotkeyModifiers.None)
        {
            // Reject bare keys without modifiers — too easy to trigger by accident.
            return true;
        }

        _hotkey = FormatHotkey(mods, key);
        StopCapture(committed: true);
        return true;
    }

    private static string FormatHotkey(HotkeyModifiers mods, Keys key)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(HotkeyModifiers.Ctrl))  parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyModifiers.Alt))   parts.Add("Alt");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyModifiers.Win))   parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private void UpdateDisplay()
    {
        if (_capturing) return;
        _display.Text = "  " + _hotkey.Replace("+", " + ");
    }

    private void CheckForConflict()
    {
        try
        {
            var parsed = HotkeyParser.Parse(_hotkey);
            const int probeId = 9999;
            bool ok = RegisterHotKey(Handle, probeId, (uint)parsed.Modifiers, (uint)parsed.Key);
            if (ok)
            {
                UnregisterHotKey(Handle, probeId);
                HasConflict = false;
                _conflictHint.Text = "";
            }
            else
            {
                HasConflict = true;
                _conflictHint.Text = "  ⚠ In use by another app";
            }
        }
        catch (Exception ex)
        {
            HasConflict = true;
            _conflictHint.Text = "  ⚠ Invalid: " + ex.Message;
        }
    }

    // Form parents call this so they don't own KeyPreview; we self-manage focus.
    private void KeyPreview(bool _) { /* placeholder for clarity; not currently needed */ }
}
