using System.Drawing;
using System.Windows.Forms;

namespace SPRecorder.Tray;

/// <summary>
/// Small modeless window for the mark-with-note hotkey. Modeless on purpose: a modal
/// ShowDialog() on the UI thread would block the quick-mark handler and the rest of
/// the app. Enter commits the typed note, Esc commits the marker without a note;
/// either way the marker's timestamp was already captured at the hotkey press.
/// </summary>
internal sealed class MarkNoteInputForm : Form
{
    private readonly TextBox _input;
    private bool _reported;

    public event Action<string?>? Submitted;  // text on commit, null on cancel

    public MarkNoteInputForm(Screen monitor)
    {
        Text = "Marker note";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(420, 92);

        var label = new Label
        {
            Text = "Note for this marker (Enter to save, Esc to skip):",
            AutoSize = true,
            Location = new Point(14, 12),
        };
        _input = new TextBox
        {
            Location = new Point(14, 40),
            Size = new Size(392, 28),
            MaxLength = 200,
            PlaceholderText = "e.g. decision to delay launch",
        };
        _input.KeyDown += OnKeyDown;

        Controls.Add(label);
        Controls.Add(_input);

        var wa = monitor.WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

        FormClosed += (_, _) => Report(null);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)  { e.Handled = true; e.SuppressKeyPress = true; Report(_input.Text); Close(); }
        else if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; Report(null); Close(); }
    }

    /// <summary>Force-commit whatever is typed (used when recording stops while open).</summary>
    public void CommitNow()
    {
        Report(_input.Text);
        Close();
    }

    private void Report(string? note)
    {
        if (_reported) return;
        _reported = true;
        Submitted?.Invoke(note);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _input.Focus();
    }

    public static string PickNoteMonitorDeviceName(
        IReadOnlyList<string> allDeviceNames, string recordedDeviceName)
    {
        foreach (var name in allDeviceNames)
            if (!string.Equals(name, recordedDeviceName, StringComparison.OrdinalIgnoreCase))
                return name;
        return allDeviceNames.Count > 0 ? allDeviceNames[0] : "";
    }
}
