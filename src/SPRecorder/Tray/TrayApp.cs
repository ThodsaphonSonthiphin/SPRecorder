using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using SPRecorder.Audio;
using SPRecorder.Configuration;
using SPRecorder.Hotkey;
using SPRecorder.Recording;
using SPRecorder.Settings;

namespace SPRecorder.Tray;

internal sealed class TrayApp : ApplicationContext
{
    private readonly AppConfigStore _store;
    private readonly NotifyIcon _notifyIcon;
    private readonly RecordingSession _session;
    private readonly CallDetector _callDetector;
    private GlobalHotkey? _startStopHotkey;
    private GlobalHotkey? _quickMarkHotkey;
    private GlobalHotkey? _markWithNoteHotkey;
    private readonly Icon _idleIcon;
    private readonly Icon _recIcon;
    private readonly System.Windows.Forms.Timer _tooltipTimer;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _screenItem;
    private readonly SynchronizationContext _uiContext;

    private bool _autoStarted;
    private CallEndConfirmation? _callEndToast;
    private ToolStripMenuItem _quickMarkItem = null!;
    private ToolStripMenuItem _noteMarkItem = null!;
    private MarkNoteInputForm? _noteForm;
    private MarkerStamp? _pendingStamp;
    private int _markerCount;

    public TrayApp(AppConfigStore store)
    {
        _store = store;
        _store.ConfigChanged += OnConfigChanged;

        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _idleIcon = IconFactory.CreateCircle(Color.Gray);
        _recIcon  = IconFactory.CreateCircle(Color.FromArgb(198, 40, 40));

        _session = new RecordingSession(() => _store.Current, PromptForSessionName);
        _session.StateChanged    += OnStateChanged;
        _session.Warning         += msg => OnUi(() => ShowBalloon(ToolTipIcon.Warning, "SPRecorder", msg));
        _session.MixingStarted   += () => OnUi(() => ShowBalloon(ToolTipIcon.Info, "Mixing tracks…", "Combining system + mic into one MP3 for AI summary."));
        _session.MixingCompleted += path => OnUi(() => OnMixingCompleted(path));
        _session.MarkerAdded += (seq, elapsed) => OnUi(() => OnMarkerAdded(seq, elapsed));

        _callDetector = new CallDetector();
        _callDetector.CallStarted += () => OnUi(OnCallStarted);
        _callDetector.CallEnded   += () => OnUi(OnCallEnded);

        _toggleItem = new ToolStripMenuItem("Start recording", null, (_, _) => ToggleRecording())
        {
            ShortcutKeyDisplayString = _store.Current.Hotkey,
        };
        _statusItem = new ToolStripMenuItem("Idle") { Enabled = false };
        _screenItem = new ToolStripMenuItem("Record screen too", null, (_, _) => ToggleScreenSetting())
        {
            CheckOnClick = false,
            Checked = _store.Current.ScreenRecordingEnabled,
        };
        _quickMarkItem = new ToolStripMenuItem("Add marker", null, (_, _) => OnQuickMark()) { Enabled = false };
        _noteMarkItem  = new ToolStripMenuItem("Add marker with note…", null, (_, _) => OnMarkWithNote()) { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(_screenItem);
        menu.Items.Add(_quickMarkItem);
        menu.Items.Add(_noteMarkItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open recordings folder", null, (_, _) => OpenFolder());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About", null, (_, _) => ShowAbout());
        menu.Items.Add("Quit",  null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            Text = "SPRecorder — idle",
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleRecording(); };

        RegisterHotkeys();

        _tooltipTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();

        if (_store.Current.AutoDetectCallsEnabled)
            _callDetector.Start();
    }

    private void RegisterHotkeys()
    {
        var cfg = _store.Current;
        _startStopHotkey?.Dispose();
        _quickMarkHotkey?.Dispose();
        _markWithNoteHotkey?.Dispose();

        _startStopHotkey    = MakeHotkey(cfg.Hotkey,            9000, ToggleRecording);
        _quickMarkHotkey    = MakeHotkey(cfg.QuickMarkHotkey,   9001, OnQuickMark);
        _markWithNoteHotkey = MakeHotkey(cfg.MarkWithNoteHotkey, 9002, OnMarkWithNote);
    }

    private GlobalHotkey? MakeHotkey(string spec, int id, Action onPressed)
    {
        try
        {
            var parsed = HotkeyParser.Parse(spec);
            var hk = new GlobalHotkey(parsed, id);
            hk.Pressed += onPressed;
            if (!hk.IsRegistered)
                ShowBalloon(ToolTipIcon.Warning, "Hotkey conflict",
                    $"{spec} is in use by another app. Use the tray menu, or change it in Settings.");
            return hk;
        }
        catch (Exception ex)
        {
            ShowBalloon(ToolTipIcon.Warning, "Hotkey error", ex.Message);
            return null;
        }
    }

    private void ToggleRecording()
    {
        if (_session.CurrentState == RecordingSession.State.Recording)
            StopRecording();
        else
            _session.Start();
    }

    private void StopRecording()
    {
        CommitPendingNote();   // write any open note's marker while still Recording
        _session.Stop();
    }

    private void OnQuickMark()
    {
        if (_session.CurrentState != RecordingSession.State.Recording)
        {
            ShowBalloon(ToolTipIcon.Info, "SPRecorder", "Not recording — start first.");
            return;
        }
        _session.AddMarker(note: null);
    }

    private void OnMarkWithNote()
    {
        if (_session.CurrentState != RecordingSession.State.Recording)
        {
            ShowBalloon(ToolTipIcon.Info, "SPRecorder", "Not recording — start first.");
            return;
        }
        if (_noteForm != null) { _noteForm.Activate(); return; }  // one at a time

        _pendingStamp = _session.CaptureStamp();
        var form = new MarkNoteInputForm(ChooseNoteMonitor());
        _noteForm = form;
        // Submitted fires on the UI thread (KeyDown / FormClosed / CommitNow), so commit
        // synchronously — a posted (async) commit would run AFTER Stop() has already closed
        // and finalized the marker log, silently dropping the note.
        form.Submitted += note =>
        {
            _session.AddMarker(note, _pendingStamp);
            _pendingStamp = null;
            _noteForm = null;
        };
        form.Show();
    }

    private void CommitPendingNote() => _noteForm?.CommitNow();

    private Screen ChooseNoteMonitor()
    {
        var recorded = _store.Current.ScreenMonitorDeviceName;
        if (string.IsNullOrEmpty(recorded))
            recorded = Screen.PrimaryScreen?.DeviceName ?? "";
        var names = Screen.AllScreens.Select(s => s.DeviceName).ToList();
        var pick = MarkNoteInputForm.PickNoteMonitorDeviceName(names, recorded);
        foreach (var s in Screen.AllScreens)
            if (s.DeviceName == pick) return s;
        return Screen.PrimaryScreen!;
    }

    private void OnMarkerAdded(int seq, TimeSpan elapsed)
    {
        _markerCount = seq;
        ShowBalloon(ToolTipIcon.Info, $"Marker #{seq}", $"{elapsed:hh\\:mm\\:ss}");
        UpdateTooltip();
    }

    private void ToggleScreenSetting()
    {
        var next = !_store.Current.ScreenRecordingEnabled;
        _store.Save(_store.Current with { ScreenRecordingEnabled = next });
    }

    private void OnConfigChanged(AppConfig oldConfig, AppConfig newConfig)
    {
        _screenItem.Checked = newConfig.ScreenRecordingEnabled;

        bool hotkeysChanged =
            oldConfig.Hotkey != newConfig.Hotkey ||
            oldConfig.QuickMarkHotkey != newConfig.QuickMarkHotkey ||
            oldConfig.MarkWithNoteHotkey != newConfig.MarkWithNoteHotkey;

        if (hotkeysChanged)
        {
            RegisterHotkeys();
            _toggleItem.ShortcutKeyDisplayString = newConfig.Hotkey;
            ShowBalloon(ToolTipIcon.Info, "Settings saved", "Hotkeys updated.");
        }
        else
        {
            ShowBalloon(ToolTipIcon.Info, "Settings saved", "");
        }

        if (oldConfig.AutoDetectCallsEnabled != newConfig.AutoDetectCallsEnabled)
        {
            if (newConfig.AutoDetectCallsEnabled) _callDetector.Start();
            else                                  _callDetector.Stop();
        }
    }

    private void OnCallStarted()
    {
        // If a call-end confirmation is showing, the call has resumed — close the toast,
        // keep recording, stay in auto-started mode.
        if (_callEndToast != null)
        {
            _callEndToast.Close();
            _callEndToast = null;
            return;
        }

        if (_session.CurrentState == RecordingSession.State.Recording) return;

        _autoStarted = true;
        ShowBalloon(ToolTipIcon.Info, "Call detected", "Auto-recording started.");
        _session.Start();
    }

    private void OnCallEnded()
    {
        if (!_autoStarted) return;
        if (_session.CurrentState != RecordingSession.State.Recording) return;
        if (_callEndToast != null) return; // already showing

        _callEndToast = new CallEndConfirmation();
        _callEndToast.StopRequested += () => OnUi(() =>
        {
            _callEndToast = null;
            if (_session.CurrentState == RecordingSession.State.Recording)
                StopRecording();
        });
        _callEndToast.KeepRequested += () => OnUi(() =>
        {
            _callEndToast = null;
            // _autoStarted stays true so the next CallEnded re-prompts.
        });
        _callEndToast.Show();
    }

    private void OpenSettings()
    {
        var isRecording = _session.CurrentState == RecordingSession.State.Recording;
        using var form = new SettingsForm(_store.Current, isRecording);
        if (form.ShowDialog() == DialogResult.OK && form.Result is { } updated)
            _store.Save(updated);
    }

    private string? PromptForSessionName()
    {
        using var prompt = new SessionNamePrompt();
        return prompt.ShowDialog() == DialogResult.OK ? prompt.SessionName : null;
    }

    private void OnStateChanged(RecordingSession.State state)
    {
        if (state == RecordingSession.State.Recording)
        {
            _notifyIcon.Icon = _recIcon;
            _toggleItem.Text = "Stop recording";
            _tooltipTimer.Start();
            _markerCount = 0;
            _quickMarkItem.Enabled = true;
            _noteMarkItem.Enabled = true;
            UpdateTooltip();
            ShowBalloon(ToolTipIcon.Info, "Recording started", "System + microphone");
        }
        else
        {
            _notifyIcon.Icon = _idleIcon;
            _toggleItem.Text = "Start recording";
            _tooltipTimer.Stop();
            _notifyIcon.Text = "SPRecorder — idle";
            _statusItem.Text = "Idle";
            _quickMarkItem.Enabled = false;
            _noteMarkItem.Enabled = false;

            // Reset auto-start tracking and dismiss any pending confirmation.
            _autoStarted = false;
            if (_callEndToast != null)
            {
                _callEndToast.Close();
                _callEndToast = null;
            }

            if (_session.SystemFilePath != null && _session.MicFilePath != null)
            {
                long total = SafeSize(_session.SystemFilePath) + SafeSize(_session.MicFilePath);
                var msg = _store.Current.MixedFileEnabled
                    ? $"Saved 2 tracks ({FormatSize(total)}). Mixing third file for AI…"
                    : $"Saved 2 tracks ({FormatSize(total)}).";
                ShowBalloon(ToolTipIcon.Info, "Recording stopped", msg);
            }
        }
    }

    private void OnMixingCompleted(string? mixedPath)
    {
        if (mixedPath is null)
        {
            ShowBalloon(ToolTipIcon.Warning, "Mixing failed", "Separate tracks were saved successfully.");
            return;
        }
        ShowBalloon(ToolTipIcon.Info, "Mixed file ready",
            $"{FormatSize(SafeSize(mixedPath))} · {Path.GetFileName(mixedPath)}");
    }

    private void OnUi(Action action) => _uiContext.Post(_ => action(), null);

    private void UpdateTooltip()
    {
        var elapsed = _session.Elapsed ?? TimeSpan.Zero;
        var markers = _markerCount > 0 ? $" · {_markerCount} markers" : "";
        var text = $"Recording… {elapsed:hh\\:mm\\:ss}{markers}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        _statusItem.Text = $"Recording for {elapsed:hh\\:mm\\:ss}{markers}";
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_store.Current.OutputDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _store.Current.OutputDirectory,
            UseShellExecute = true,
        });
    }

    private void ShowAbout() =>
        ShowBalloon(ToolTipIcon.Info, "SPRecorder",
            "Dual-track meeting recorder.\nConfig: Settings… in tray menu, or appsettings.json next to the .exe.");

    private void ShowBalloon(ToolTipIcon icon, string title, string text)
    {
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = string.IsNullOrEmpty(text) ? " " : text;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private static long SafeSize(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
    private static string FormatSize(long b) =>
        b < 1024 * 1024 ? $"{b / 1024.0:0.#} KB" : $"{b / (1024.0 * 1024):0.##} MB";

    protected override void ExitThreadCore()
    {
        _tooltipTimer.Stop();
        CommitPendingNote();
        _startStopHotkey?.Dispose();
        _quickMarkHotkey?.Dispose();
        _markWithNoteHotkey?.Dispose();
        _callDetector.Dispose();
        _callEndToast?.Close();
        _session.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _idleIcon.Dispose();
        _recIcon.Dispose();
        base.ExitThreadCore();
    }
}
