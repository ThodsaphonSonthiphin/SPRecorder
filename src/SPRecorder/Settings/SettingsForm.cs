using System.Drawing;
using System.Windows.Forms;
using SPRecorder.Configuration;
using SPRecorder.Hotkey;

namespace SPRecorder.Settings;

internal sealed class SettingsForm : Form
{
    private const int TabPad = 28;
    private const int FieldGap = 18;
    private const int LabelToInput = 6;
    private const int InputHeight = 28;
    private const int InputWidth = 600;
    private const int FormWidth = 720;
    private const int FormHeight = 640;
    private const int FooterHeight = 76;

    private readonly AppConfig _initial;
    private readonly bool _isRecording;

    public AppConfig? Result { get; private set; }

    private TextBox _outputDir = null!;
    private TextBox _fileNamePattern = null!;
    private HotkeyCaptureControl _hotkey = null!;
    private ComboBox _bitrate = null!;
    private CheckBox _promptForName = null!;
    private CheckBox _autoDetectCalls = null!;

    private ComboBox _micDevice = null!;
    private ComboBox _renderDevice = null!;
    private Label _audioRecordingHint = null!;

    private CheckBox _mixedEnabled = null!;
    private RadioButton _mixedMono = null!;
    private RadioButton _mixedStereo = null!;
    private ComboBox _mixedSampleRate = null!;
    private GroupBox _mixedDetails = null!;

    private RadioButton _splitNone = null!;
    private RadioButton _splitByTime = null!;
    private RadioButton _splitBySize = null!;
    private NumericUpDown _splitMinutes = null!;
    private NumericUpDown _splitSizeMb = null!;
    private Label _splitSizeHint = null!;
    private CheckBox _splitSystem = null!;
    private CheckBox _splitMic = null!;
    private CheckBox _splitMixed = null!;
    private GroupBox _splitApplyTo = null!;

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
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(FormWidth, FormHeight);

