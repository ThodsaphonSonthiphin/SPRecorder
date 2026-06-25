# Inactive-Hotkey Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a global hotkey that fails to register (conflict with another app) durably discoverable — a persistent tray badge + menu item + one consolidated startup balloon + a per-key Settings indicator — sourced from `GlobalHotkey.IsRegistered`.

**Architecture:** A new pure `HotkeyStatus` record carries the three hotkeys' live registration flags. `TrayApp` builds it after `RegisterHotkeys`, raises one consolidated balloon, and refreshes the tray icon (a badged variant from `IconFactory`), a consolidated menu item, and the tooltip. `OpenSettings` passes the status into `SettingsForm`, which shows a non-probing "inactive" hint on each conflicting hotkey control. Resolution = rebind + Save → `RegisterHotkeys` re-runs → indicators clear.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), Windows Forms, System.Drawing, xUnit. Builds on the markers hotkey infrastructure already on branch `feat/markers`. Spec: [docs/superpowers/specs/2026-06-25-inactive-hotkey-notification-design.md](../specs/2026-06-25-inactive-hotkey-notification-design.md); ADRs 0015–0018.

## Global Constraints

- Branch `feat/markers`; target `net10.0-windows`, x64, Windows-only. No new NuGet dependencies.
- Source of truth = `GlobalHotkey.IsRegistered` (a hotkey is **active** iff `hk is { IsRegistered: true }`); **never re-probe** (re-`RegisterHotKey` of an owned combo is a false positive — ADR 0015).
- Hotkey display names (verbatim): `"Start/stop"`, `"Quick-mark"`, `"Mark with note"`.
- Startup: **one consolidated balloon**, not per-key; remove the per-key balloon from `MakeHotkey` (ADR 0016).
- Tray badge color amber `Color.FromArgb(255, 193, 7)`; composited over the existing idle (`Color.Gray`) / recording (`Color.FromArgb(198, 40, 40)`) icons; pre-created, disposed in `ExitThreadCore` (ADR 0017).
- Settings inactive hint text (verbatim): `"  ⚠  Currently inactive — in use by another app"`. Showing it must NOT block Save (`Save_Click` validates via `HotkeyValidation.Validate`, not `HasConflict`).
- No background polling, no re-check command; indicators refresh only after `RegisterHotkeys` (startup + Save) — ADR 0018.
- Match existing idioms (init-only records, `ShowBalloon`, the icon/menu/tooltip patterns). Build: `dotnet build`; test: `dotnet test`.
- WinForms/Win32/tray paths are build-gated + manual (the project's established strategy); only `HotkeyStatus` (and an `IconFactory` smoke test) are unit-testable.

---

### Task 1: `HotkeyStatus` record

**Files:**
- Create: `src/SPRecorder/Hotkey/HotkeyStatus.cs`
- Test: `tests/SPRecorder.Tests/HotkeyStatusTests.cs`

**Interfaces:**
- Produces: `record HotkeyStatus(bool StartStop, bool QuickMark, bool MarkWithNote)` with `bool AnyInactive` and `IReadOnlyList<string> InactiveLabels()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/SPRecorder.Tests/HotkeyStatusTests.cs`:

```csharp
using SPRecorder.Hotkey;

namespace SPRecorder.Tests;

public class HotkeyStatusTests
{
    [Fact]
    public void AnyInactive_AllRegistered_False()
        => Assert.False(new HotkeyStatus(true, true, true).AnyInactive);

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void AnyInactive_OneFailed_True(bool a, bool b, bool c)
        => Assert.True(new HotkeyStatus(a, b, c).AnyInactive);

    [Fact]
    public void InactiveLabels_AllRegistered_Empty()
        => Assert.Empty(new HotkeyStatus(true, true, true).InactiveLabels());

    [Fact]
    public void InactiveLabels_QuickMarkOnly()
        => Assert.Equal(new[] { "Quick-mark" }, new HotkeyStatus(true, false, true).InactiveLabels());

    [Fact]
    public void InactiveLabels_AllInactive_InOrder()
        => Assert.Equal(new[] { "Start/stop", "Quick-mark", "Mark with note" },
                        new HotkeyStatus(false, false, false).InactiveLabels());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~HotkeyStatusTests"`
Expected: FAIL — `HotkeyStatus` not defined (compile error).

- [ ] **Step 3: Implement `HotkeyStatus`**

Create `src/SPRecorder/Hotkey/HotkeyStatus.cs`:

```csharp
namespace SPRecorder.Hotkey;

/// <summary>Live registration status of the three global hotkeys (true = registered/active).</summary>
public sealed record HotkeyStatus(bool StartStop, bool QuickMark, bool MarkWithNote)
{
    public bool AnyInactive => !StartStop || !QuickMark || !MarkWithNote;

    /// <summary>Human-readable names of the inactive hotkeys, in display order.</summary>
    public IReadOnlyList<string> InactiveLabels()
    {
        var list = new List<string>(3);
        if (!StartStop)    list.Add("Start/stop");
        if (!QuickMark)    list.Add("Quick-mark");
        if (!MarkWithNote) list.Add("Mark with note");
        return list;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~HotkeyStatusTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/SPRecorder/Hotkey/HotkeyStatus.cs tests/SPRecorder.Tests/HotkeyStatusTests.cs
git commit -m "feat: add HotkeyStatus (live registration status of the three hotkeys)"
```

---

### Task 2: `IconFactory.CreateCircleWithBadge`

**Files:**
- Modify: `src/SPRecorder/Tray/IconFactory.cs`
- Test: `tests/SPRecorder.Tests/IconFactoryTests.cs`

**Interfaces:**
- Produces: `IconFactory.CreateCircleWithBadge(Color baseColor, Color badgeColor, int size = 32) → Icon` — the base circle (identical to `CreateCircle`) with a small badge dot composited in the lower-right corner.

- [ ] **Step 1: Write the failing smoke test**

Create `tests/SPRecorder.Tests/IconFactoryTests.cs`:

```csharp
using System.Drawing;
using SPRecorder.Tray;

namespace SPRecorder.Tests;

public class IconFactoryTests
{
    [Fact]
    public void CreateCircleWithBadge_ReturnsUsableIcon()
    {
        using var icon = IconFactory.CreateCircleWithBadge(Color.Gray, Color.FromArgb(255, 193, 7));
        Assert.NotNull(icon);
        Assert.True(icon.Width > 0 && icon.Height > 0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IconFactoryTests"`
Expected: FAIL — `CreateCircleWithBadge` not defined (compile error).

- [ ] **Step 3: Implement `CreateCircleWithBadge`**

In `src/SPRecorder/Tray/IconFactory.cs`, add after `CreateCircle` (mirrors it, then draws the badge):

```csharp
    public static Icon CreateCircleWithBadge(Color baseColor, Color badgeColor, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            int pad = size / 8;
            using (var brush = new SolidBrush(baseColor))
                g.FillEllipse(brush, pad, pad, size - 2 * pad, size - 2 * pad);

            // Warning badge: a filled dot with a thin contrasting outline, lower-right.
            int d = size * 7 / 16;                 // badge diameter (~14px at 32)
            int x = size - d - 1, y = size - d - 1;
            using (var outline = new SolidBrush(Color.FromArgb(40, 40, 40)))
                g.FillEllipse(outline, x - 1, y - 1, d + 2, d + 2);
            using (var badge = new SolidBrush(badgeColor))
                g.FillEllipse(badge, x, y, d, d);
        }
        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~IconFactoryTests"`
Expected: PASS. (If `System.Drawing`/GDI is unavailable on the runner, report it — the method is still validated by the build + manual tray check in Task 5.)

- [ ] **Step 5: Commit**

```bash
git add src/SPRecorder/Tray/IconFactory.cs tests/SPRecorder.Tests/IconFactoryTests.cs
git commit -m "feat: add IconFactory.CreateCircleWithBadge (warning badge overlay)"
```

---

### Task 3: `HotkeyCaptureControl.SetInactiveStatus`

**Files:**
- Modify: `src/SPRecorder/Settings/HotkeyCaptureControl.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `void SetInactiveStatus(bool inactive)` — shows/clears a non-probing "currently inactive" hint and sets `HasConflict`.

> No unit test (WinForms `UserControl` with private label); build-gated + manual (Task 4/5).

- [ ] **Step 1: Add `SetInactiveStatus`**

In `src/SPRecorder/Settings/HotkeyCaptureControl.cs`, add right after the `SetInitialHotkey` method (after its closing brace, near line 50):

```csharp
    /// <summary>
    /// Show (or clear) an externally-determined "inactive" hint for the loaded hotkey,
    /// WITHOUT probing — the authoritative source is the app's own GlobalHotkey.IsRegistered.
    /// Re-probing here would always report a conflict for a combo the app already owns.
    /// </summary>
    public void SetInactiveStatus(bool inactive)
    {
        HasConflict = inactive;
        _conflictHint.Text = inactive ? "  ⚠  Currently inactive — in use by another app" : "";
    }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/SPRecorder/Settings/HotkeyCaptureControl.cs
git commit -m "feat: add HotkeyCaptureControl.SetInactiveStatus (non-probing inactive hint)"
```

---

### Task 4: `SettingsForm` — accept + display live status

**Files:**
- Modify: `src/SPRecorder/Settings/SettingsForm.cs`

**Interfaces:**
- Consumes: `HotkeyStatus` (Task 1), `HotkeyCaptureControl.SetInactiveStatus` (Task 3). The form already has `_hotkey` (General tab), `_quickMarkHotkey`, `_markWithNoteHotkey` (Markers tab) controls.
- Produces: `SettingsForm(AppConfig initial, bool isRecording, HotkeyStatus? hotkeyStatus = null)`.

> No unit test (WinForms); build-gated + manual (Task 5).

- [ ] **Step 1: Add a field + accept the status in the ctor**

In `src/SPRecorder/Settings/SettingsForm.cs`: add `using SPRecorder.Hotkey;` at the top if not present (it is — `HotkeyParser`/`HotkeyValidation` live there). Add a field near the other `private readonly` fields (e.g. beside `_initial`/`_isRecording`):

```csharp
    private readonly HotkeyStatus? _hotkeyStatus;
```

Change the ctor signature and capture the parameter. Find:

```csharp
    public SettingsForm(AppConfig initial, bool isRecording)
    {
        _initial = initial;
        _isRecording = isRecording;
```

Replace with:

```csharp
    public SettingsForm(AppConfig initial, bool isRecording, HotkeyStatus? hotkeyStatus = null)
    {
        _initial = initial;
        _isRecording = isRecording;
        _hotkeyStatus = hotkeyStatus;
```

- [ ] **Step 2: Apply inactive status in `ApplyConfigToControls`**

In `ApplyConfigToControls`, locate the three `SetInitialHotkey` calls (start/stop on the General tab, the two marker controls added on the Markers tab):

```csharp
        _hotkey.SetInitialHotkey(cfg.Hotkey);
        ...
        _quickMarkHotkey.SetInitialHotkey(cfg.QuickMarkHotkey);
        _markWithNoteHotkey.SetInitialHotkey(cfg.MarkWithNoteHotkey);
```

Immediately after the LAST of these three `SetInitialHotkey` calls, add (the controls all exist by then):

```csharp
        if (_hotkeyStatus is { } hs)
        {
            _hotkey.SetInactiveStatus(!hs.StartStop);
            _quickMarkHotkey.SetInactiveStatus(!hs.QuickMark);
            _markWithNoteHotkey.SetInactiveStatus(!hs.MarkWithNote);
        }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded`. (Existing `new SettingsForm(cfg, isRecording)` call sites still compile via the optional `hotkeyStatus = null`.)

- [ ] **Step 4: Run the full suite (no regressions)**

Run: `dotnet test`
Expected: PASS — all existing + Task 1/2 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/SPRecorder/Settings/SettingsForm.cs
git commit -m "feat: SettingsForm shows per-key inactive status from live HotkeyStatus"
```

---

### Task 5: `TrayApp` — badge, menu, consolidated balloon, refresh

**Files:**
- Modify: `src/SPRecorder/Tray/TrayApp.cs`

**Interfaces:**
- Consumes: `HotkeyStatus` (Task 1), `IconFactory.CreateCircleWithBadge` (Task 2), `SettingsForm(.., .., HotkeyStatus?)` (Task 4).

> No unit test (WinForms/Win32/tray). Gate = clean build + the manual checklist in Step 9 (HUMAN gate — needs a desktop + a real hotkey conflict; do not attempt to drive the GUI).

- [ ] **Step 1: Add badged-icon fields + the conflict menu item field**

In `src/SPRecorder/Tray/TrayApp.cs`, beside `private readonly Icon _idleIcon;` / `_recIcon;` add:

```csharp
    private readonly Icon _idleIconBadged;
    private readonly Icon _recIconBadged;
```

Beside `_quickMarkItem` / `_noteMarkItem` add:

```csharp
    private ToolStripMenuItem _hotkeyConflictItem = null!;
```

- [ ] **Step 2: Create the badged icons in the ctor**

After the existing `_idleIcon = IconFactory.CreateCircle(Color.Gray);` / `_recIcon = IconFactory.CreateCircle(Color.FromArgb(198, 40, 40));` lines, add:

```csharp
        _idleIconBadged = IconFactory.CreateCircleWithBadge(Color.Gray, Color.FromArgb(255, 193, 7));
        _recIconBadged  = IconFactory.CreateCircleWithBadge(Color.FromArgb(198, 40, 40), Color.FromArgb(255, 193, 7));
```

- [ ] **Step 3: Create + insert the conflict menu item**

Where `_quickMarkItem` / `_noteMarkItem` are created (near the other `new ToolStripMenuItem(...)`), add:

```csharp
        _hotkeyConflictItem = new ToolStripMenuItem("⚠ Hotkey(s) inactive — open Settings", null, (_, _) => OpenSettings())
        {
            Visible = false,
        };
```

In the menu assembly, insert it right after `menu.Items.Add(_statusItem);`:

```csharp
        menu.Items.Add(_statusItem);
        menu.Items.Add(_hotkeyConflictItem);
```

- [ ] **Step 4: Add status helpers (`CurrentHotkeyStatus`, `ApplyTrayIcon`, `HotkeyConflictSuffix`, `RefreshHotkeyStatusIndicators`)**

Add these methods (e.g. just after `RegisterHotkeys`/`MakeHotkey`):

```csharp
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
            _hotkeyConflictItem.Text = $"⚠ {string.Join(", ", status.InactiveLabels())} hotkey inactive — open Settings";

        ApplyTrayIcon();

        if (_session.CurrentState == RecordingSession.State.Recording)
            UpdateTooltip();
        else
            _notifyIcon.Text = "SPRecorder — idle" + HotkeyConflictSuffix();
    }
```

- [ ] **Step 5: Consolidate the balloon in `RegisterHotkeys`; strip the per-key balloon from `MakeHotkey`**

Replace the body of `RegisterHotkeys` (keep the three `MakeHotkey` lines) so it ends by raising ONE balloon and refreshing:

```csharp
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
            var labels = string.Join(", ", status.InactiveLabels());
            ShowBalloon(ToolTipIcon.Warning, $"{status.InactiveLabels().Count} hotkey(s) inactive",
                $"{labels} couldn't register (in use by another app). Open Settings to fix.");
        }

        RefreshHotkeyStatusIndicators();
    }
```

Replace `MakeHotkey` so it no longer shows a per-key balloon (registration failure is now surfaced by the consolidated balloon + indicators; a parse error returns null and is treated as inactive):

```csharp
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
```

(The ctor already calls `RegisterHotkeys();`, which now refreshes the indicators on startup. `OnConfigChanged` already calls `RegisterHotkeys()` when a hotkey spec changes, so a rebind+Save refreshes too — no extra calls needed.)

- [ ] **Step 6: Use `ApplyTrayIcon()` + conflict suffix in `OnStateChanged`**

In `OnStateChanged`, in the **Recording** branch replace `_notifyIcon.Icon = _recIcon;` with `ApplyTrayIcon();`.
In the **Idle** branch replace `_notifyIcon.Icon = _idleIcon;` with `ApplyTrayIcon();`, and replace `_notifyIcon.Text = "SPRecorder — idle";` with:

```csharp
            _notifyIcon.Text = "SPRecorder — idle" + HotkeyConflictSuffix();
```

- [ ] **Step 7: Append the conflict suffix in `UpdateTooltip`**

Replace `UpdateTooltip` so the recording tooltip carries the suffix (truncation guard preserved):

```csharp
    private void UpdateTooltip()
    {
        var elapsed = _session.Elapsed ?? TimeSpan.Zero;
        var markers = _markerCount > 0 ? $" · {_markerCount} markers" : "";
        var text = $"Recording… {elapsed:hh\\:mm\\:ss}{markers}{HotkeyConflictSuffix()}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        _statusItem.Text = $"Recording for {elapsed:hh\\:mm\\:ss}{markers}";
    }
```

- [ ] **Step 8: Pass `HotkeyStatus` into Settings + dispose badged icons**

In `OpenSettings`, pass the live status:

```csharp
        using var form = new SettingsForm(_store.Current, isRecording, CurrentHotkeyStatus());
```

In `ExitThreadCore`, after `_idleIcon.Dispose(); _recIcon.Dispose();` add:

```csharp
        _idleIconBadged.Dispose();
        _recIconBadged.Dispose();
```

- [ ] **Step 9: Build, run suite, then HUMAN manual gate**

Run: `dotnet build` → `Build succeeded`; then `dotnet test` → all green (no new unit tests this task).

Step 9 manual checks (HUMAN — needs a desktop + a real conflict; do NOT drive the GUI yourself, record as a gate):
- Bind a marker hotkey to a combo another app owns (or temporarily change a default to a known-taken combo), launch → the tray icon shows the amber badge; ONE balloon "N hotkey(s) inactive …"; right-click shows "⚠ … hotkey inactive — open Settings".
- Click the menu item → Settings opens; the conflicting key shows "⚠ Currently inactive — in use by another app"; non-conflicting keys show nothing.
- Rebind the conflicting key to a free combo → Save → the badge + menu item disappear, tooltip is clean, and the new key works.
- Start recording with a conflict present → the badge composites over the red icon.
- No conflict (all three register) → no badge, no menu item, no balloon (unchanged behavior).

- [ ] **Step 10: Commit**

```bash
git add src/SPRecorder/Tray/TrayApp.cs
git commit -m "feat: surface inactive hotkeys via tray badge, menu, consolidated balloon + Settings status"
```

---

## Self-Review

**1. Spec coverage:**
- §2 `HotkeyStatus` (AnyInactive, InactiveLabels, source rule) → **Task 1** ✓
- §3.1 badged icon (`CreateCircleWithBadge`, pre-created variants, disposal) → **Task 2** + **Task 5 Steps 1,2,8** ✓
- §3.2 icon selection (recording × inactive) → **Task 5 Step 4 `ApplyTrayIcon`** + **Step 6** ✓
- §3.3 menu item + tooltip suffix + consolidated balloon + per-key balloon removed → **Task 5 Steps 3,5,6,7** ✓
- §3.4 single refresh point after RegisterHotkeys (startup + OnConfigChanged) → **Task 5 Step 5** (`RefreshHotkeyStatusIndicators` called at the end of `RegisterHotkeys`; ctor + OnConfigChanged already call it) ✓
- §4 SettingsForm ctor `HotkeyStatus?` + `SetInactiveStatus` wiring; HasConflict doesn't block Save → **Task 4** + **Task 3** ✓
- §5 runtime lifecycle (rebind clears indicators) → **Task 5 Step 5** (OnConfigChanged→RegisterHotkeys→refresh) ✓
- §6 component table → Tasks 1–5 ✓
- §7 edge cases: all-inactive (InactiveLabels lists all), parse error → null → inactive (MakeHotkey catch), cleared-by-rebind (refresh), inactive-while-recording (ApplyTrayIcon composes over red), balloon suppressed (badge/menu/Settings fallback), open-Settings-no-change-Save (re-register fails again, indicators stay) → covered by Tasks 1/4/5 ✓
- §9 testing (HotkeyStatus unit + IconFactory smoke + manual) → Tasks 1,2,5 ✓

No gaps.

**2. Placeholder scan:** No TBD/"handle edge cases"/bare-prose code steps — every code step shows complete code.

**3. Type consistency:** `HotkeyStatus(bool StartStop, bool QuickMark, bool MarkWithNote)`, `.AnyInactive`, `.InactiveLabels()`, `IconFactory.CreateCircleWithBadge(Color, Color, int=32)`, `HotkeyCaptureControl.SetInactiveStatus(bool)`, `SettingsForm(AppConfig, bool, HotkeyStatus?=null)`, `TrayApp.CurrentHotkeyStatus()/ApplyTrayIcon()/HotkeyConflictSuffix()/RefreshHotkeyStatusIndicators()` — names/signatures consistent across all consuming tasks. `IsRegistered` active-check (`is { IsRegistered: true }`) used identically in TrayApp helpers and OpenSettings.
