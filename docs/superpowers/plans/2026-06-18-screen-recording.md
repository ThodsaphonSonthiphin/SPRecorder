# Screen Recording Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional `screen` track — a self-contained MP4 of one selectable monitor (default primary) with embedded system+mic audio, plus a mouse-click highlight and a KeyCastr-style on-screen key caster — opt-in via a persisted setting and a tray quick-toggle, additive to the existing audio pipeline.

**Architecture:** A new `ScreenRecorder` wraps ScreenRecorderLib (capture + H.264 + audio mux + mouse-click highlight). A new `InputHighlightOverlay` draws the key caster as a transparent click-through window pinned to the recorded monitor, fed by a global low-level keyboard hook; ScreenRecorderLib's monitor capture composites it into the MP4. `RecordingSession` starts/stops both under the toggle, never letting a video failure break audio, and the MP4 bypasses the audio mix/split post-processing.

**Tech Stack:** .NET 10 (`net10.0-windows`), Windows Forms, NAudio (existing audio), ScreenRecorderLib v5+ (screen video, x64 native), Win32 P/Invoke (`SetWindowsHookEx`/`WH_KEYBOARD_LL`, layered windows), xUnit.

## Global Constraints

- **Target framework:** `net10.0-windows` (unchanged).
- **Platform:** build/publish with **`Platform = x64`** — ScreenRecorderLib's MSBuild targets reject `AnyCPU` (ADR 0002).
- **No single-file publish** for this build — `ScreenRecorderLib.dll` is mixed-mode C++/CLI and cannot live in a self-extracting bundle; distribute the published **folder** (ADR 0002).
- **DPI:** the app runs **PerMonitorV2** (`<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`) so the overlay lands pixel-correct across mixed-DPI monitors (ADR 0006).
- **Additive & safe:** the existing `system`/`mic`/`mixed`/split MP3 pipeline must be unchanged; **audio recording must never fail because video failed** (ADR 0005).
- **Opt-in:** screen recording is off by default; the toggle is read once at `Start()` (ADR 0004).
- **Privacy:** the key caster shows every key; surface a warning in Settings and README (ADR 0003). The keyboard hook is installed only while a screen recording with keystrokes is active, and never persists keys to disk.
- **Tests:** run with `dotnet test`. Keep ScreenRecorderLib and Win32 hooks OUT of the test project — only pure logic (config, filename) is unit-tested; native/UI is build + manual smoke.
- **Commits:** one commit per task, conventional-commit style.

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/SPRecorder/SPRecorder.csproj` | Package ref, Platform x64, PerMonitorV2 | Modify |
| `src/SPRecorder/appsettings.json` | Default config keys | Modify |
| `src/SPRecorder/Configuration/AppConfig.cs` | New screen fields + `Load` clamping | Modify |
| `src/SPRecorder/Recording/FileNameBuilder.cs` | `BuildScreen` → `_screen.mp4` | Modify |
| `src/SPRecorder/Recording/ScreenRecorder.cs` | ScreenRecorderLib wrapper (capture/encode/audio/mouse-click, display enum) | Create |
| `src/SPRecorder/Overlay/InputHighlightOverlay.cs` | Key caster: layered click-through overlay + `WH_KEYBOARD_LL` hook | Create |
| `src/SPRecorder/Recording/RecordingSession.cs` | Start/stop screen + overlay under toggle; `ScreenFilePath`; rename; failure isolation | Modify |
| `src/SPRecorder/Settings/SettingsForm.cs` | New "Screen" tab + monitor dropdown | Modify |
| `src/SPRecorder/Tray/TrayApp.cs` | Checkable "Record screen too" menu item | Modify |
| `tests/SPRecorder.Tests/AppConfigStoreTests.cs` | Roundtrip + clamp new fields | Modify |
| `tests/SPRecorder.Tests/FileNameBuilderTests.cs` | `_screen.mp4` naming | Modify |
| `README.md` | Folder distribution, prerequisites, usage, privacy, roadmap | Modify |

---

## Task 1: Project setup — ScreenRecorderLib, x64, PerMonitorV2

**Files:**
- Modify: `src/SPRecorder/SPRecorder.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: the `ScreenRecorderLib` namespace is referenceable; the app process is PerMonitorV2; builds target x64.

- [ ] **Step 1: Add the package and build properties**

Edit `src/SPRecorder/SPRecorder.csproj`. Add to the existing first `<PropertyGroup>`:

```xml
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

Add to the package `<ItemGroup>` (alongside NAudio):

```xml
    <PackageReference Include="ScreenRecorderLib" Version="6.*" />
```

> If `6.*` does not resolve, pin to the latest published `5.x`/`6.x` shown by `dotnet list package` / nuget.org. Any v5+ has the multi-source `Recorder.GetDisplays()` API this plan uses.

- [ ] **Step 2: Restore and build (x64)**

Run: `dotnet build src/SPRecorder/SPRecorder.csproj -c Debug`
Expected: BUILD SUCCEEDED. (If you see "AnyCPU is not supported", confirm `PlatformTarget=x64` was added.)

- [ ] **Step 3: Confirm existing tests still pass**

Run: `dotnet test`
Expected: all existing tests PASS (the test project does not reference ScreenRecorderLib).

- [ ] **Step 4: Commit**

```bash
git add src/SPRecorder/SPRecorder.csproj
git commit -m "build: add ScreenRecorderLib, target x64, enable PerMonitorV2"
```

---

## Task 2: Config fields for screen recording

**Files:**
- Modify: `src/SPRecorder/Configuration/AppConfig.cs`
- Modify: `src/SPRecorder/appsettings.json`
- Test: `tests/SPRecorder.Tests/AppConfigStoreTests.cs`

**Interfaces:**
- Consumes: existing `AppConfig` record + `AppConfig.Load(IConfiguration)`.
- Produces: new init properties `ScreenRecordingEnabled` (bool), `ScreenMonitorDeviceName` (string), `ScreenFrameRate` (int), `ScreenQuality` (string), `ShowMouseClicks` (bool), `ShowKeystrokes` (bool); `Load` clamps `ScreenFrameRate` to {15,25,30} and normalizes `ScreenQuality` to `Low|Medium|High`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/SPRecorder.Tests/AppConfigStoreTests.cs`:

