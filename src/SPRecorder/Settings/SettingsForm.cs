using System.Drawing;
using System.Windows.Forms;
using SPRecorder.Configuration;
using SPRecorder.Hotkey;

namespace SPRecorder.Settings;

internal sealed class SettingsForm : Form
{
    private readonly AppConfig _initial;
    private readonly bool _isRecording;

    public AppConfig? Result { get; private set; }

    private TextBox _outputDir = null!;
    private TextBox _fileNamePattern = null!;
    private HotkeyCaptureControl _hotkey = null!;
    private ComboBox _bitrate = null!;
    private CheckBox _promptForName = null!;

    private ComboBox _micDevice = null!;
    private ComboBox _renderDevice = null!;
    private Label _audioRecordingHint = null!;

    private CheckBox _mixedEnabled = null!;
    private RadioButton _mixedMono = null!;
    private RadioButton _mixedStereo = null!;
    private ComboBox _mixedSampleRate = null!;
    private GroupBox _mixedDetails = null!;

    public SettingsForm(AppConfig initial, bool isRecording)
    {
        _initial = initial;
        _isRecording = isRecording;

        Text = "SPRecorder — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 440);

        var tabs = new TabControl
        {
            Dock = DockStyle.Top,
            Height = ClientSize.Height - 60,
        };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildAudioTab());
        tabs.TabPages.Add(BuildMixedTab());

        Controls.Add(tabs);
        Controls.Add(BuildFooter());

