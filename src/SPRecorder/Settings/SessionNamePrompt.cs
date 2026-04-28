using System.Windows.Forms;

namespace SPRecorder.Settings;

internal sealed class SessionNamePrompt : Form
{
    private readonly TextBox _input;

    public string SessionName => _input.Text;

    public SessionNamePrompt()
    {
        Text = "Name this recording";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new System.Drawing.Size(380, 130);

        var label = new Label
        {
            Text = "Session name (used as folder + filename prefix):",
            AutoSize = true,
            Location = new System.Drawing.Point(16, 14),
        };

        _input = new TextBox
        {
            Location = new System.Drawing.Point(16, 38),
            Size = new System.Drawing.Size(348, 24),
            PlaceholderText = "e.g. Q2 Planning",
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new System.Drawing.Point(196, 80),
            Size = new System.Drawing.Size(80, 28),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new System.Drawing.Point(284, 80),
            Size = new System.Drawing.Size(80, 28),
        };

        Controls.Add(label);
        Controls.Add(_input);
        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var buf = raw.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        return new string(buf).TrimEnd('.', ' ');
    }
}