```csharp
    [Fact]
    public void Save_RoundtripsScreenFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppConfigStore(path, new AppConfig());
            var updated = new AppConfig
            {
                ScreenRecordingEnabled = true,
                ScreenMonitorDeviceName = "\\\\.\\DISPLAY2",
                ScreenFrameRate = 25,
                ScreenQuality = "High",
                ShowMouseClicks = false,
                ShowKeystrokes = false,
            };

            store.Save(updated);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ScreenRecordingEnabled").GetBoolean());
            Assert.Equal("\\\\.\\DISPLAY2", root.GetProperty("ScreenMonitorDeviceName").GetString());
            Assert.Equal(25, root.GetProperty("ScreenFrameRate").GetInt32());
            Assert.Equal("High", root.GetProperty("ScreenQuality").GetString());
            Assert.False(root.GetProperty("ShowMouseClicks").GetBoolean());
            Assert.False(root.GetProperty("ShowKeystrokes").GetBoolean());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_ClampsScreenFrameRate_AndNormalizesQuality()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
            {
              "ScreenFrameRate": 99,
              "ScreenQuality": "ultra"
            }
            """);

            var builder = new ConfigurationBuilder().AddJsonFile(path);
            var loaded = AppConfig.Load(builder.Build());

            Assert.Equal(30, loaded.ScreenFrameRate);     // snapped to nearest allowed {15,25,30}
            Assert.Equal("Medium", loaded.ScreenQuality); // unknown → Medium
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AppConfigStoreTests"`
Expected: FAIL — `AppConfig` has no member `ScreenRecordingEnabled` (compile error).

- [ ] **Step 3: Add the fields and clamping**

In `src/SPRecorder/Configuration/AppConfig.cs`, add after the `SplitMixedTrack` property:

```csharp
    public bool   ScreenRecordingEnabled  { get; init; } = false;
    public string ScreenMonitorDeviceName { get; init; } = "";        // "" = primary; else \\.\DISPLAYn
    public int    ScreenFrameRate         { get; init; } = 30;        // 15 | 25 | 30
    public string ScreenQuality           { get; init; } = "Medium";  // Low | Medium | High
    public bool   ShowMouseClicks         { get; init; } = true;
    public bool   ShowKeystrokes          { get; init; } = true;
```

In the `Load` method's `return raw with { ... }` block, add:

```csharp
            ScreenFrameRate = NearestFrameRate(raw.ScreenFrameRate),
            ScreenQuality   = raw.ScreenQuality is "Low" or "Medium" or "High" ? raw.ScreenQuality : "Medium",
```

And add this private helper to the record:

```csharp
    private static int NearestFrameRate(int fps)
    {
        int[] allowed = { 15, 25, 30 };
        int best = allowed[0];
        foreach (var a in allowed)
            if (Math.Abs(a - fps) < Math.Abs(best - fps)) best = a;
        return best;
    }
```

- [ ] **Step 4: Update `appsettings.json` defaults**

In `src/SPRecorder/appsettings.json`, add before the closing brace (after `SplitMixedTrack`):

```json
  "ScreenRecordingEnabled": false,
  "ScreenMonitorDeviceName": "",
  "ScreenFrameRate": 30,
  "ScreenQuality": "Medium",
  "ShowMouseClicks": true,
  "ShowKeystrokes": true
```

