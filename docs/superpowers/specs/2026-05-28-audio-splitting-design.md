# Audio splitting — design spec

**Date:** 2026-05-28
**Status:** Approved for implementation planning

## Goal

Let SPRecorder split each recorded MP3 into chunks of a configurable size or duration so long meetings stay within NotebookLM's 200 MB upload limit. The feature must work on machines without admin rights (no external installer, no extra runtime).

## Constraints

- **Self-contained**: must ship in the existing single-file `.exe`. No new binary dependencies (rules out shelling out to `ffmpeg`).
- **NotebookLM compatibility**: output chunks must be valid MP3 files accepted by NotebookLM ingestion.
- **No regression**: when split is disabled, recording behavior must match today's flow exactly.

## Decisions

| Decision | Choice | Reason |
|---|---|---|
| Split timing | Post-process after `Stop()` | Avoids hot-rollover gaps, sync drift between system/mic, and pipeline churn |
| Split unit | Both time and size — user picks in Settings | Time fits "navigation chunks", size fits NotebookLM cap |
| Tracks split | All 3 (system, mic, mixed), each toggleable; default all on | Default keeps tracks aligned; power user can opt out per track |
| Original after split | Delete (chunks only) | Matches NotebookLM upload use case, avoids redundancy |
| Filename suffix | `_001`, `_002`, `_003` (zero-padded 3 digits) | Sorts correctly in Explorer past 9 chunks |
| Implementation | Frame-level copy via `NAudio.Wave.Mp3Frame` | Bit-perfect, fast (< 1 s for 200 MB), uses existing NAudio dependency |

## Config schema

Added to `AppConfig` and `appsettings.json`:

```csharp
public string SplitMode        { get; init; } = "None";   // "None" | "Time" | "Size"
public int    SplitTimeMinutes { get; init; } = 30;
public int    SplitSizeMb      { get; init; } = 195;      // safety margin under NotebookLM 200 MB
public bool   SplitSystemTrack { get; init; } = true;
public bool   SplitMicTrack    { get; init; } = true;
public bool   SplitMixedTrack  { get; init; } = true;
```

Validation in `AppConfig.Load`:

```csharp
SplitMode        = raw.SplitMode is "Time" or "Size" ? raw.SplitMode : "None",
SplitTimeMinutes = Math.Clamp(raw.SplitTimeMinutes, 1, 1440),
SplitSizeMb      = Math.Clamp(raw.SplitSizeMb,      1, 10000),
```

## Settings UI

New tab **"Splitting"** in the existing Settings dialog (alongside General / Audio / Mixed file).

```
┌─ Split mode ──────────────────────────┐
│  ○ None                                │
│  ○ By time     [  30 ] minutes         │
│  ● By size     [ 195 ] MB              │
└────────────────────────────────────────┘

Apply to:
  ☑ System track    ☑ Microphone track    ☑ Mixed track
```

UI behaviour:
- `NumericUpDown` for both inputs; bounds match `Math.Clamp` ranges above.
- When `Split mode = None`: both numeric inputs and all three checkboxes are greyed out.
- When `Split mode = Time`: minutes input enabled, MB input greyed.
- When `Split mode = Size`: MB input enabled, minutes input greyed.
- If `SplitSizeMb > 200`: hint label under the MB field — *"NotebookLM accepts ≤ 200 MB"* (informational, not blocking).

Rationale for a dedicated tab: splitting touches all three tracks, not just the mixed file, so nesting it under "Mixed file" would misrepresent its scope.

## Splitter component

Two new files under `src/SPRecorder/Audio/`.

### `IMp3Splitter.cs`

```csharp
public interface IMp3Splitter
{
    // Returns paths of generated chunks.
    // If input <= threshold, returns [inputPath] and writes nothing.
    IReadOnlyList<string> SplitByTime(string inputPath, TimeSpan maxDuration);
    IReadOnlyList<string> SplitBySize(string inputPath, long maxBytes);
}
```

Separate methods (not an enum) so callers don't carry a mode discriminator.

### `Mp3FrameSplitter.cs`

Frame-level copy using `Mp3Frame.LoadFromStream`. Each MP3 frame's `RawData` (header + side info + data) is written verbatim to the current chunk's `FileStream`; when the running size/duration crosses the threshold, the current file closes and a new one opens.