        var footer = BuildFooter();
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 8),
        };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildAudioTab());
        tabs.TabPages.Add(BuildMixedTab());
        tabs.TabPages.Add(BuildSplittingTab());

        // Order matters: Bottom-docked controls must be added BEFORE Fill,
        // otherwise the Fill control claims the entire ClientArea and the
        // footer ends up with zero width.
        Controls.Add(footer);
        Controls.Add(tabs);

        ApplyConfigToControls(_initial);
    }

    // ---------- General tab ----------
    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("General") { Padding = new Padding(TabPad) };
        var y = TabPad;

        // Output directory
        page.Controls.Add(MakeLabel("Output directory", TabPad, y));
        y += 22 + LabelToInput;
        const int browseWidth = 110;
        const int browseGap = 8;
        _outputDir = new TextBox
        {
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth - browseWidth - browseGap, InputHeight),
        };
        var browse = new Button
        {
            Text = "Browse…",
            Location = new Point(TabPad + InputWidth - browseWidth, y - 1),
            Size = new Size(browseWidth, InputHeight + 2),
        };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _outputDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _outputDir.Text = dlg.SelectedPath;
        };
        page.Controls.Add(_outputDir);
        page.Controls.Add(browse);
        y += InputHeight + FieldGap;

        // File name pattern
        page.Controls.Add(MakeLabel("File name pattern", TabPad, y));
        y += 22 + LabelToInput;
        _fileNamePattern = new TextBox
        {
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, InputHeight),
        };
        page.Controls.Add(_fileNamePattern);
        y += InputHeight + 4;
        page.Controls.Add(MakeHint("Tokens: {timestamp:format}, {track}.   {track} = system | mic | mixed.", TabPad, y));
        y += 18 + FieldGap;

        // Hotkey
        page.Controls.Add(MakeLabel("Global hotkey", TabPad, y));
        y += 22 + LabelToInput;
        _hotkey = new HotkeyCaptureControl
        {
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 56),
        };
        page.Controls.Add(_hotkey);
        y += 56 + FieldGap;

        // MP3 bitrate
        page.Controls.Add(MakeLabel("MP3 bitrate", TabPad, y));
        y += 22 + LabelToInput;
        _bitrate = new ComboBox
        {
            Location = new Point(TabPad, y),
            Size = new Size(320, InputHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _bitrate.Items.AddRange(new object[]
        {
            new BitrateOption(64,  "64 kbps (smallest)"),
            new BitrateOption(96,  "96 kbps (recommended for voice)"),
            new BitrateOption(128, "128 kbps"),
            new BitrateOption(192, "192 kbps (high quality)"),
        });
        _bitrate.DisplayMember = nameof(BitrateOption.Display);
        page.Controls.Add(_bitrate);
        y += InputHeight + FieldGap + 4;

        // Prompt-for-name checkbox
        _promptForName = new CheckBox
        {
            Text = "Ask for session name after recording",
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 24),
        };
        page.Controls.Add(_promptForName);
        y += 24 + 2;
        page.Controls.Add(MakeHint("Saves files in OutputDirectory/<your-name>_<timestamp>/", TabPad + 22, y));
        y += 18 + FieldGap;

        // Auto-detect calls
        _autoDetectCalls = new CheckBox
        {
            Text = "Auto-detect calls (Teams, Zoom, Meet, etc.)",
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 24),
        };
        page.Controls.Add(_autoDetectCalls);
        y += 24 + 2;
        page.Controls.Add(MakeHint("Auto-starts recording when an app uses your mic + speakers; asks before stopping.", TabPad + 22, y));

        return page;
    }

    // ---------- Audio tab ----------
    private TabPage BuildAudioTab()
    {
        var page = new TabPage("Audio") { Padding = new Padding(TabPad) };
        var y = TabPad;

        page.Controls.Add(MakeLabel("Microphone", TabPad, y));
        y += 22 + LabelToInput;
        _micDevice = new ComboBox
        {
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, InputHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(DeviceOption.Display),
        };
        page.Controls.Add(_micDevice);
        y += InputHeight + FieldGap;

        page.Controls.Add(MakeLabel("System audio (loopback)", TabPad, y));
        y += 22 + LabelToInput;
        _renderDevice = new ComboBox
        {
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, InputHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(DeviceOption.Display),
        };
        page.Controls.Add(_renderDevice);
        y += InputHeight + FieldGap;

        var refresh = new Button
        {
            Text = "↻ Refresh device list",
            Location = new Point(TabPad, y),
            Size = new Size(220, 32),
        };
        refresh.Click += (_, _) => PopulateAudioDevices();
        page.Controls.Add(refresh);
        y += 32 + FieldGap;

        _audioRecordingHint = new Label
        {
            Text = "⚠  Stop recording to change devices.",
            Location = new Point(TabPad, y),
            AutoSize = true,
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
        var page = new TabPage("Mixed file") { Padding = new Padding(TabPad) };

        _mixedEnabled = new CheckBox
        {
            Text = "Generate mixed file after each recording",
            Location = new Point(TabPad, TabPad),
            Size = new Size(InputWidth, 24),
        };
        _mixedEnabled.CheckedChanged += (_, _) => _mixedDetails.Enabled = _mixedEnabled.Checked;
        page.Controls.Add(_mixedEnabled);

        _mixedDetails = new GroupBox
        {
            Text = "Mixed file options",
            Location = new Point(TabPad, TabPad + 36),
            Size = new Size(InputWidth, 240),
        };
        page.Controls.Add(_mixedDetails);

        var gy = 32;
        _mixedDetails.Controls.Add(MakeLabel("Format", 20, gy));
        gy += 22 + 2;
        _mixedMono = new RadioButton
        {
            Text = "Mono (recommended for AI summarization)",
            Location = new Point(28, gy),
            Size = new Size(InputWidth - 60, 24),
        };
        _mixedDetails.Controls.Add(_mixedMono);
        gy += 26;
        _mixedStereo = new RadioButton
        {
            Text = "Stereo (L = system audio, R = microphone)",
            Location = new Point(28, gy),
            Size = new Size(InputWidth - 60, 24),
        };
        _mixedDetails.Controls.Add(_mixedStereo);
        gy += 36;

        _mixedDetails.Controls.Add(MakeLabel("Target sample rate", 20, gy));
        gy += 22 + 2;
        _mixedSampleRate = new ComboBox
        {
            Location = new Point(28, gy),
            Size = new Size(280, InputHeight),
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

    // ---------- Splitting tab ----------
    private TabPage BuildSplittingTab()
    {
        var page = new TabPage("Splitting") { Padding = new Padding(TabPad) };
        int y = TabPad;

        var modeBox = new GroupBox
        {
            Text = "Split mode",
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 150),
        };
        page.Controls.Add(modeBox);

        const int numericWidth = 90;

        _splitNone = new RadioButton
        {
            Text = "None — keep one file per track",
            Location = new Point(16, 28),
            Size = new Size(InputWidth - 40, 24),
        };
        modeBox.Controls.Add(_splitNone);

        _splitByTime = new RadioButton
        {
            Text = "By time",
            Location = new Point(16, 60),
            Size = new Size(110, 24),
        };
        modeBox.Controls.Add(_splitByTime);
        _splitMinutes = new NumericUpDown
        {
            Location = new Point(140, 58),
            Size = new Size(numericWidth, InputHeight),
            Minimum = 1,
            Maximum = 1440,
        };
        modeBox.Controls.Add(_splitMinutes);
        modeBox.Controls.Add(MakeLabel("minutes", 140 + numericWidth + 8, 62));

        _splitBySize = new RadioButton
        {
            Text = "By size",
            Location = new Point(16, 100),
            Size = new Size(110, 24),
        };
        modeBox.Controls.Add(_splitBySize);
        _splitSizeMb = new NumericUpDown
        {
            Location = new Point(140, 98),
            Size = new Size(numericWidth, InputHeight),
            Minimum = 1,
            Maximum = 10000,
        };
        modeBox.Controls.Add(_splitSizeMb);
        modeBox.Controls.Add(MakeLabel("MB", 140 + numericWidth + 8, 102));

        _splitSizeHint = MakeHint("NotebookLM accepts ≤ 200 MB", 140, 124);
        _splitSizeHint.ForeColor = Color.DarkOrange;
        _splitSizeHint.Visible = false;
        modeBox.Controls.Add(_splitSizeHint);

        y += modeBox.Height + FieldGap;

        _splitApplyTo = new GroupBox
        {
            Text = "Apply to",
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 120),
        };
        page.Controls.Add(_splitApplyTo);

        _splitSystem = new CheckBox
        {
            Text = "System track",
            Location = new Point(16, 28),
            Size = new Size(InputWidth - 40, 24),
        };
        _splitApplyTo.Controls.Add(_splitSystem);

        _splitMic = new CheckBox
        {
            Text = "Microphone track",
            Location = new Point(16, 56),
            Size = new Size(InputWidth - 40, 24),
        };
        _splitApplyTo.Controls.Add(_splitMic);

        _splitMixed = new CheckBox
        {
            Text = "Mixed track",
            Location = new Point(16, 84),
            Size = new Size(InputWidth - 40, 24),
        };
        _splitApplyTo.Controls.Add(_splitMixed);

        // Wire up enable/disable logic
        void Refresh()
        {
            bool timeOn = _splitByTime.Checked;
            bool sizeOn = _splitBySize.Checked;
            bool anyOn  = timeOn || sizeOn;

            _splitMinutes.Enabled = timeOn;
            _splitSizeMb.Enabled  = sizeOn;
            _splitApplyTo.Enabled = anyOn;
            _splitSizeHint.Visible = sizeOn && _splitSizeMb.Value > 200;
        }
        _splitNone.CheckedChanged   += (_, _) => Refresh();
        _splitByTime.CheckedChanged += (_, _) => Refresh();
        _splitBySize.CheckedChanged += (_, _) => Refresh();
        _splitSizeMb.ValueChanged   += (_, _) => Refresh();

        return page;
    }

    private Panel BuildFooter()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = FooterHeight,
            BackColor = Color.FromArgb(246, 246, 246),
        };

        const int btnW = 110;
        const int btnH = 34;

        var save = new Button
        {
            Text = "Save",
            Size = new Size(btnW, btnH),
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 0, 0),
        };
        save.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 212);
        save.Click += Save_Click;

        var cancel = new Button
        {
            Text = "Cancel",
            Size = new Size(btnW, btnH),
            Margin = new Padding(10, 0, 0, 0),
        };
        cancel.Click += (_, _) => { Result = null; Close(); };

        // FlowLayoutPanel right-to-left auto-positions buttons against the right edge,
        // immune to whatever width the parent panel ends up with.
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, (FooterHeight - btnH) / 2, 24, 0),
        };
        flow.Controls.Add(save);    // rightmost
        flow.Controls.Add(cancel);  // to the left of Save

        panel.Controls.Add(flow);
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
        ForeColor = Color.FromArgb(40, 40, 40),
    };

    private static Label MakeHint(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = Color.FromArgb(120, 120, 120),
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8.25f),
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
        _hotkey.SetInitialHotkey(cfg.Hotkey);

        SelectComboByValue(_bitrate, cfg.Mp3BitrateKbps,
            o => ((BitrateOption)o!).Kbps);

        _promptForName.Checked = cfg.PromptForSessionName;
        _autoDetectCalls.Checked = cfg.AutoDetectCallsEnabled;

        PopulateAudioDevices();

        _mixedEnabled.Checked = cfg.MixedFileEnabled;
        _mixedDetails.Enabled = cfg.MixedFileEnabled;
        var stereo = cfg.MixedFileFormat.Equals("Stereo", StringComparison.OrdinalIgnoreCase);
        _mixedStereo.Checked = stereo;
        _mixedMono.Checked = !stereo;

        SelectComboByValue(_mixedSampleRate, cfg.MixedFileSampleRate,
            o => ((SampleRateOption)o!).Hz);

        _splitMinutes.Value = Math.Clamp(cfg.SplitTimeMinutes, (int)_splitMinutes.Minimum, (int)_splitMinutes.Maximum);
        _splitSizeMb.Value  = Math.Clamp(cfg.SplitSizeMb,      (int)_splitSizeMb.Minimum,  (int)_splitSizeMb.Maximum);
        _splitSystem.Checked = cfg.SplitSystemTrack;
        _splitMic.Checked    = cfg.SplitMicTrack;
        _splitMixed.Checked  = cfg.SplitMixedTrack;

        switch (cfg.SplitMode)
        {
            case "Time": _splitByTime.Checked = true; break;
            case "Size": _splitBySize.Checked = true; break;
            default:     _splitNone.Checked   = true; break;
        }
        // Apply enable/visibility state directly. The radio CheckedChanged handlers
        // inside BuildSplittingTab also fire Refresh(), but doing it explicitly here
        // covers the case where the desired radio was already the default-checked one.
        bool isTime = cfg.SplitMode.Equals("Time", StringComparison.OrdinalIgnoreCase);
        bool isSize = cfg.SplitMode.Equals("Size", StringComparison.OrdinalIgnoreCase);
        _splitMinutes.Enabled  = isTime;
        _splitSizeMb.Enabled   = isSize;
        _splitApplyTo.Enabled  = isTime || isSize;
        _splitSizeHint.Visible = isSize && _splitSizeMb.Value > 200;
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
            AutoDetectCallsEnabled = _autoDetectCalls.Checked,
            SplitMode = _splitByTime.Checked ? "Time"
                      : _splitBySize.Checked ? "Size"
                      : "None",
            SplitTimeMinutes = (int)_splitMinutes.Value,
            SplitSizeMb      = (int)_splitSizeMb.Value,
            SplitSystemTrack = _splitSystem.Checked,
            SplitMicTrack    = _splitMic.Checked,
            SplitMixedTrack  = _splitMixed.Checked,
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record BitrateOption(int Kbps, string Display);
    private sealed record SampleRateOption(int Hz, string Display);
    private sealed record DeviceOption(string Id, string Display);
}
