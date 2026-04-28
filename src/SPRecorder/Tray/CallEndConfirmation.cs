using System.Drawing;
using System.Windows.Forms;

namespace SPRecorder.Tray;

internal sealed class CallEndConfirmation : Form
{
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly Label _countdownLabel;
    private int _secondsLeft = 60;

    public event Action? StopRequested;
    public event Action? KeepRequested;

    public CallEndConfirmation()
    {
        Text = "";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(43, 43, 43);
        ForeColor = Color.White;
        Font = SystemFonts.MessageBoxFont;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(400, 160);
        StartPosition = FormStartPosition.Manual;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - Size.Width - 24, screen.Bottom - Size.Height - 24);

        var title = new Label
        {
            Text = "Call ended — stop recording?",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 18),
        };

        _countdownLabel = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 200),
            Location = new Point(20, 48),
        };

        var keep = new Button
        {
            Text = "Keep recording",
            Size = new Size(150, 34),
            Location = new Point(20, 100),
            BackColor = Color.FromArgb(70, 70, 70),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        keep.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
        keep.Click += (_, _) => { KeepRequested?.Invoke(); Close(); };

        var stop = new Button
        {
            Text = "Stop",
            Size = new Size(110, 34),
            Location = new Point(264, 100),
            BackColor = Color.FromArgb(198, 40, 40),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        stop.FlatAppearance.BorderColor = Color.FromArgb(198, 40, 40);
        stop.Click += (_, _) => { StopRequested?.Invoke(); Close(); };

        Controls.Add(title);
        Controls.Add(_countdownLabel);
        Controls.Add(keep);
        Controls.Add(stop);

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                _countdownTimer.Stop();
                StopRequested?.Invoke();
                Close();
            }
            else
            {
                UpdateCountdown();
            }
        };
        UpdateCountdown();
        _countdownTimer.Start();
    }

    private void UpdateCountdown() =>
        _countdownLabel.Text = $"No response in {_secondsLeft}s = Stop";

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _countdownTimer.Dispose();
        base.Dispose(disposing);
    }
}