```csharp
public sealed class Mp3FrameSplitter : IMp3Splitter
{
    public IReadOnlyList<string> SplitBySize(string inputPath, long maxBytes)
    {
        if (new FileInfo(inputPath).Length <= maxBytes) return new[] { inputPath };
        return SplitFrames(inputPath, (size, _) => size > maxBytes);
    }

    public IReadOnlyList<string> SplitByTime(string inputPath, TimeSpan maxDuration)
    {
        using (var probe = new Mp3FileReader(inputPath))
            if (probe.TotalTime <= maxDuration) return new[] { inputPath };
        return SplitFrames(inputPath, (_, dur) => dur > maxDuration);
    }

    private static IReadOnlyList<string> SplitFrames(
        string inputPath,
        Func<long, TimeSpan, bool> shouldClose)
    {
        var outputs = new List<string>();
        using var reader = new Mp3FileReader(inputPath);

        FileStream? current = null;
        long currentSize = 0;
        TimeSpan currentDuration = TimeSpan.Zero;
        int index = 1;

        Mp3Frame? frame;
        while ((frame = Mp3Frame.LoadFromStream(reader)) != null)
        {
            if (current == null || shouldClose(currentSize, currentDuration))
            {
                current?.Dispose();
                var path = ChunkPath(inputPath, index++);
                outputs.Add(path);
                current = File.Create(path);
                currentSize = 0;
                currentDuration = TimeSpan.Zero;
            }
            current!.Write(frame.RawData, 0, frame.RawData.Length);
            currentSize += frame.RawData.Length;
            currentDuration += TimeSpan.FromSeconds(
                (double)frame.SampleCount / frame.SampleRate);
        }
        current?.Dispose();
        return outputs;
    }

    private static string ChunkPath(string inputPath, int index)
    {
        var dir  = Path.GetDirectoryName(inputPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var ext  = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{stem}_{index:D3}{ext}");
    }
}
```

**Known quirk:** the first 1–2 frames of chunks 2+ may show mild MP3 bit-reservoir artifact (~50 ms). Negligible for voice/podcast content; not addressed.

**No Xing/Info header** is written. Since the source files are CBR, players estimate duration from byte rate × file size correctly.

## Integration into `RecordingSession`

`StartMixingInBackground` is renamed to `StartPostProcessingInBackground` and gains a split step that runs after mix in the same background task.

```csharp
public void Stop()
{
    // ... (unchanged: stop captures, dispose writers, set Idle, optional rename) ...

    var willMix   = _activeConfig.MixedFileEnabled;
    var willSplit = !_activeConfig.SplitMode.Equals("None", StringComparison.OrdinalIgnoreCase);

    if (willMix || willSplit)
        StartPostProcessingInBackground(willMix, willSplit);
}

private void StartPostProcessingInBackground(bool willMix, bool willSplit)
{
    var sysPath   = SystemFilePath;
    var micPath   = MicFilePath;
    var mixedPath = MixedFilePath;
    var cfg       = _activeConfig;

    if (sysPath is null || micPath is null || mixedPath is null) return;

    MixingStarted?.Invoke();
    Task.Run(() =>
    {
        if (willMix)
        {
            try
            {
                if (cfg.MixedFileFormat.Equals("Stereo", StringComparison.OrdinalIgnoreCase))
                    Mp3Mixer.MixToStereo(sysPath, micPath, mixedPath, cfg.Mp3BitrateKbps, cfg.MixedFileSampleRate);
                else
                    Mp3Mixer.MixToMono(sysPath, micPath, mixedPath, cfg.Mp3BitrateKbps, cfg.MixedFileSampleRate);
            }
            catch (Exception ex) { Warning?.Invoke("Mixing failed: " + ex.Message); }
        }

        int totalChunks = 0;
        if (willSplit)
        {
            var splitter = new Mp3FrameSplitter();
            if (cfg.SplitSystemTrack)              totalChunks += SplitTrack(splitter, sysPath,   cfg);
            if (cfg.SplitMicTrack)                 totalChunks += SplitTrack(splitter, micPath,   cfg);
            if (cfg.SplitMixedTrack && willMix)    totalChunks += SplitTrack(splitter, mixedPath, cfg);
        }

        MixingCompleted?.Invoke(willMix ? mixedPath : null);
        if (willSplit) SplitCompleted?.Invoke(totalChunks);
    });
}

private int SplitTrack(IMp3Splitter splitter, string path, AppConfig cfg)
{
    if (!File.Exists(path)) return 0;
    try
    {
        var chunks = cfg.SplitMode.Equals("Time", StringComparison.OrdinalIgnoreCase)
            ? splitter.SplitByTime(path, TimeSpan.FromMinutes(cfg.SplitTimeMinutes))
            : splitter.SplitBySize(path, (long)cfg.SplitSizeMb * 1024L * 1024L);

        if (chunks.Count > 1)
        {
            File.Delete(path);
            return chunks.Count;
        }
        return 0;
    }
    catch (Exception ex)
    {
        Warning?.Invoke($"Split failed for {Path.GetFileName(path)}: {ex.Message}");
        return 0;
    }
}
```