        ApplyConfigToControls(_initial);
    }

    // ---------- General tab ----------
    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("General") { Padding = new Padding(16) };

        var y = 12;
        page.Controls.Add(MakeLabel("Output directory", 16, y));
        y += 22;
        _outputDir = new TextBox { Location = new Point(16, y), Size = new Size(420, 24) };
        var browse = new Button
        {
            Text = "Browse…",
            Location = new Point(442, y - 1),
            Size = new Size(80, 26),
        };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _outputDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _outputDir.Text = dlg.SelectedPath;
        };
        page.Controls.Add(_outputDir);
        page.Controls.Add(browse);

        y += 36;
        page.Controls.Add(MakeLabel("File name pattern", 16, y));
        y += 22;
        _fileNamePattern = new TextBox { Location = new Point(16, y), Size = new Size(506, 24) };
        page.Controls.Add(_fileNamePattern);
        y += 28;
        page.Controls.Add(MakeHint("Tokens: {timestamp:format}, {track}. {track} = system | mic | mixed.", 16, y));

        y += 28;
        page.Controls.Add(MakeLabel("Global hotkey", 16, y));
        y += 22;
        _hotkey = new HotkeyCaptureControl
        {
            Location = new Point(16, y),
            Size = new Size(506, 50),
        };
        page.Controls.Add(_hotkey);

        y += 60;
        page.Controls.Add(MakeLabel("MP3 bitrate", 16, y));
        y += 22;
        _bitrate = new ComboBox
        {
            Location = new Point(16, y),
            Size = new Size(220, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _bitrate.Items.AddRange(new object[]
        {
            new BitrateOption(64, "64 kbps (smallest)"),
            new BitrateOption(96, "96 kbps (recommended for voice)"),
            new BitrateOption(128, "128 kbps"),
            new BitrateOption(192, "192 kbps (high quality)"),
        });
        _bitrate.DisplayMember = nameof(BitrateOption.Display);

        page.Controls.Add(_bitrate);

        y += 36;
        _promptForName = new CheckBox
        {
            Text = "Ask for session name after recording",
            Location = new Point(16, y),
            Size = new Size(400, 22),
        };
        page.Controls.Add(_promptForName);
        y += 22;
        page.Controls.Add(MakeHint("Saves files in OutputDirectory/<your-name>_<timestamp>/", 36, y));

        return page;
    }

    // ---------- Audio tab ----------
    private TabPage BuildAudioTab()
    {
        var page = new TabPage("Audio") { Padding = new Padding(16) };
        var y = 12;

        page.Controls.Add(MakeLabel("Microphone", 16, y));
        y += 22;
        _micDevice = new ComboBox
        {
            Location = new Point(16, y),
            Size = new Size(506, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(DeviceOption.Display),
        };
        page.Controls.Add(_micDevice);

        y += 36;
        page.Controls.Add(MakeLabel("System audio (loopback)", 16, y));
        y += 22;
        _renderDevice = new ComboBox
        {
            Location = new Point(16, y),
            Size = new Size(506, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(DeviceOption.Display),
        };
        page.Controls.Add(_renderDevice);

        y += 36;
        var refresh = new Button
        {
            Text = "↻ Refresh device list",
            Location = new Point(16, y),
            Size = new Size(180, 28),
        };
        refresh.Click += (_, _) => PopulateAudioDevices();
        page.Controls.Add(refresh);

        y += 40;
        _audioRecordingHint = new Label
        {
            Text = "⚠ Stop recording to change devices.",
            Location = new Point(16, y),
            Size = new Size(506, 22),
            ForeColor = Color.DarkOrange,
            Visible = _isRecording,
        };
        page.Controls.Add(_audioRecordingHint);

        if (_isRecording)
        {
            _micDevice.Enabled = false;
            _renderDevice.Enabled = false;
            refresh.Enabled = false;
        }

        return page;
    }

    // ---------- Mixed tab ----------
    private TabPage BuildMixedTab()
    {
        var page = new TabPage("Mixed file") { Padding = new Padding(16) };

        _mixedEnabled = new CheckBox
        {
            Text = "Generate mixed file after each recording",
            Location = new Point(16, 16),
            Size = new Size(400, 22),
        };
        _mixedEnabled.CheckedChanged += (_, _) => _mixedDetails.Enabled = _mixedEnabled.Checked;
        page.Controls.Add(_mixedEnabled);

        _mixedDetails = new GroupBox
        {
            Text = "Mixed file options",
            Location = new Point(16, 48),
            Size = new Size(506, 200),
        };
        page.Controls.Add(_mixedDetails);

        var y = 26;
        _mixedDetails.Controls.Add(MakeLabel("Format", 16, y));
        y += 22;
        _mixedMono = new RadioButton
        {
            Text = "Mono (recommended for AI)",
            Location = new Point(20, y),
            Size = new Size(280, 22),
        };
        _mixedDetails.Controls.Add(_mixedMono);
        y += 24;
        _mixedStereo = new RadioButton
        {
            Text = "Stereo (L = system, R = mic)",
            Location = new Point(20, y),
            Size = new Size(280, 22),
        };
        _mixedDetails.Controls.Add(_mixedStereo);

        y += 32;
        _mixedDetails.Controls.Add(MakeLabel("Target sample rate", 16, y));
        y += 22;
        _mixedSampleRate = new ComboBox
        {
            Location = new Point(20, y),
            Size = new Size(220, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _mixedSampleRate.Items.AddRange(new object[]
        {
            new SampleRateOption(22050, "22 050 Hz (smallest)"),
            new SampleRateOption(44100, "44 100 Hz (recommended)"),
            new SampleRateOption(48000, "48 000 Hz"),
        });
        _mixedSampleRate.DisplayMember = nameof(SampleRateOption.Display);
        _mixedDetails.Controls.Add(_mixedSampleRate);

        return page;
    }

    private Panel BuildFooter()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Color.FromArgb(246, 246, 246),
        };
        var save = new Button
        {
            Text = "Save",
            Size = new Size(96, 30),
            Location = new Point(panel.Width - 110, 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        save.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 212);
        save.Click += Save_Click;

        var cancel = new Button
        {
            Text = "Cancel",
            Size = new Size(96, 30),
            Location = new Point(panel.Width - 214, 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        cancel.Click += (_, _) => { Result = null; Close(); };

        panel.Controls.Add(save);
        panel.Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
        return panel;
    }

    // ---------- Helpers ----------
    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = Color.FromArgb(51, 51, 51),
    };

    private static Label MakeHint(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = Color.FromArgb(120, 120, 120),
        Font = new Font("Segoe UI", 8.25f),
    };

    private void PopulateAudioDevices()
    {
        _micDevice.Items.Clear();
        _renderDevice.Items.Clear();

        var defaultMic = AudioDeviceEnumerator.GetDefault(NAudio.CoreAudioApi.DataFlow.Capture);
        var defaultRender = AudioDeviceEnumerator.GetDefault(NAudio.CoreAudioApi.DataFlow.Render);

        _micDevice.Items.Add(new DeviceOption("",
            "Default — " + (defaultMic?.FriendlyName ?? "(none)")));
        foreach (var d in AudioDeviceEnumerator.GetCaptureDevices())
            _micDevice.Items.Add(new DeviceOption(d.Id, d.FriendlyName));

        _renderDevice.Items.Add(new DeviceOption("",
            "Default — " + (defaultRender?.FriendlyName ?? "(none)")));
        foreach (var d in AudioDeviceEnumerator.GetRenderDevices())
            _renderDevice.Items.Add(new DeviceOption(d.Id, d.FriendlyName));

        SelectDevice(_micDevice, _initial.MicrophoneDeviceId);
        SelectDevice(_renderDevice, _initial.SystemAudioDeviceId);
    }

    private static void SelectDevice(ComboBox box, string id)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is DeviceOption d && d.Id == id) { box.SelectedIndex = i; return; }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private void ApplyConfigToControls(AppConfig cfg)
    {
        _outputDir.Text = cfg.OutputDirectory;
        _fileNamePattern.Text = cfg.FileNamePattern;
        _hotkey.Hotkey = cfg.Hotkey;

        SelectComboByValue(_bitrate, cfg.Mp3BitrateKbps,
            o => ((BitrateOption)o!).Kbps);

        _promptForName.Checked = cfg.PromptForSessionName;

        PopulateAudioDevices();

        _mixedEnabled.Checked = cfg.MixedFileEnabled;
        _mixedDetails.Enabled = cfg.MixedFileEnabled;
        _mixedMono.Checked   = cfg.MixedFileFormat.Equals("Stereo", StringComparison.OrdinalIgnoreCase) == false;
        _mixedStereo.Checked = cfg.MixedFileFormat.Equals("Stereo", StringComparison.OrdinalIgnoreCase);

        SelectComboByValue(_mixedSampleRate, cfg.MixedFileSampleRate,
            o => ((SampleRateOption)o!).Hz);
    }

    private static void SelectComboByValue<T>(ComboBox combo, T target, Func<object?, T> getter)
        where T : IEquatable<T>
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (getter(combo.Items[i]).Equals(target)) { combo.SelectedIndex = i; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_outputDir.Text))
            errors.Add("Output directory must not be empty.");
        else
        {
            try { Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(_outputDir.Text)); }
            catch (Exception ex) { errors.Add("Output directory cannot be created: " + ex.Message); }
        }

        if (string.IsNullOrWhiteSpace(_fileNamePattern.Text) || !_fileNamePattern.Text.Contains("{track}"))
            errors.Add("File name pattern must contain {track}.");

        try { HotkeyParser.Parse(_hotkey.Hotkey); }
        catch (Exception ex) { errors.Add("Hotkey: " + ex.Message); }

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), "Invalid settings",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var micId = (_micDevice.SelectedItem as DeviceOption)?.Id ?? "";
        var renderId = (_renderDevice.SelectedItem as DeviceOption)?.Id ?? "";
        var bitrate = ((BitrateOption?)_bitrate.SelectedItem)?.Kbps ?? 96;
        var sampleRate = ((SampleRateOption?)_mixedSampleRate.SelectedItem)?.Hz ?? 44100;

        Result = _initial with
        {
            OutputDirectory = _outputDir.Text,
            FileNamePattern = _fileNamePattern.Text,
            Hotkey = _hotkey.Hotkey,
            Mp3BitrateKbps = bitrate,
            MicrophoneDeviceId = micId,
            SystemAudioDeviceId = renderId,
            MixedFileEnabled = _mixedEnabled.Checked,
            MixedFileFormat = _mixedStereo.Checked ? "Stereo" : "Mono",
            MixedFileSampleRate = sampleRate,
            PromptForSessionName = _promptForName.Checked,
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------- Combo item types ----------
    private sealed record BitrateOption(int Kbps, string Display);
    private sealed record SampleRateOption(int Hz, string Display);
    private sealed record DeviceOption(string Id, string Display);
}
