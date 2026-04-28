# SPRecorder

Dual-track meeting recorder for Windows. Captures **system audio** (what other meeting participants say through your speakers/headphones) and **microphone input** (your voice) into **two separate MP3 files**, controlled by a global hotkey.

Built for the case where Microsoft Teams meetings aren't being recorded by the host. Output files are sized for upload to Google NotebookLM (≤ 200 MB per source).

After each recording, SPRecorder also produces a third **mixed mono MP3** that combines both tracks into a single file — this is what you upload to NotebookLM (or any AI summarizer) so the AI hears one coherent conversation instead of two disconnected sources. The two separate tracks are still saved alongside it for archive/editing.

> **Compliance note**: Recording meetings without participants' awareness may violate company policy or data-protection law (GDPR, Norwegian privacy rules). Confirm with your employer before routine use.

## Requirements

- Windows 10/11
- .NET 10 SDK (build only). The published single-file `.exe` is self-contained.

## Build

```bash
dotnet build
```

## Run tests

```bash
dotnet test
```

## Publish single-file `.exe`

```bash
dotnet publish src/SPRecorder/SPRecorder.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src/SPRecorder/bin/Release/net10.0-windows/win-x64/publish/SPRecorder.exe` (~70 MB).

Copy `SPRecorder.exe` and `appsettings.json` together to a folder of your choice (e.g. `C:\Tools\SPRecorder\`). Auto-start: place a shortcut in `shell:startup`.

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
```
