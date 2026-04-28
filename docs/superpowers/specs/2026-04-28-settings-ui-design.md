# SPRecorder — Settings UI

## Context

SPRecorder is currently configured exclusively via `appsettings.json` next to the `.exe`. To change the hotkey, output directory, MP3 bitrate, etc., the user must open the JSON file in a text editor and restart the app. There is no in-app way to discover or change settings.

This work adds a **Settings dialog** opened from the tray menu, exposing all current and several new configuration options through a tabbed Windows Forms UI. Changes apply live where possible (no restart required).

The same release also adds a **named-session feature** so the user can label each recording (e.g., "Q2 Planning") with a folder/filename that makes the topic obvious when files are uploaded to NotebookLM or shared with others — without forcing them to label every recording (it's an opt-in toggle).

## Tech additions

- No new NuGet packages. WinForms (`NotifyIcon`, `Form`, `TabControl`, `MMDeviceEnumerator` from existing NAudio) covers everything.
- Settings persistence: read/write the same `appsettings.json` next to the `.exe` (the user explicitly chose this over `%AppData%`).

## Data model — extended `AppConfig`

`Configuration/AppConfig.cs` gains five fields. All have defaults so existing `appsettings.json` files continue to load.

```csharp
public sealed record AppConfig
{
    // existing
    public string OutputDirectory { get; init; }
    public string FileNamePattern { get; init; }
    public string Hotkey { get; init; }
    public int    Mp3BitrateKbps { get; init; }

    // new — audio devices ("" = Windows default)
    public string MicrophoneDeviceId  { get; init; } = "";
    public string SystemAudioDeviceId { get; init; } = "";

    // new — mixed file
    public bool   MixedFileEnabled    { get; init; } = true;
    public string MixedFileFormat     { get; init; } = "Mono";   // "Mono" | "Stereo"
    public int    MixedFileSampleRate { get; init; } = 44100;

    // new — named session
    public bool   PromptForSessionName { get; init; } = false;
}
```

## New components

```
src/SPRecorder/Settings/
  AppConfigStore.cs            Load + Save appsettings.json (System.Text.Json),
                               raises ConfigChanged event
  AudioDeviceEnumerator.cs     Lists active render + capture devices via
                               MMDeviceEnumerator (NAudio.CoreAudioApi)
  SettingsForm.cs              WinForms dialog: TabControl with 3 pages,
                               Save/Cancel footer, validation
  HotkeyCaptureControl.cs      Custom UserControl: click → captures next
                               modifier+key combo → displays "Ctrl+Alt+R"
  SessionNamePrompt.cs         Tiny modal (TextBox + OK/Cancel) shown after
                               recording stops when PromptForSessionName=true
```

## Updates to existing files

| File | Change |
|---|---|
| `Audio/SystemAudioCapture.cs` | Constructor takes optional `string deviceId`. Empty → default render device. |
| `Audio/MicrophoneCapture.cs`  | Same pattern: optional `deviceId`. |
| `Audio/Mp3Mixer.cs`           | Add `MixToStereo(systemMp3, micMp3, output, bitrate, rate)` overload using `MultiplexingSampleProvider` (L=system, R=mic). |
| `Recording/RecordingSession.cs` | Pass device IDs from config. After Stop & writers disposed: if `PromptForSessionName`, open `SessionNamePrompt`; on OK, move 2 source files into `{name}_{timestamp}/` and rename. Pick mono vs stereo mixer based on `MixedFileFormat`. Skip mixing entirely when `MixedFileEnabled=false`. |
| `Tray/TrayApp.cs` | New menu item "Settings…" between "Open recordings folder" and "About". Holds reference to `AppConfigStore`; on `ConfigChanged` → re-register hotkey if changed, refresh tooltip. |
| `Program.cs` | Build `AppConfigStore` (singleton); pass it to `TrayApp` instead of a static `AppConfig`. |

## Settings dialog — UX

**Window:** modal `Form`, `StartPosition = CenterScreen`, ~560 × 440 px, fixed-size dialog (no resize/maximize), title "SPRecorder — Settings".

**Contents:** `TabControl` with three tabs:

### Tab "General"
- Output directory: textbox + Browse… button (opens `FolderBrowserDialog`)
- File name pattern: textbox with hint text below
- Global hotkey: `HotkeyCaptureControl` (see below)
- MP3 bitrate: `ComboBox` — 64 / 96 (default) / 128 / 192 kbps
- `[ ]` Ask for session name after recording — checkbox + small hint underneath: *Saves files in OutputDirectory/&lt;your-name&gt;_&lt;timestamp&gt;/*

### Tab "Audio"
- Microphone: `ComboBox` populated by `AudioDeviceEnumerator.GetCaptureDevices()`, prepended with "Default — &lt;current default name&gt;"
- System audio (loopback): `ComboBox` populated by `GetRenderDevices()`, same default prefix
- "↻ Refresh device list" button — re-runs enumeration
- If recording is in progress, both ComboBoxes are disabled with a hint label *"Stop recording to change devices"*

### Tab "Mixed file"
- `[✓]` Generate mixed file after each recording (default on)
- Format: radio `(•) Mono (recommended for AI)  ( ) Stereo (L=system, R=mic)`
- Target sample rate: `ComboBox` — 22050 / 44100 (default) / 48000 Hz
- All controls in this tab are grayed out when the checkbox is unchecked

**Footer:** `[ Cancel ]  [ Save ]` aligned right.

### `HotkeyCaptureControl` behavior
- Default state: shows current hotkey text (e.g., `Ctrl + Alt + R`) + a small "click to change" badge.
- On click: enters capture mode (background turns light blue, text becomes `Press a key combo…`).
- Capture mode KeyDown handler: collects modifiers (`Ctrl`, `Alt`, `Shift`, `Win`) until a non-modifier key arrives, then sets the new value and exits capture mode.
- `Esc` while capturing: cancel, restore previous value.
- Live conflict check: immediately after a successful capture (i.e., the moment a new combo is captured), the control attempts a temporary `RegisterHotKey` then `UnregisterHotKey`; if registration fails, the control shows ⚠ "In use by another app" in orange below the field. Save is still permitted (stored in config) but the runtime startup balloon will warn.

## Save flow

1. User clicks **Save** in `SettingsForm`.
2. Validation runs:
   - `OutputDirectory` not empty, parent exists or `Directory.CreateDirectory` succeeds.
   - `FileNamePattern` contains `{track}` (otherwise the system/mic/mixed files would all collide).
   - `Hotkey` parses successfully via `HotkeyParser.Parse`.
3. If any validation fails: highlight the offending field's label in red, show a single MessageBox listing all errors, dialog stays open.
4. On success: `AppConfigStore.Save(newConfig)` writes the JSON atomically (write to temp file → `File.Replace`).
5. `AppConfigStore` raises `ConfigChanged(oldConfig, newConfig)`.
6. `TrayApp` handler:
   - If `Hotkey` changed: dispose old `GlobalHotkey`, create new one with the new combo. If registration fails, show balloon warning.
   - Other fields: take effect on next `RecordingSession.Start()` call.
7. Dialog closes; tray balloon: *"Settings saved"* (and *"Hotkey changed to X"* if applicable).

**Cancel** simply closes the dialog with no changes saved.

## Named-session feature flow

When `PromptForSessionName == true` and recording stops:

1. `RecordingSession.Stop()` closes both writers as today.
2. **Before** kicking off mixing, `RecordingSession` calls a `Func<string?>` provided at construction time (`TrayApp` supplies a delegate that opens `SessionNamePrompt` on the UI thread). Returning `null` or empty string is treated as Cancel.
3. The prompt dialog: text input with placeholder *"e.g. Q2 Planning"*, default-focused, `[ Cancel ]  [ OK ]`.
4. User responses:
   - **OK with non-empty name** → sanitize the name (replace invalid filename chars per `Path.GetInvalidFileNameChars()` with `_`, trim trailing dots/spaces). Build `folderName = {sanitized}_{timestamp:yyyy-MM-dd_HH-mm-ss}`. Create `{OutputDirectory}/{folderName}/`. Move and rename the two source MP3s to `{folderName}_system.mp3` / `{folderName}_mic.mp3` inside that folder. Update `RecordingSession.SystemFilePath`, `MicFilePath`, and `MixedFilePath` to point inside the new folder. Then kick off mixing into the new folder.
   - **Cancel / OK with empty name / Esc** → leave files where they are (current root + timestamp behaviour). Mix as today into root.
5. The dialog blocks the UI thread but does NOT block the captures (they're already stopped). The tray icon has already returned to gray.

When `PromptForSessionName == false` the entire feature is skipped — behavior is identical to today.

### File layout — toggle on
```
Documents/SPRecorder/
  Q2 Planning_2026-04-28_14-30-22/
    Q2 Planning_2026-04-28_14-30-22_system.mp3
    Q2 Planning_2026-04-28_14-30-22_mic.mp3
    Q2 Planning_2026-04-28_14-30-22_mixed.mp3
  Daily standup_2026-04-28_15-00-12/
    Daily standup_2026-04-28_15-00-12_system.mp3
    ...
```

### File layout — toggle off (unchanged from today)
```
Documents/SPRecorder/
  2026-04-28_14-30-22_system.mp3
  2026-04-28_14-30-22_mic.mp3
  2026-04-28_14-30-22_mixed.mp3
```

## Edge cases

| Situation | Behavior |
|---|---|
| User clears OutputDirectory and clicks Save | Validation fails, label red, MessageBox lists error |
| User picks device that is unplugged before next recording | NAudio throws on `Start()`. `RecordingSession.Warning` event → tray balloon. State stays Idle. |
| User changes hotkey while recording is in progress | New hotkey registered immediately. Old hotkey still works for the in-flight recording? No — old is unregistered. The currently-pressed-next instance of the new hotkey will stop the recording. Tray menu Stop also still works as a fallback. |
| User opens Settings while recording, changes audio device | Audio tab dropdowns are disabled; user sees hint and must Stop first. |
| User picks Stereo mix format then chooses default mono mic | Mixer still produces stereo MP3; mic data lands on R channel even if originally mono (mono is upmixed by the multiplexer). |
| User enters session name with `?:/\` characters | Sanitized to `_` per `Path.GetInvalidFileNameChars()`. |
| Same session name typed twice in quick succession | Folder names differ because `_timestamp` always differs. |
| User presses Cancel on session name modal | Files stay in root with timestamp names — same as toggle-off behavior. |
| User presses the global hotkey while the session-name prompt is open | Hotkey starts a fresh recording. The previous session's files (still in root with their timestamp names) are abandoned in place — equivalent to Cancel. The new recording uses captured local paths so there is no collision. |
| `appsettings.json` becomes corrupt or unreadable on Save | `AppConfigStore.Save` writes to `appsettings.json.tmp` then `File.Replace` — partial write cannot leave the original truncated. On read, malformed JSON falls back to defaults and shows a startup balloon. |

## Testing

### Unit tests (new + extended)
```
tests/SPRecorder.Tests/
  AppConfigStoreTests.cs
    - Save then Load returns identical config
    - Atomic write: original survives if mid-write process exits (simulate via partial file)
    - Defaults applied for missing fields
  Mp3MixerTests.cs (extended)
    - MixToStereo: output is stereo, L channel matches system source, R matches mic source
  AudioDeviceEnumeratorTests.cs
    - GetRenderDevices returns ≥1 device on a normal Windows box
    - All returned devices have non-empty Id and FriendlyName
  SessionNamePromptTests.cs
    - Sanitizer replaces invalid chars
    - Empty input treated as Cancel
  HotkeyCaptureControlTests.cs
    - KeyDown sequence Ctrl, Shift, M produces "Ctrl+Shift+M"
    - Esc resets to previous value
```

### Manual smoke test
1. Right-click tray → "Settings…" → dialog opens centered, ~560×440 px.
2. **General**: change hotkey to `Ctrl+Shift+M` → click Save → balloon "Hotkey changed". Old `Ctrl+Alt+R` no longer toggles; new combo does.
3. **Audio**: change microphone to a different device → Save → start recording → confirm `_mic.mp3` came from the new device (compare audio).
4. **Mixed file**: uncheck "Generate mixed file" → Save → record → confirm only 2 files appear, no `_mixed.mp3`. Re-enable, change to Stereo → record → mixed file plays as stereo (L=system, R=mic) in any audio player.
5. **Named session**: enable "Ask for session name after recording" → record → stop → modal appears → type "Test Meeting" → OK → confirm `Documents/SPRecorder/Test Meeting_<timestamp>/Test Meeting_<timestamp>_system.mp3` etc. exist.
6. Repeat (5) but click Cancel on the prompt → confirm files land in root with timestamp-only names.
7. While recording, open Settings → confirm Audio tab dropdowns are disabled with hint text.
8. Restart the app → confirm all changed settings persisted.

## Out of scope

- First-run wizard (Settings is opened on demand only).
- Editing the file-name pattern with a token reference / live preview (just a textbox + hint).
- Migration of `appsettings.json` from older shapes (we only add fields with defaults; old files keep working).
- Importing/exporting settings, profiles, or per-device profiles.
- Auto-detection of Teams calls, rolling buffer, or any other capture-trigger changes.
