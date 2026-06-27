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
    private readonly Icon _idleIconBadged;
    private readonly Icon _recIconBadged;
    private readonly System.Windows.Forms.Timer _tooltipTimer;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _screenItem;
    private readonly SynchronizationContext _uiContext;

    private bool _autoStarted;
    private CallEndConfirmation? _callEndToast;
    private ToolStripMenuItem _quickMarkItem = null!;
    private ToolStripMenuItem _noteMarkItem = null!;
    private ToolStripMenuItem _hotkeyConflictItem = null!;
    private MarkNoteInputForm? _noteForm;
    private MarkerStamp? _pendingStamp;
    private int _markerCount;
    private ToolStripMenuItem _openReviewItem = null!;
    private string? _lastReviewPagePath;
    private Action? _onBalloonClick;

    private readonly HdrDisplay _hdr = new();
    private string? _hdrTurnedOffFor;   // monitor we turned HDR off on, to restore on stop (null = untouched)

    public TrayApp(AppConfigStore store)
    {
        _store = store;
        _store.ConfigChanged += OnConfigChanged;

        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _idleIcon = IconFactory.CreateCircle(Color.Gray);
        _recIcon  = IconFactory.CreateCircle(Color.FromArgb(198, 40, 40));
        _idleIconBadged = IconFactory.CreateCircleWithBadge(Color.Gray, Color.FromArgb(255, 193, 7));
        _recIconBadged  = IconFactory.CreateCircleWithBadge(Color.FromArgb(198, 40, 40), Color.FromArgb(255, 193, 7));

        _session = new RecordingSession(() => _store.Current, PromptForSessionName);
        _session.StateChanged    += OnStateChanged;
        _session.Warning         += msg => OnUi(() => ShowBalloon(ToolTipIcon.Warning, "SPRecorder", msg));
        _session.MixingStarted   += () => OnUi(() => ShowBalloon(ToolTipIcon.Info, "Mixing tracks…", "Combining system + mic into one MP3 for AI summary."));
        _session.MixingCompleted += path => OnUi(() => OnMixingCompleted(path));
        _session.MarkerAdded += (seq, elapsed) => OnUi(() => OnMarkerAdded(seq, elapsed));
        _session.ReviewPageReady += path => OnUi(() => OnReviewPageReady(path));

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
        _hotkeyConflictItem = new ToolStripMenuItem("⚠ Hotkey(s) inactive — open Settings", null, (_, _) => OpenSettings())
        {
            Visible = false,
        };

        _openReviewItem = new ToolStripMenuItem("Open marker review", null, (_, _) => OpenReviewPage())
        {
            Visible = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(_hotkeyConflictItem);
        menu.Items.Add(_screenItem);
        menu.Items.Add(_quickMarkItem);
        menu.Items.Add(_noteMarkItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open recordings folder", null, (_, _) => OpenFolder());
        menu.Items.Add(_openReviewItem);
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
        _notifyIcon.BalloonTipClicked += (_, _) => { var a = _onBalloonClick; _onBalloonClick = null; a?.Invoke(); };

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

        var status = CurrentHotkeyStatus();
        if (status.AnyInactive)
        {
            var inactive = status.InactiveLabels();
            ShowBalloon(ToolTipIcon.Warning, $"{inactive.Count} hotkey(s) inactive",
                $"{string.Join(", ", inactive)} couldn't register (in use by another app). Open Settings to fix.");
        }

        RefreshHotkeyStatusIndicators();
    }

    private GlobalHotkey? MakeHotkey(string spec, int id, Action onPressed)
    {
        try
        {
            var parsed = HotkeyParser.Parse(spec);
            var hk = new GlobalHotkey(parsed, id);
            hk.Pressed += onPressed;
            return hk;
        }
        catch
        {
            return null;   // unparseable spec → inactive (surfaced by the consolidated indicators)
        }
    }

    private HotkeyStatus CurrentHotkeyStatus() => new(
        _startStopHotkey    is { IsRegistered: true },
        _quickMarkHotkey    is { IsRegistered: true },
        _markWithNoteHotkey is { IsRegistered: true });

    private void ApplyTrayIcon()
    {
        bool recording = _session.CurrentState == RecordingSession.State.Recording;
        bool inactive  = CurrentHotkeyStatus().AnyInactive;
        _notifyIcon.Icon = recording
            ? (inactive ? _recIconBadged : _recIcon)
            : (inactive ? _idleIconBadged : _idleIcon);
    }

    private string HotkeyConflictSuffix()
        => CurrentHotkeyStatus().AnyInactive ? " · ⚠ hotkey inactive" : "";

    private void RefreshHotkeyStatusIndicators()
    {
        var status = CurrentHotkeyStatus();
        _hotkeyConflictItem.Visible = status.AnyInactive;
        if (status.AnyInactive)
            _hotkeyConflictItem.Text = $"⚠ {string.Join(", ", status.InactiveLabels())} inactive — open Settings";

        ApplyTrayIcon();

        if (_session.CurrentState == RecordingSession.State.Recording)
            UpdateTooltip();
        else
            _notifyIcon.Text = "SPRecorder — idle" + HotkeyConflictSuffix();
    }

    private void ToggleRecording()
    {
        if (_session.CurrentState == RecordingSession.State.Recording)
            StopRecording();
        else
            StartManual();
    }

    private void StartManual()
    {
        if (!ConfirmHdrBeforeStart()) return;   // user cancelled the start
        _session.Start();
    }

    /// <summary>
    /// If screen recording is on and the recorded monitor is in HDR, warn the user —
    /// ScreenRecorderLib can't capture HDR, so colours come out washed-out/oversaturated —
    /// and optionally turn HDR off for the recording (restored on stop). Returns false
    /// only when the user chooses to cancel the start. Best-effort: never blocks on error.
    /// </summary>
    private bool ConfirmHdrBeforeStart()
    {
        if (!_store.Current.ScreenRecordingEnabled) return true;

        var device = _store.Current.ScreenMonitorDeviceName;
        if (_hdr.GetState(device) != HdrState.On) return true;

        var label = string.IsNullOrEmpty(device) ? "primary monitor" : device;
        switch (HdrWarningPrompt.Ask(label))
        {
            case HdrPromptResult.DisableHdr:
                if (_hdr.TryTurnOff(device))
                    _hdrTurnedOffFor = device;
                else
                    ShowBalloon(ToolTipIcon.Warning, "Couldn't turn off HDR",
                        "Recording will continue, but colours may be wrong. Try Win+Alt+B.");
                return true;
            case HdrPromptResult.RecordAnyway:
                return true;
            default:
                return false;   // Cancel
        }
    }

    private void RestoreHdrIfNeeded()
    {
        if (_hdrTurnedOffFor is { } device)
        {
            _hdr.TurnOn(device);
            _hdrTurnedOffFor = null;
        }
    }

    /// <summary>Non-blocking HDR warning for the auto-start path (no modal dialog).</summary>
    private void WarnIfRecordedMonitorHdr()
    {
        if (!_store.Current.ScreenRecordingEnabled) return;
        if (_hdr.GetState(_store.Current.ScreenMonitorDeviceName) != HdrState.On) return;
        ShowBalloon(ToolTipIcon.Warning, "HDR is on",
            "Recording colours will be wrong. Press Win+Alt+B to turn HDR off, then restart the recording.");
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
        WarnIfRecordedMonitorHdr();
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
        using var form = new SettingsForm(_store.Current, isRecording, CurrentHotkeyStatus());
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
            ApplyTrayIcon();
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
            RestoreHdrIfNeeded();   // turn HDR back on if we disabled it for this recording

            ApplyTrayIcon();
            _toggleItem.Text = "Start recording";
            _tooltipTimer.Stop();
            _notifyIcon.Text = "SPRecorder — idle" + HotkeyConflictSuffix();
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

    private void OnReviewPageReady(string path)
    {
        _lastReviewPagePath = path;
        _openReviewItem.Visible = true;
        ShowBalloon(ToolTipIcon.Info, "Marker review ready",
            _markerCount > 0 ? $"{_markerCount} marker(s) — click to open" : "Click to open");
        _onBalloonClick = OpenReviewPage;   // set AFTER ShowBalloon (which clears it)
        if (_store.Current.AutoOpenMarkerReview) OpenReviewPage();
    }

    private void OpenReviewPage()
    {
        var p = _lastReviewPagePath;
        if (p is null || !File.Exists(p)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowBalloon(ToolTipIcon.Warning, "Couldn't open review page", ex.Message);
        }
    }

    private void OnUi(Action action) => _uiContext.Post(_ => action(), null);

    private void UpdateTooltip()
    {
        var elapsed = _session.Elapsed ?? TimeSpan.Zero;
        var markers = _markerCount > 0 ? $" · {_markerCount} markers" : "";
        var text = $"Recording… {elapsed:hh\\:mm\\:ss}{markers}{HotkeyConflictSuffix()}";
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
        _onBalloonClick = null;
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
        _idleIconBadged.Dispose();
        _recIconBadged.Dispose();
        base.ExitThreadCore();
    }
}
