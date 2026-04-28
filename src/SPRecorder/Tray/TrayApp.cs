using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
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
    private GlobalHotkey? _hotkey;
    private readonly Icon _idleIcon;
    private readonly Icon _recIcon;
    private readonly System.Windows.Forms.Timer _tooltipTimer;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly SynchronizationContext _uiContext;

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

        _toggleItem = new ToolStripMenuItem("Start recording", null, (_, _) => _session.Toggle())
        {
            ShortcutKeyDisplayString = _store.Current.Hotkey,
        };
        _statusItem = new ToolStripMenuItem("Idle") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_statusItem);
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
        _notifyIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _session.Toggle(); };

        RegisterHotkey(_store.Current.Hotkey);

        _tooltipTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
    }

    private void RegisterHotkey(string hotkeySpec)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        try
        {
            var parsed = HotkeyParser.Parse(hotkeySpec);
            _hotkey = new GlobalHotkey(parsed);
            _hotkey.Pressed += () => _session.Toggle();
            if (!_hotkey.IsRegistered)
            {
                ShowBalloon(ToolTipIcon.Warning, "Hotkey conflict",
                    $"{hotkeySpec} is in use by another app. Use the tray menu, or change Hotkey in Settings.");
            }
        }
        catch (Exception ex)
        {
            ShowBalloon(ToolTipIcon.Warning, "Hotkey error", ex.Message);
        }
    }

    private void OnConfigChanged(AppConfig oldConfig, AppConfig newConfig)
    {
        if (oldConfig.Hotkey != newConfig.Hotkey)
        {
            RegisterHotkey(newConfig.Hotkey);
            _toggleItem.ShortcutKeyDisplayString = newConfig.Hotkey;
            ShowBalloon(ToolTipIcon.Info, "Settings saved",
                $"Hotkey changed to {newConfig.Hotkey}.");
        }
        else
        {
            ShowBalloon(ToolTipIcon.Info, "Settings saved", "");
        }
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
        var text = $"Recording… {elapsed:hh\\:mm\\:ss}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        _statusItem.Text = $"Recording for {elapsed:hh\\:mm\\:ss}";
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
        _hotkey?.Dispose();
        _session.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _idleIcon.Dispose();
        _recIcon.Dispose();
        base.ExitThreadCore();
    }
}
