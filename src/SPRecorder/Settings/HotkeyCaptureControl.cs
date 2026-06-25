using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SPRecorder.Hotkey;

namespace SPRecorder.Settings;

internal sealed class HotkeyCaptureControl : UserControl
{
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

    /// <summary>
    /// Set the displayed hotkey without probing for a conflict.
    /// Use this when initialising the control from the currently-registered hotkey,
    /// otherwise the probe always reports a conflict (the app itself owns the registration).
    /// </summary>
    public void SetInitialHotkey(string spec)
    {
        _hotkey = spec;
        UpdateDisplay();
        _conflictHint.Text = "";
        HasConflict = false;
    }

    /// <summary>
    /// Show (or clear) an externally-determined "inactive" hint for the loaded hotkey,
    /// WITHOUT probing — the authoritative source is the app's own GlobalHotkey.IsRegistered.
    /// Re-probing here would always report a conflict for a combo the app already owns.
    /// </summary>
    public void SetInactiveStatus(bool inactive)
    {
        HasConflict = inactive;
        _conflictHint.Text = inactive ? "  ⚠  Currently inactive — in use by another app" : "";
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasConflict { get; private set; }

    public HotkeyCaptureControl()
    {
        Height = 56;
        TabStop = true;
        Font = SystemFonts.MessageBoxFont;

        _display = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 110, 0),
            Font = new Font("Cascadia Code", 10.5f),
            Cursor = Cursors.Hand,
        };
        _display.Click += (_, _) => StartCapture();

        _badge = new Label
        {
            Text = "click to change",
            BackColor = Color.FromArgb(230, 241, 251),
            ForeColor = Color.FromArgb(0, 90, 158),
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8f),
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
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8.25f),
            Text = "",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0),
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
        const int badgeWidth = 100;
        const int badgeHeight = 22;
        _badge.Bounds = new Rectangle(_display.Right - badgeWidth - 8, _display.Top + 6,
                                      badgeWidth, badgeHeight);
    }

    private void StartCapture()
    {
        _capturing = true;
        _display.BackColor = Color.FromArgb(230, 241, 251);
        _display.Text = "  Press a key combo…   (Esc to cancel)";
        _badge.Visible = false;
        Focus();
    }

    private void StopCapture(bool committed)
    {
        _capturing = false;
        _display.BackColor = Color.White;
        _badge.Visible = true;
        UpdateDisplay();
        if (committed) CheckForConflict();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape)
        {
            StopCapture(committed: false);
            return true;
        }

        if (key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey ||
            key == Keys.LWin || key == Keys.RWin)
        {
            return true;
        }

        var mods = HotkeyModifiers.None;
        if ((keyData & Keys.Control) != 0) mods |= HotkeyModifiers.Ctrl;
        if ((keyData & Keys.Alt)     != 0) mods |= HotkeyModifiers.Alt;
        if ((keyData & Keys.Shift)   != 0) mods |= HotkeyModifiers.Shift;

        if (mods == HotkeyModifiers.None)
            return true;

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
        _display.Text = "  " + _hotkey.Replace("+", "  +  ");
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
                _conflictHint.Text = "  ⚠  In use by another app";
            }
        }
        catch (Exception ex)
        {
            HasConflict = true;
            _conflictHint.Text = "  ⚠  Invalid: " + ex.Message;
        }
    }
}