(Remember to add a comma after the previous `SplitMixedTrack` line.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AppConfigStoreTests"`
Expected: PASS (all, including the two new tests).

- [ ] **Step 6: Commit**

```bash
git add src/SPRecorder/Configuration/AppConfig.cs src/SPRecorder/appsettings.json tests/SPRecorder.Tests/AppConfigStoreTests.cs
git commit -m "feat: add screen-recording config fields with load clamping"
```

---

## Task 3: `_screen.mp4` filename builder

**Files:**
- Modify: `src/SPRecorder/Recording/FileNameBuilder.cs`
- Test: `tests/SPRecorder.Tests/FileNameBuilderTests.cs`

**Interfaces:**
- Consumes: existing `FileNameBuilder.Build(string pattern, DateTime timestamp, string track)`.
- Produces: `static string FileNameBuilder.BuildScreen(string pattern, DateTime timestamp)` — same name as the `screen` track but with the extension forced to `.mp4`.

- [ ] **Step 1: Write the failing test**

Add to `tests/SPRecorder.Tests/FileNameBuilderTests.cs`:

```csharp
    [Fact]
    public void BuildScreen_ForcesMp4Extension()
    {
        var ts = new DateTime(2026, 6, 18, 14, 3, 22);
        var name = FileNameBuilder.BuildScreen("{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3", ts);
        Assert.Equal("2026-06-18_14-03-22_screen.mp4", name);
    }

    [Fact]
    public void BuildScreen_AddsMp4_WhenPatternHasNoExtension()
    {
        var ts = new DateTime(2026, 6, 18, 14, 3, 22);
        var name = FileNameBuilder.BuildScreen("{timestamp:yyyy-MM-dd}_{track}", ts);
        Assert.Equal("2026-06-18_screen.mp4", name);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FileNameBuilderTests"`
Expected: FAIL — `BuildScreen` not defined (compile error).

- [ ] **Step 3: Implement `BuildScreen`**

Add to `src/SPRecorder/Recording/FileNameBuilder.cs`, after `Build`:

```csharp
    public static string BuildScreen(string pattern, DateTime timestamp)
    {
        var baseName = Build(pattern, timestamp, "screen");
        return Path.ChangeExtension(baseName, ".mp4");
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FileNameBuilderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SPRecorder/Recording/FileNameBuilder.cs tests/SPRecorder.Tests/FileNameBuilderTests.cs
git commit -m "feat: build _screen.mp4 filename from the existing pattern"
```

---

## Task 4: `ScreenRecorder` wrapper over ScreenRecorderLib

**Files:**
- Create: `src/SPRecorder/Recording/ScreenRecorder.cs`

**Interfaces:**
- Consumes: `AppConfig` (`ScreenMonitorDeviceName`, `ScreenFrameRate`, `ScreenQuality`, `ShowMouseClicks`); ScreenRecorderLib types.
- Produces:
  - `record struct DisplayInfo(string DeviceName, string FriendlyName)`
  - `static IReadOnlyList<DisplayInfo> ScreenRecorder.GetDisplays()`
  - `sealed class ScreenRecorder : IDisposable` with `event Action<string>? Failed`, `void Start(string filePath, AppConfig cfg)`, `void Stop()`, `string? ResolvedDeviceName { get; }`.

This wrapper is the single place that touches ScreenRecorderLib; isolating it keeps `RecordingSession` testable and contains any API drift.

- [ ] **Step 1: Create the wrapper**

Create `src/SPRecorder/Recording/ScreenRecorder.cs`:

```csharp
using ScreenRecorderLib;

namespace SPRecorder.Recording;

/// <summary>A single connected display the user can pick to record.</summary>
public readonly record struct DisplayInfo(string DeviceName, string FriendlyName);

/// <summary>
/// Wraps ScreenRecorderLib. Records one monitor (default primary) to an MP4 with
/// system+mic audio embedded and a built-in mouse-click highlight. The ONLY type
/// that references ScreenRecorderLib, so API drift is contained here.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    private Recorder? _recorder;
    private readonly ManualResetEventSlim _completed = new(false);

    public event Action<string>? Failed;

    /// <summary>The \\.\DISPLAYn actually used (after primary fallback), for logging.</summary>
    public string? ResolvedDeviceName { get; private set; }

    /// <summary>Enumerate connected displays for the Settings picker.</summary>
    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        try
        {
            return Recorder.GetDisplays()
                .Select(d => new DisplayInfo(d.DeviceName, d.FriendlyName))
                .ToList();
        }
        catch
        {
            return Array.Empty<DisplayInfo>();
        }
    }

    public void Start(string filePath, AppConfig cfg)
    {
        // Pick the configured monitor; fall back to primary if it is gone.
        DisplayRecordingSource source;
        var wanted = cfg.ScreenMonitorDeviceName;
        if (!string.IsNullOrEmpty(wanted) &&
            Recorder.GetDisplays().Any(d => d.DeviceName == wanted))
        {
            source = new DisplayRecordingSource(wanted);
        }
        else
        {
            if (!string.IsNullOrEmpty(wanted))
                Failed?.Invoke($"Monitor {wanted} not found; recording the primary monitor instead.");
            source = DisplayRecordingSource.MainMonitor;
        }
        source.IsCursorCaptureEnabled = true;
        ResolvedDeviceName = source.DeviceName;

        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase> { source },
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = true,
                IsOutputDeviceEnabled = true, // system loopback
                IsInputDeviceEnabled = true,  // microphone
            },
            VideoOptions = new VideoOptions
            {
                Framerate = cfg.ScreenFrameRate,
                BitrateMode = BitrateControlMode.Quality,
                Quality = QualityFor(cfg.ScreenQuality),
            },
            MouseOptions = new MouseOptions
            {
                IsMouseClicksDetected = cfg.ShowMouseClicks,
                IsMousePointerEnabled = true,
            },
        };

        _completed.Reset();
        _recorder = Recorder.CreateRecorder(options);
        _recorder.OnRecordingComplete += (_, _) => _completed.Set();
        _recorder.OnRecordingFailed += (_, e) =>
        {
            Failed?.Invoke("Screen recording failed: " + e.Error);
            _completed.Set();
        };
        _recorder.Record(filePath);
    }

    public void Stop()
    {
        var rec = _recorder;
        if (rec is null) return;
        try
        {
            rec.Stop();
            // ScreenRecorderLib finalizes the MP4 asynchronously; wait briefly so the
            // file is closed before the caller renames/moves it.
            _completed.Wait(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Failed?.Invoke("Screen stop: " + ex.Message);
        }
        finally
        {
            rec.Dispose();
            _recorder = null;
        }
    }

    private static int QualityFor(string q) => q switch
    {
        "Low"  => 40,
        "High" => 90,
        _      => 70, // Medium
    };

    public void Dispose()
    {
        try { _recorder?.Dispose(); } catch { /* ignore */ }
        _recorder = null;
        _completed.Dispose();
    }
}
```

- [ ] **Step 2: Build and reconcile API names against the installed package**

Run: `dotnet build src/SPRecorder/SPRecorder.csproj -c Debug`
Expected: BUILD SUCCEEDED. If any ScreenRecorderLib member name differs in the installed version (e.g. `BitrateControlMode`, `QualityFor` range, `DisplayRecordingSource` ctor), fix the wrapper to match the package's IntelliSense — the public surface of `ScreenRecorder` (the `DisplayInfo` record, `GetDisplays`, `Start`, `Stop`, `Failed`, `ResolvedDeviceName`) must stay exactly as written, since later tasks depend on it.

- [ ] **Step 3: Confirm existing tests still pass**

Run: `dotnet test`
Expected: all PASS (no test references this class yet).

- [ ] **Step 4: Commit**

```bash
git add src/SPRecorder/Recording/ScreenRecorder.cs
git commit -m "feat: add ScreenRecorder wrapper over ScreenRecorderLib"
```

---

## Task 5: `InputHighlightOverlay` — key caster + keyboard hook

**Files:**
- Create: `src/SPRecorder/Overlay/InputHighlightOverlay.cs`

**Interfaces:**
- Consumes: `System.Windows.Forms.Screen`, Win32 P/Invoke.
- Produces: `sealed class InputHighlightOverlay : IDisposable` with `void Show(System.Windows.Forms.Screen monitor)` and `void HideAndDispose()`. Internally owns the `WH_KEYBOARD_LL` hook and a transparent click-through caption window pinned to `monitor.Bounds`.

The overlay shows every key pressed (ADR 0003), pinned to the **recorded** monitor (ADR 0006). It is click-through and never sets `WDA_EXCLUDEFROMCAPTURE`, so ScreenRecorderLib composites it into the MP4.

- [ ] **Step 1: Create the overlay**

Create `src/SPRecorder/Overlay/InputHighlightOverlay.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SPRecorder.Overlay;

/// <summary>
/// A transparent, topmost, click-through caption pinned to a chosen monitor that
/// shows the keys being pressed (KeyCastr-style). Fed by a global low-level
/// keyboard hook. Active only while a screen recording with keystrokes is on; it
/// never persists keystrokes. Because it is layered + topmost and does NOT set
/// WDA_EXCLUDEFROMCAPTURE, the screen recorder captures it into the video.
/// </summary>
public sealed class InputHighlightOverlay : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly LowLevelKeyboardProc _proc;   // kept alive for the hook
    private IntPtr _hookId = IntPtr.Zero;
    private CaptionForm? _form;
    private readonly System.Windows.Forms.Timer _fade = new() { Interval = 250 };
    private DateTime _lastKey = DateTime.MinValue;

    public InputHighlightOverlay() => _proc = HookCallback;

    public void Show(Screen monitor)
    {
        _form = new CaptionForm();
        _form.Bounds = monitor.Bounds;           // virtual-screen coords (may be negative)
        _form.Show();
        _form.DpiChanged += (_, _) => { if (_form != null) _form.Bounds = monitor.Bounds; };

        _hookId = SetHook(_proc);

        _fade.Tick += (_, _) =>
        {
            if (_form != null && (DateTime.UtcNow - _lastKey).TotalMilliseconds > 1500)
                _form.SetText("");
        };
        _fade.Start();
    }

    public void HideAndDispose()
    {
        _fade.Stop();
        if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
        _form?.Close();
        _form?.Dispose();
        _form = null;
    }

    public void Dispose()
    {
        HideAndDispose();
        _fade.Dispose();
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        // Module handle is optional for LL hooks within the same process.
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            var label = KeyLabels.Describe((Keys)vk);
            _lastKey = DateTime.UtcNow;
            _form?.AppendKey(label);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ---- transparent click-through caption window ----
    private sealed class CaptionForm : Form
    {
        private readonly Label _label;

        public CaptionForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = System.Drawing.Color.Magenta;       // chroma key
            TransparencyKey = System.Drawing.Color.Magenta; // make background see-through
            _label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 120,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(40, 40, 40),
                Font = new System.Drawing.Font("Segoe UI", 28f, System.Drawing.FontStyle.Bold),
            };
            Controls.Add(_label);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void AppendKey(string label)
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                var existing = _label.Text;
                var combined = string.IsNullOrEmpty(existing) ? label : existing + "  " + label;
                if (combined.Length > 40) combined = combined[^40..];
                _label.Text = combined;
            });
        }

        public void SetText(string text)
        {
            if (IsDisposed) return;
            BeginInvoke(() => _label.Text = text);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}

internal static class KeyLabels
{
    public static string Describe(Keys key)
    {
        var mods = "";
        if ((Control.ModifierKeys & Keys.Control) != 0) mods += "Ctrl+";
        if ((Control.ModifierKeys & Keys.Alt) != 0)     mods += "Alt+";
        if ((Control.ModifierKeys & Keys.Shift) != 0)   mods += "Shift+";

        var name = key switch
        {
            Keys.Space => "Space",
            Keys.Return => "Enter",
            Keys.Escape => "Esc",
            Keys.Tab => "Tab",
            Keys.Back => "Backspace",
            Keys.Delete => "Del",
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.A and <= Keys.Z => key.ToString(),
            >= Keys.F1 and <= Keys.F12 => key.ToString(),
            Keys.Up => "↑", Keys.Down => "↓", Keys.Left => "←", Keys.Right => "→",
            Keys.LControlKey or Keys.RControlKey or Keys.ControlKey => "",
            Keys.LMenu or Keys.RMenu or Keys.Menu => "",
            Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey => "",
            Keys.LWin or Keys.RWin => "",
            _ => key.ToString(),
        };
        // For a bare modifier press, show just the modifier chord (drop trailing +).
        if (name.Length == 0) return mods.TrimEnd('+');
        return mods + name;
    }
}
```

> Design notes baked into the code: the caption window uses `TransparencyKey` so only the dark caption bar is visible; `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` make it click-through and non-focus-stealing; `Bounds = monitor.Bounds` pins it to the recorded monitor (coords may be negative); `DpiChanged` re-pins for mixed-DPI. It never calls `SetWindowDisplayAffinity`, so it composites into the capture.

- [ ] **Step 2: Build**

Run: `dotnet build src/SPRecorder/SPRecorder.csproj -c Debug`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add src/SPRecorder/Overlay/InputHighlightOverlay.cs
git commit -m "feat: add key-caster overlay with low-level keyboard hook"
```

---

## Task 6: Wire screen + overlay into `RecordingSession`

**Files:**
- Modify: `src/SPRecorder/Recording/RecordingSession.cs`

**Interfaces:**
- Consumes: `ScreenRecorder` (Task 4), `InputHighlightOverlay` (Task 5), `FileNameBuilder.BuildScreen` (Task 3), `AppConfig` screen fields (Task 2).
- Produces: `public string? ScreenFilePath { get; private set; }` on `RecordingSession`; screen + overlay start/stop under the toggle, isolated from audio.

- [ ] **Step 1: Add fields, usings, and property**

In `src/SPRecorder/Recording/RecordingSession.cs`, add `using SPRecorder.Overlay;` to the usings. Add fields next to the audio capture fields:

```csharp
    private ScreenRecorder? _screenRecorder;
    private InputHighlightOverlay? _overlay;
```

Add the public property next to `MixedFilePath`:

```csharp
    public string? ScreenFilePath { get; private set; }
```

- [ ] **Step 2: Start the screen track in `Start()`**

In `Start()`, after the audio captures are started (`_systemCapture.Start(); _micCapture.Start();`) and before `CurrentState = State.Recording;`, add:

```csharp
        if (_activeConfig.ScreenRecordingEnabled)
            StartScreenTrack();
```

Add the helper method (screen failure must never break audio — note the broad catch):

```csharp
    private void StartScreenTrack()
    {
        try
        {
            ScreenFilePath = Path.Combine(_activeConfig.OutputDirectory,
                FileNameBuilder.BuildScreen(_activeConfig.FileNamePattern, _startedAt));

            if (_activeConfig.ShowKeystrokes)
            {
                var monitor = MonitorForDevice(_activeConfig.ScreenMonitorDeviceName);
                _overlay = new InputHighlightOverlay();
                _overlay.Show(monitor);
            }

            _screenRecorder = new ScreenRecorder();
            _screenRecorder.Failed += msg => Warning?.Invoke(msg);
            _screenRecorder.Start(ScreenFilePath, _activeConfig);
        }
        catch (Exception ex)
        {
            Warning?.Invoke("Screen recording could not start; continuing with audio only. " + ex.Message);
            try { _overlay?.HideAndDispose(); } catch { /* ignore */ }
            _overlay = null;
            _screenRecorder = null;
            ScreenFilePath = null;
        }
    }

    private static System.Windows.Forms.Screen MonitorForDevice(string deviceName)
    {
        if (!string.IsNullOrEmpty(deviceName))
        {
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                if (s.DeviceName == deviceName) return s;
        }
        return System.Windows.Forms.Screen.PrimaryScreen!;
    }
```

> `Screen.DeviceName` and ScreenRecorderLib's `DeviceName` both follow the `\\.\DISPLAYn` convention; if a mismatch is observed during smoke testing, map via position instead. The wrapper's own primary-fallback (Task 4) still protects the capture even if the overlay picks the wrong monitor.

- [ ] **Step 3: Stop the screen track in `Stop()`**

In `Stop()`, after the audio captures are stopped/disposed and `_micCapture = null;`, and **before** `CurrentState = State.Idle;`, add:

```csharp
        try { _screenRecorder?.Stop(); } catch (Exception ex) { Warning?.Invoke("Screen stop: " + ex.Message); }
        try { _overlay?.HideAndDispose(); } catch { /* ignore */ }
        _screenRecorder?.Dispose();
        _screenRecorder = null;
        _overlay = null;
```

This finalizes the MP4 live; it deliberately does NOT enter `StartPostProcessingInBackground` (which stays audio-only — ADR 0005).

- [ ] **Step 4: Include the MP4 in the session-folder rename**

In `TryRenameToSessionFolder()`, after the mixed path is computed and the audio files are moved, add the screen move. Replace the block that sets `MixedFilePath = newMixed;` so it also handles the MP4:

```csharp
            SystemFilePath = newSystem;
            MicFilePath    = newMic;
            MixedFilePath  = newMixed;

            if (ScreenFilePath is not null && File.Exists(ScreenFilePath))
            {
                var newScreen = Path.Combine(folder, $"{folderName}_screen.mp4");
                File.Move(ScreenFilePath, newScreen);
                ScreenFilePath = newScreen;
            }
```

- [ ] **Step 5: Cover the screen track in `Dispose()`**

`Dispose()` already calls `Stop()` when recording, which now tears down the screen track. No change needed — confirm by reading the method.

- [ ] **Step 6: Build and run existing tests**

Run: `dotnet build src/SPRecorder/SPRecorder.csproj -c Debug && dotnet test`
Expected: BUILD SUCCEEDED; all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SPRecorder/Recording/RecordingSession.cs
git commit -m "feat: record screen track alongside audio, isolated from failures"
```

---

## Task 7: "Screen" tab in Settings with monitor picker

**Files:**
- Modify: `src/SPRecorder/Settings/SettingsForm.cs`

**Interfaces:**
- Consumes: `ScreenRecorder.GetDisplays()` (Task 4); `AppConfig` screen fields (Task 2).
- Produces: a new tab whose controls read/write `ScreenRecordingEnabled`, `ScreenMonitorDeviceName`, `ScreenFrameRate`, `ScreenQuality`, `ShowMouseClicks`, `ShowKeystrokes` through the existing `ApplyConfigToControls` / `Save_Click` flow.

- [ ] **Step 1: Declare the controls and option records**

In `SettingsForm`, add fields alongside the split controls:

```csharp
    private CheckBox _screenEnabled = null!;
    private ComboBox _screenMonitor = null!;
    private ComboBox _screenFps = null!;
    private ComboBox _screenQuality = null!;
    private CheckBox _showMouseClicks = null!;
    private CheckBox _showKeystrokes = null!;
    private GroupBox _screenDetails = null!;
```

Add to the option records at the bottom (next to `DeviceOption`):

```csharp
    private sealed record MonitorOption(string DeviceName, string Display);
    private sealed record FpsOption(int Fps, string Display);
    private sealed record QualityOption(string Value, string Display);
```

- [ ] **Step 2: Register the tab**

In the constructor, after `tabs.TabPages.Add(BuildSplittingTab());`, add:

```csharp
        tabs.TabPages.Add(BuildScreenTab());
```

- [ ] **Step 3: Build the tab**

Add this method to `SettingsForm` (mirrors the existing tab builders):

```csharp
    // ---------- Screen tab ----------
    private TabPage BuildScreenTab()
    {
        var page = new TabPage("Screen") { Padding = new Padding(TabPad) };
        int y = TabPad;

        _screenEnabled = new CheckBox
        {
            Text = "Record screen too",
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 24),
        };
        _screenEnabled.CheckedChanged += (_, _) => _screenDetails.Enabled = _screenEnabled.Checked;
        page.Controls.Add(_screenEnabled);
        y += 24 + FieldGap;

        _screenDetails = new GroupBox
        {
            Text = "Screen options",
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 300),
        };
        page.Controls.Add(_screenDetails);

        int gy = 30;
        _screenDetails.Controls.Add(MakeLabel("Monitor", 16, gy));
        gy += 22 + LabelToInput;
        _screenMonitor = new ComboBox
        {
            Location = new Point(20, gy),
            Size = new Size(InputWidth - 60, InputHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(MonitorOption.Display),
        };
        _screenDetails.Controls.Add(_screenMonitor);
        gy += InputHeight + FieldGap;

        _screenDetails.Controls.Add(MakeLabel("Frame rate", 16, gy));
        gy += 22 + LabelToInput;
        _screenFps = new ComboBox
        {
            Location = new Point(20, gy),
            Size = new Size(220, InputHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(FpsOption.Display),
        };
        _screenFps.Items.AddRange(new object[]
        {
            new FpsOption(15, "15 fps (smallest)"),
            new FpsOption(25, "25 fps"),
            new FpsOption(30, "30 fps (smoothest)"),
        });
        _screenDetails.Controls.Add(_screenFps);
        gy += InputHeight + FieldGap;

        _screenDetails.Controls.Add(MakeLabel("Quality", 16, gy));
        gy += 22 + LabelToInput;
        _screenQuality = new ComboBox
        {
            Location = new Point(20, gy),
            Size = new Size(220, InputHeight),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(QualityOption.Display),
        };
        _screenQuality.Items.AddRange(new object[]
        {
            new QualityOption("Low",    "Low (smallest file)"),
            new QualityOption("Medium", "Medium (recommended)"),
            new QualityOption("High",   "High (sharpest)"),
        });
        _screenDetails.Controls.Add(_screenQuality);
        gy += InputHeight + FieldGap;

        _showMouseClicks = new CheckBox
        {
            Text = "Highlight mouse clicks",
            Location = new Point(20, gy),
            Size = new Size(InputWidth - 60, 24),
        };
        _screenDetails.Controls.Add(_showMouseClicks);
        gy += 26;

        _showKeystrokes = new CheckBox
        {
            Text = "Show keystrokes on screen",
            Location = new Point(20, gy),
            Size = new Size(InputWidth - 60, 24),
        };
        _screenDetails.Controls.Add(_showKeystrokes);
        gy += 24 + 2;
        var privacy = MakeHint("⚠  Shows every key you press in the video — avoid typing passwords while recording.", 38, gy);
        privacy.ForeColor = Color.DarkOrange;
        privacy.MaximumSize = new Size(InputWidth - 70, 0);
        privacy.AutoSize = true;
        _screenDetails.Controls.Add(privacy);

        return page;
    }

    private void PopulateMonitors()
    {
        _screenMonitor.Items.Clear();
        _screenMonitor.Items.Add(new MonitorOption("", "Primary monitor"));
        foreach (var d in ScreenRecorder.GetDisplays())
            _screenMonitor.Items.Add(new MonitorOption(d.DeviceName, d.FriendlyName));
    }
```

Add `using SPRecorder.Recording;` to the file's usings.

- [ ] **Step 4: Load config into the controls**

At the end of `ApplyConfigToControls`, add:

```csharp
        _screenEnabled.Checked = cfg.ScreenRecordingEnabled;
        _screenDetails.Enabled = cfg.ScreenRecordingEnabled;
        PopulateMonitors();
        SelectMonitor(cfg.ScreenMonitorDeviceName);
        SelectComboByValue(_screenFps, cfg.ScreenFrameRate, o => ((FpsOption)o!).Fps);
        SelectStringCombo(_screenQuality, cfg.ScreenQuality, o => ((QualityOption)o!).Value);
        _showMouseClicks.Checked = cfg.ShowMouseClicks;
        _showKeystrokes.Checked = cfg.ShowKeystrokes;
```

Add these helpers next to `SelectComboByValue`:

```csharp
    private void SelectMonitor(string deviceName)
    {
        for (int i = 0; i < _screenMonitor.Items.Count; i++)
            if (_screenMonitor.Items[i] is MonitorOption m && m.DeviceName == deviceName)
            { _screenMonitor.SelectedIndex = i; return; }
        if (_screenMonitor.Items.Count > 0) _screenMonitor.SelectedIndex = 0;
    }

    private static void SelectStringCombo(ComboBox combo, string target, Func<object?, string> getter)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (getter(combo.Items[i]) == target) { combo.SelectedIndex = i; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }
```

- [ ] **Step 5: Save the controls into the result**

In `Save_Click`, inside the `Result = _initial with { ... }` block, add:

```csharp
            ScreenRecordingEnabled  = _screenEnabled.Checked,
            ScreenMonitorDeviceName = (_screenMonitor.SelectedItem as MonitorOption)?.DeviceName ?? "",
            ScreenFrameRate         = ((FpsOption?)_screenFps.SelectedItem)?.Fps ?? 30,
            ScreenQuality           = ((QualityOption?)_screenQuality.SelectedItem)?.Value ?? "Medium",
            ShowMouseClicks         = _showMouseClicks.Checked,
            ShowKeystrokes          = _showKeystrokes.Checked,
```

- [ ] **Step 6: Build**

Run: `dotnet build src/SPRecorder/SPRecorder.csproj -c Debug`
Expected: BUILD SUCCEEDED.

- [ ] **Step 7: Manual smoke — Settings tab**

Run the app (`dotnet run --project src/SPRecorder`), open Settings → Screen tab. Verify: the monitor dropdown lists "Primary monitor" + each connected display; toggling "Record screen too" enables/disables the group; Save then reopen Settings shows the values persisted in `appsettings.json`.

- [ ] **Step 8: Commit**

```bash
git add src/SPRecorder/Settings/SettingsForm.cs
git commit -m "feat: add Screen settings tab with monitor picker and privacy hint"
```

---

## Task 8: Tray quick-toggle "Record screen too"

**Files:**
- Modify: `src/SPRecorder/Tray/TrayApp.cs`

**Interfaces:**
- Consumes: `AppConfigStore` (existing `Current`, `Save`, `ConfigChanged`).
- Produces: a checkable tray menu item that flips `ScreenRecordingEnabled` and persists it.

- [ ] **Step 1: Add the menu item field**

In `TrayApp`, add next to `_toggleItem`:

```csharp
    private readonly ToolStripMenuItem _screenItem;
```

- [ ] **Step 2: Create and insert the item**

In the constructor, after `_statusItem` is created and before building `menu`, add:

```csharp
        _screenItem = new ToolStripMenuItem("Record screen too", null, (_, _) => ToggleScreenSetting())
        {
            CheckOnClick = false,
            Checked = _store.Current.ScreenRecordingEnabled,
        };
```

Then add it to the menu right after the status item:

```csharp
        menu.Items.Add(_screenItem);
```

(Insert this line in the `menu.Items.Add(...)` sequence, immediately after `menu.Items.Add(_statusItem);`.)

- [ ] **Step 3: Implement the toggle handler**

Add the method:

```csharp
    private void ToggleScreenSetting()
    {
        var next = !_store.Current.ScreenRecordingEnabled;
        _store.Save(_store.Current with { ScreenRecordingEnabled = next });
    }
```

- [ ] **Step 4: Keep the checkmark in sync with config changes**

In `OnConfigChanged`, add (so Settings-dialog edits and tray clicks both reflect):

```csharp
        _screenItem.Checked = newConfig.ScreenRecordingEnabled;
```

- [ ] **Step 5: Build**

Run: `dotnet build src/SPRecorder/SPRecorder.csproj -c Debug`
Expected: BUILD SUCCEEDED.

- [ ] **Step 6: Manual smoke — tray toggle**

Run the app, right-click tray. Verify: "Record screen too" shows a checkmark matching the setting; clicking it flips the check and updates `ScreenRecordingEnabled` in `appsettings.json`; opening Settings reflects the same state.

- [ ] **Step 7: Commit**

```bash
git add src/SPRecorder/Tray/TrayApp.cs
git commit -m "feat: add tray quick-toggle for screen recording"
```

---

## Task 9: README — folder distribution, prerequisites, usage, privacy

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: nothing.
- Produces: docs matching the shipped behavior.

- [ ] **Step 1: Update the publish/distribution section**

In `README.md`, replace the single-file publish instructions and the "Copy `SPRecorder.exe` and `appsettings.json`" line with folder-distribution guidance:

```markdown
## Publish

```bash
dotnet publish src/SPRecorder/SPRecorder.csproj -c Release -r win-x64 --self-contained true
```

Output folder: `src/SPRecorder/bin/Release/net10.0-windows/win-x64/publish/`.

> Screen recording uses ScreenRecorderLib, whose native `ScreenRecorderLib.dll`
> cannot be embedded in a single-file `.exe`. **Copy the whole published folder**
> (it contains `SPRecorder.exe`, `ScreenRecorderLib.dll`, `appsettings.json`, and
> companion DLLs) to a location of your choice. No installer is required.
>
> Prerequisites on the target PC: the **Visual C++ Redistributable** and
> **Media Foundation** (both present on normal Windows 10/11; on N/KN editions
> install the Media Feature Pack). Build target is **x64**.
```

- [ ] **Step 2: Document the screen feature, usage, and privacy**

Add a feature bullet near the top description and a usage note. Append to the Usage section:

```markdown
### Screen recording (optional)

Enable **Record screen too** in Settings → Screen, or via the tray menu. When on,
recording also produces `<name>_screen.mp4` of the chosen monitor (default
primary) with the meeting audio embedded, plus a mouse-click highlight and an
on-screen display of the keys you press.

> **Privacy**: the keystroke display shows **every key you type**, so anything you
> type — including passwords — appears in the video. Turn off "Show keystrokes on
> screen" (Settings → Screen) before typing secrets, or avoid typing them while
> recording.
```

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: document screen recording, folder distribution, and privacy"
```

---

## Task 10: End-to-end manual verification (integration gate)

**Files:** none (verification only).

This task is the integration gate for the native/UI pieces that cannot be unit-tested (per the spec's Testing section). Do it on a machine with **two monitors at different DPI scales** if available.

- [ ] **Step 1: Single-monitor happy path**

Enable screen recording (default primary). Start recording, click around and type a short sentence, stop. Verify:
- `<timestamp>_screen.mp4` exists and plays with **both** your voice and system audio.
- Mouse clicks show a highlight; pressed keys appear as an on-screen caption in the video.
- The existing `_system.mp3` / `_mic.mp3` (and `_mixed.mp3` if enabled) are still produced.

- [ ] **Step 2: Monitor selection**

In Settings → Screen, pick a non-primary monitor; record briefly on that monitor. Verify the MP4 shows that monitor and the key caster caption appears **on the recorded monitor** (even if you type while focused on another monitor).

- [ ] **Step 3: Monitor-gone fallback**

Set a monitor, then unplug/disable it (or pick one, disconnect, and restart the app). Start recording. Verify a warning balloon appears and the primary monitor is recorded instead — recording does not crash.

- [ ] **Step 4: Video failure does not kill audio**

Temporarily force a screen failure (e.g. rename/remove `ScreenRecorderLib.dll` in a copied publish folder, or run where WGC/DD is unavailable). Start and stop a recording. Verify: a warning is shown, **the audio MP3s are still produced**, and the app stays responsive.

- [ ] **Step 5: Session folder includes the MP4**

Enable "Ask for session name", record with screen on, stop, and name the session. Verify the MP4 moved into `OutputDirectory/<name>_<timestamp>/` alongside the MP3s.

- [ ] **Step 6: Splitting untouched**

Enable size/time splitting. Record with screen on. Verify the MP3 tracks split as before and the **MP4 is not split** (one `_screen.mp4`).

- [ ] **Step 7: Final full test run**

Run: `dotnet test`
Expected: all unit tests PASS. Record the manual results (pass/fail per step) in the PR description.

---

## Self-Review (completed during authoring)

- **Spec coverage:** MP4+embedded audio → Task 4; opt-in toggle (setting + tray) → Tasks 2/7/8; monitor selection + primary fallback → Tasks 2/4/6/7; key caster (all keys) + mouse highlight → Tasks 4/5; overlay pinned to recorded monitor + PerMonitorV2 + negative coords → Tasks 1/5/6; `_screen.mp4` naming → Task 3; session-folder rename → Task 6; video bypasses mix/split + audio-survives-failure → Task 6 (+ verified in Task 10/4 & 10/6); folder distribution + x64 + prerequisites → Tasks 1/9; privacy warning → Tasks 7/9. All spec sections map to a task.
- **Placeholder scan:** the one "verify against the installed ScreenRecorderLib version" note (Task 4) is deliberate guidance for an external native API the plan cannot execute, not a missing design — the wrapper's public surface is fully specified so dependents are unaffected.
- **Type consistency:** `ScreenRecorder.GetDisplays()` returns `IReadOnlyList<DisplayInfo>` (used in Tasks 6/7); `DisplayInfo(DeviceName, FriendlyName)`; `ScreenRecorder.Start(string, AppConfig)` / `Stop()` / `Failed` / `ResolvedDeviceName` used consistently in Task 6; `InputHighlightOverlay.Show(Screen)` / `HideAndDispose()` used in Task 6; config field names identical across Tasks 2/4/6/7/8; `FileNameBuilder.BuildScreen` defined in Task 3 and used in Task 6.
