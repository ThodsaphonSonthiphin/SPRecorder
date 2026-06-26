using System.Drawing;
using System.Windows.Forms;

namespace SPRecorder.Tray;

/// <summary>What the user chose when warned that the recorded monitor is in HDR.</summary>
internal enum HdrPromptResult
{
    /// <summary>Turn HDR off for the recording (restore it on stop), then record.</summary>
    DisableHdr,
    /// <summary>Record with HDR left on — colours will be wrong.</summary>
    RecordAnyway,
    /// <summary>Don't start recording.</summary>
    Cancel,
}

/// <summary>
/// Shown when the user starts a recording while the recorded monitor has HDR on.
/// ScreenRecorderLib can't capture HDR correctly, so we offer to turn it off for the
/// duration. Modeled on the app's other small dialogs; no business logic of its own.
/// </summary>
internal sealed class HdrWarningPrompt : Form
{
    public HdrPromptResult Result { get; private set; } = HdrPromptResult.Cancel;

    private HdrWarningPrompt(string monitorLabel)
    {
        Text = "HDR is on";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(440, 170);

        var message = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(408, 90),
            Text =
                $"The monitor you're recording ({monitorLabel}) has HDR turned on. " +
                "The recorder can't capture HDR, so colours will come out washed-out / oversaturated.\n\n" +
                "Turn HDR off for this recording? It will be switched back on when you stop.",
        };
        Controls.Add(message);

        var disable = new Button
        {
            Text = "Disable HDR && record",
            Location = new Point(16, 120),
            Size = new Size(150, 32),
            DialogResult = DialogResult.OK,
        };
        disable.Click += (_, _) => { Result = HdrPromptResult.DisableHdr; Close(); };

        var anyway = new Button
        {
            Text = "Record anyway",
            Location = new Point(176, 120),
            Size = new Size(120, 32),
        };
        anyway.Click += (_, _) => { Result = HdrPromptResult.RecordAnyway; Close(); };

        var cancel = new Button
        {
            Text = "Cancel",
            Location = new Point(306, 120),
            Size = new Size(118, 32),
            DialogResult = DialogResult.Cancel,
        };
        cancel.Click += (_, _) => { Result = HdrPromptResult.Cancel; Close(); };

        Controls.Add(disable);
        Controls.Add(anyway);
        Controls.Add(cancel);
        AcceptButton = disable;
        CancelButton = cancel;
    }

    /// <summary>Show the warning modally and return the user's choice.</summary>
    public static HdrPromptResult Ask(string monitorLabel)
    {
        using var prompt = new HdrWarningPrompt(monitorLabel);
        prompt.ShowDialog();
        return prompt.Result;
    }
}