New event (additive — no break to existing `MixingStarted` / `MixingCompleted` subscribers):

```csharp
public event Action<int>? SplitCompleted;  // arg = total chunks across all tracks
```

The tray can subscribe to `SplitCompleted` to show a toast like *"Split into N chunks"*.

## Edge cases

| Case | Behaviour |
|---|---|
| File at or under threshold | `chunks.Count == 1`, original kept, not counted as a chunk |
| File missing (e.g., mix failed) | `SplitTrack` returns 0, no exception |
| MP3 corrupted mid-file | `Mp3Frame.LoadFromStream` returns null at the bad frame; final chunk may be short; `Warning` fires |
| Disk full mid-split | `IOException` caught in `SplitTrack`, original preserved, `Warning` fires |
| All three "Apply to" checkboxes off, `SplitMode != None` | `totalChunks = 0`, no work done, no error |
| `SplitMixedTrack = true` but `MixedFileEnabled = false` | Skipped (`&& willMix` guard) |
| User starts a new recording while post-processing runs | New filenames use a new timestamp — no collision |
| Config edited mid-recording | `_activeConfig` snapshotted at `Start()`; split uses the snapshot |

## Testing

Unit tests in `tests/SPRecorder.Tests/Audio/Mp3FrameSplitterTests.cs`. MP3 fixtures are generated at test time with `LameMP3FileWriter`; no binary checked into the repo.

| Test | Setup | Expectation |
|---|---|---|
| `SplitBySize_FileUnderThreshold_NoSplit` | 10 s MP3, threshold 1 MB | Returns `[inputPath]`, file unchanged |
| `SplitBySize_FileOverThreshold_ProducesChunks` | 60 s MP3, threshold 200 KB | Returns ~4 paths, each chunk ≈ 200 KB |
| `SplitByTime_FileUnderDuration_NoSplit` | 30 s MP3, max 60 s | Returns `[inputPath]` |
| `SplitByTime_FileOverDuration_ProducesChunks` | 90 s MP3, max 30 s | Returns 3 paths, each ≈ 30 s |
| `Chunk_FilenamesAreZeroPadded` | Any split that produces ≥ 2 chunks | Suffixes are `_001`, `_002`, … |
| `Chunk_IsValidMp3` | Any chunk | `new Mp3FileReader(chunk)` opens, frames read |
| `Chunks_ConcatenatedEqualsInput` | Any split | Concatenating chunk bytes in order matches input bytes exactly |
| `SplitByTime_HandlesShortFinalChunk` | 70 s MP3, max 30 s | Chunks 1–2 ≈ 30 s, chunk 3 ≈ 10 s |

`RecordingSession` integration tests are out of scope — they would require live WASAPI capture and are too brittle.

### Manual verification (required before claiming done)

- [ ] `dotnet build` and `dotnet test` both green
- [ ] Record a ~5 minute meeting with `SplitMode=Size`, `SplitSizeMb=1` → multiple chunks produced, original deleted
- [ ] Open chunks in VLC → play through, no objectionable gap at chunk boundaries
- [ ] **Upload one chunk to NotebookLM → ingested and transcribed successfully** (primary constraint)
- [ ] Set `SplitMode=None` → behaviour identical to current release (no regression)

## Out of scope

- Hot rollover during recording (rejected in favour of post-process — see decisions table)
- Re-encoding splitter (kept available as a future swap behind `IMp3Splitter` if frame-copy ever causes NotebookLM rejection)
- ID3 tagging of chunks
- Chunk-level metadata file (manifest of which chunks belong to which session)
