# SPRecorder

Dual-track meeting recorder for Windows with optional screen capture. Captures **system audio** (what other meeting participants say through your speakers/headphones) and **microphone input** (your voice) into **two separate MP3 files**, controlled by a global hotkey. Optionally records your screen to an MP4 alongside the audio.

Built for the case where Microsoft Teams meetings aren't being recorded by the host. Output files are sized for upload to Google NotebookLM (≤ 200 MB per source).

After each recording, SPRecorder also produces a third **mixed mono MP3** that combines both tracks into a single file — this is what you upload to NotebookLM (or any AI summarizer) so the AI hears one coherent conversation instead of two disconnected sources. The two separate tracks are still saved alongside it for archive/editing.

> **Compliance note**: Recording meetings without participants' awareness may violate company policy or data-protection law (GDPR, Norwegian privacy rules). Confirm with your employer before routine use.

## Requirements

- Windows 10/11 x64
- .NET 10 SDK (build only). The published output is a self-contained folder.
- **Visual C++ Redistributable** and **Media Foundation** on the target PC (both present on normal Windows 10/11; on N/KN editions install the Media Feature Pack).

## Build

```bash
dotnet build
```

## Run tests

```bash
dotnet test
```

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

Auto-start: place a shortcut to `SPRecorder.exe` in `shell:startup`.

## Configuration (`appsettings.json`)

```json
{
  "OutputDirectory": "%USERPROFILE%\\Documents\\SPRecorder",
  "FileNamePattern": "{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3",
  "Hotkey": "Ctrl+Alt+R",
  "Mp3BitrateKbps": 96
}
```

`{track}` is replaced with `system` or `mic`. Modifier keys: `Ctrl`, `Alt`, `Shift`, `Win`. Restart the app after editing.

## Usage

1. Run `SPRecorder.exe` → gray circle in tray.
2. Press hotkey (default `Ctrl+Alt+R`) → icon turns red, recording starts.
3. Press hotkey again → recording stops. Two MP3 files appear immediately (`_system.mp3`, `_mic.mp3`); the third (`_mixed.mp3`) appears a few seconds later once mixing finishes (a toast confirms when ready).
4. Upload `_mixed.mp3` to NotebookLM/your AI summarizer of choice.
5. Right-click tray for menu: Start/Stop, Open folder, About, Quit.

### Screen recording (optional)

Enable **Record screen too** in Settings → Screen, or via the tray menu. When on,
recording also produces `<name>_screen.mp4` of the chosen monitor (default primary)
with the meeting audio embedded, plus a mouse-click highlight and an on-screen
display of the keys you press.

> **Privacy**: the keystroke display shows **every key you type**, so anything you
> type — including passwords — appears in the video. Turn off "Show keystrokes on
> screen" (Settings → Screen) before typing secrets, or avoid typing them while
> recording.

## Project layout

```
src/SPRecorder/
  Program.cs                    Single-instance lock + tray bootstrap
  appsettings.json              Config (path, hotkey, bitrate)
  Configuration/AppConfig.cs    POCO + binder + env-var expansion
  Audio/SystemAudioCapture.cs   WASAPI loopback wrapper
  Audio/MicrophoneCapture.cs    WASAPI capture wrapper
  Audio/Mp3StreamWriter.cs      PCM → 16-bit mono → LameMP3FileWriter
  Audio/Mp3Mixer.cs             Reads 2 MP3s, resamples to common rate, mono-mixes, encodes
  Recording/RecordingSession.cs Orchestrates 2 captures + 2 writers + post-stop mixing
  Recording/FileNameBuilder.cs  Token substitution + filename sanitization
  Hotkey/GlobalHotkey.cs        Win32 RegisterHotKey via P/Invoke
  Hotkey/HotkeyParser.cs        "Ctrl+Alt+R" → modifiers + virtual-key
  Tray/TrayApp.cs               ApplicationContext + NotifyIcon + menu
  Tray/IconFactory.cs           Generates idle/recording icons at runtime
tests/SPRecorder.Tests/         xUnit tests for the pure pieces
docs/ui-mockup.html             Visual mockup of tray UI states
docs/settings-mockup.html       Visual mockup of upcoming Settings dialog
docs/superpowers/specs/         Design specs for planned work
```

## Roadmap

Designed but not yet implemented:

- **In-app Settings dialog** — tabbed Windows Forms window opened from the tray menu (General / Audio / Mixed file). Apply changes live without restart. Adds device pickers (microphone, system loopback) and stereo-mix option. See [docs/superpowers/specs/2026-04-28-settings-ui-design.md](docs/superpowers/specs/2026-04-28-settings-ui-design.md) for the full design.
- **Named sessions** (opt-in) — after each recording stops, prompt for a topic name (e.g. *"Q2 Planning"*) and save the files into `OutputDirectory/<name>_<timestamp>/` with matching filename prefixes. Makes recordings easy to identify when uploading to NotebookLM. Specced alongside the Settings dialog.

