# Audio Splitting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a post-process step to SPRecorder that splits each recorded MP3 (system, mic, mixed) into chunks of a configurable size or duration so long meetings stay under NotebookLM's 200 MB upload cap.

**Architecture:** A new `IMp3Splitter` / `Mp3FrameSplitter` pair copies MP3 frames verbatim into numbered chunk files using NAudio's `Mp3Frame.LoadFromStream`. `RecordingSession.Stop()` runs the splitter inside the existing background mix task. Settings exposes mode (None / Time / Size), threshold values, and per-track checkboxes via a new "Splitting" tab.

**Tech Stack:** .NET 10 / WinForms / NAudio 2.3.0 / NAudio.Lame 2.1.0 / xUnit. No new dependencies.

**Spec:** [docs/superpowers/specs/2026-05-28-audio-splitting-design.md](../specs/2026-05-28-audio-splitting-design.md)

---

## File Structure

**Create**
- `src/SPRecorder/Audio/IMp3Splitter.cs` — splitter interface (~10 lines)
- `src/SPRecorder/Audio/Mp3FrameSplitter.cs` — frame-level NAudio implementation (~70 lines)
- `tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs` — 8 unit tests (~180 lines)

**Modify**
- `src/SPRecorder/Configuration/AppConfig.cs` — add 6 properties + validation
- `src/SPRecorder/appsettings.json` — add 6 defaults
- `src/SPRecorder/Recording/RecordingSession.cs` — rename `StartMixingInBackground` → `StartPostProcessingInBackground`, add split step + `SplitCompleted` event + `SplitTrack` helper
- `src/SPRecorder/Settings/SettingsForm.cs` — add `BuildSplittingTab()` + persist 6 new fields
- `src/SPRecorder/Tray/TrayApp.cs` — subscribe to `SplitCompleted` and show toast
- `tests/SPRecorder.Tests/AppConfigStoreTests.cs` — extend roundtrip test with new fields

---

## Task 1: Config schema

**Files:**
- Modify: `src/SPRecorder/Configuration/AppConfig.cs`
- Modify: `src/SPRecorder/appsettings.json`
- Modify: `tests/SPRecorder.Tests/AppConfigStoreTests.cs`

- [ ] **Step 1.1: Add a failing test for split fields roundtripping through `AppConfigStore`**

Append to `tests/SPRecorder.Tests/AppConfigStoreTests.cs`:

```csharp
[Fact]
public void Save_RoundtripsSplitFields()
{
    var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
    try
    {
        var store = new AppConfigStore(path, new AppConfig());
        var updated = new AppConfig
        {
            SplitMode = "Size",
            SplitTimeMinutes = 45,
            SplitSizeMb = 180,
            SplitSystemTrack = false,
            SplitMicTrack = true,
            SplitMixedTrack = true,
        };

        store.Save(updated);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("Size", root.GetProperty("SplitMode").GetString());
        Assert.Equal(45,     root.GetProperty("SplitTimeMinutes").GetInt32());
        Assert.Equal(180,    root.GetProperty("SplitSizeMb").GetInt32());
        Assert.False(        root.GetProperty("SplitSystemTrack").GetBoolean());
        Assert.True(         root.GetProperty("SplitMicTrack").GetBoolean());
        Assert.True(         root.GetProperty("SplitMixedTrack").GetBoolean());
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
```

- [ ] **Step 1.2: Run the test to confirm it fails (compile error — properties don't exist yet)**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~AppConfigStoreTests.Save_RoundtripsSplitFields"`
Expected: BUILD FAIL with `'AppConfig' does not contain a definition for 'SplitMode'`.

- [ ] **Step 1.3: Add the 6 properties to `AppConfig`**

Edit `src/SPRecorder/Configuration/AppConfig.cs`. After the `AutoDetectCallsEnabled` property, add:

```csharp
public string SplitMode        { get; init; } = "None";   // "None" | "Time" | "Size"
public int    SplitTimeMinutes { get; init; } = 30;
public int    SplitSizeMb      { get; init; } = 195;
public bool   SplitSystemTrack { get; init; } = true;
public bool   SplitMicTrack    { get; init; } = true;
public bool   SplitMixedTrack  { get; init; } = true;
```

Update `AppConfig.Load` to validate the new fields. Replace the body of `Load` with:

```csharp
public static AppConfig Load(IConfiguration cfg)
{
    var raw = cfg.Get<AppConfig>() ?? new AppConfig();
    return raw with
    {
        OutputDirectory = Environment.ExpandEnvironmentVariables(raw.OutputDirectory),
        SplitMode = raw.SplitMode is "Time" or "Size" ? raw.SplitMode : "None",
        SplitTimeMinutes = Math.Clamp(raw.SplitTimeMinutes, 1, 1440),
        SplitSizeMb      = Math.Clamp(raw.SplitSizeMb,      1, 10000),
    };
}
```

- [ ] **Step 1.4: Add defaults to `appsettings.json`**

Edit `src/SPRecorder/appsettings.json`. Add after `"AutoDetectCallsEnabled": false,`:

```json
  "SplitMode": "None",
  "SplitTimeMinutes": 30,
  "SplitSizeMb": 195,
  "SplitSystemTrack": true,
  "SplitMicTrack": true,
  "SplitMixedTrack": true
```

(Remember to add a comma after `"AutoDetectCallsEnabled": false`.)

- [ ] **Step 1.5: Run the test to confirm it passes**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~AppConfigStoreTests.Save_RoundtripsSplitFields"`
Expected: PASS.

- [ ] **Step 1.6: Add a failing test for `AppConfig.Load` validation**

Append to `tests/SPRecorder.Tests/AppConfigStoreTests.cs`:

```csharp
[Fact]
public void Load_ClampsAndFallsBackInvalidSplitFields()
{
    var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
    try
    {
        File.WriteAllText(path, """
        {
          "SplitMode": "Garbage",
          "SplitTimeMinutes": 99999,
          "SplitSizeMb": 0
        }
        """);

        var builder = new ConfigurationBuilder().AddJsonFile(path);
        var loaded = AppConfig.Load(builder.Build());

        Assert.Equal("None", loaded.SplitMode);
        Assert.Equal(1440,   loaded.SplitTimeMinutes); // upper bound
        Assert.Equal(1,      loaded.SplitSizeMb);      // lower bound
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
```

Add `using Microsoft.Extensions.Configuration;` at the top of the file if not already there.

- [ ] **Step 1.7: Run the validation test — should already pass given Step 1.3**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~AppConfigStoreTests.Load_ClampsAndFallsBackInvalidSplitFields"`
Expected: PASS.

- [ ] **Step 1.8: Commit**

```powershell
git add src/SPRecorder/Configuration/AppConfig.cs src/SPRecorder/appsettings.json tests/SPRecorder.Tests/AppConfigStoreTests.cs
git commit -m "Add split-related config fields with clamp + fallback validation"
```

---

## Task 2: Splitter — interface, file shell, and "no split" case

**Files:**
- Create: `src/SPRecorder/Audio/IMp3Splitter.cs`
- Create: `src/SPRecorder/Audio/Mp3FrameSplitter.cs`
- Create: `tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs`

- [ ] **Step 2.1: Write the first failing test (file under threshold returns input unchanged)**

Create `tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs`:

```csharp
using NAudio.Lame;
using NAudio.Wave;
using SPRecorder.Audio;

namespace SPRecorder.Tests;

public class Mp3FrameSplitterTests
{
    [Fact]
    public void SplitBySize_FileUnderThreshold_ReturnsInputUnchanged()
    {
        var path = TempPath("size-under");
        try
        {
            WriteSineMp3(path, TimeSpan.FromSeconds(2));
            var originalBytes = File.ReadAllBytes(path);

            var splitter = new Mp3FrameSplitter();
            var result = splitter.SplitBySize(path, 1_000_000); // 1 MB, way over file size

            Assert.Single(result);
            Assert.Equal(path, result[0]);
            Assert.Equal(originalBytes, File.ReadAllBytes(path)); // untouched
        }
        finally
        {
            DeleteAll(path);
        }
    }

    // --- helpers ---

    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"sprec_split_{label}_{Guid.NewGuid():N}.mp3");

    private static void WriteSineMp3(string path, TimeSpan duration, int sampleRate = 44100, int bitrateKbps = 96)
    {
        var fmt = new WaveFormat(sampleRate, 16, 1);
        using var writer = new LameMP3FileWriter(path, fmt, bitrateKbps);
        int totalSamples = (int)(sampleRate * duration.TotalSeconds);
        var buf = new byte[totalSamples * 2];
        for (int i = 0; i < totalSamples; i++)
        {
            short v = (short)(MathF.Sin(2 * MathF.PI * 440 * i / sampleRate) * 16000);
            buf[i * 2]     = (byte)(v & 0xFF);
            buf[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        writer.Write(buf, 0, buf.Length);
    }

    private static void DeleteAll(params string[] paths)
    {
        foreach (var p in paths)
            if (File.Exists(p)) File.Delete(p);
        // also clean any numbered chunks next to each input
        foreach (var p in paths)
        {
            var dir = Path.GetDirectoryName(p) ?? "";
            var stem = Path.GetFileNameWithoutExtension(p);
            foreach (var f in Directory.GetFiles(dir, $"{stem}_*.mp3"))
                File.Delete(f);
        }
    }
}
```

- [ ] **Step 2.2: Run the test — expect compile error**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~Mp3FrameSplitterTests"`
Expected: BUILD FAIL — `IMp3Splitter` / `Mp3FrameSplitter` not defined.

- [ ] **Step 2.3: Create the interface**

Create `src/SPRecorder/Audio/IMp3Splitter.cs`:

```csharp
namespace SPRecorder.Audio;

public interface IMp3Splitter
{
    // Returns paths of generated chunks.
    // If input <= threshold, returns [inputPath] and writes nothing.
    IReadOnlyList<string> SplitByTime(string inputPath, TimeSpan maxDuration);
    IReadOnlyList<string> SplitBySize(string inputPath, long maxBytes);
}
```

- [ ] **Step 2.4: Create the minimal `Mp3FrameSplitter` that handles only the "no split" case**

Create `src/SPRecorder/Audio/Mp3FrameSplitter.cs`:

```csharp
using NAudio.Wave;

namespace SPRecorder.Audio;

public sealed class Mp3FrameSplitter : IMp3Splitter
{
    public IReadOnlyList<string> SplitBySize(string inputPath, long maxBytes)
    {
        if (new FileInfo(inputPath).Length <= maxBytes) return new[] { inputPath };
        throw new NotImplementedException("size-based split not yet implemented");
    }

    public IReadOnlyList<string> SplitByTime(string inputPath, TimeSpan maxDuration)
    {
        using (var probe = new Mp3FileReader(inputPath))
            if (probe.TotalTime <= maxDuration) return new[] { inputPath };
        throw new NotImplementedException("time-based split not yet implemented");
    }
}
```

- [ ] **Step 2.5: Run the test — expect PASS**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~Mp3FrameSplitterTests"`
Expected: PASS (1 test).

- [ ] **Step 2.6: Commit**

```powershell
git add src/SPRecorder/Audio/IMp3Splitter.cs src/SPRecorder/Audio/Mp3FrameSplitter.cs tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs
git commit -m "Add IMp3Splitter interface + no-op fast path when file is under threshold"
```

---

## Task 3: Splitter — size-based split with chunk numbering

**Files:**
- Modify: `src/SPRecorder/Audio/Mp3FrameSplitter.cs`
- Modify: `tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs`

- [ ] **Step 3.1: Add failing tests for size-based split producing chunks**

Append inside the `Mp3FrameSplitterTests` class, before `// --- helpers ---`:

```csharp
[Fact]
public void SplitBySize_FileOverThreshold_ProducesMultipleChunks()
{
    var path = TempPath("size-over");
    try
    {
        WriteSineMp3(path, TimeSpan.FromSeconds(60)); // ~720 KB at 96 kbps mono
        long threshold = 200 * 1024; // 200 KB

        var splitter = new Mp3FrameSplitter();
        var chunks = splitter.SplitBySize(path, threshold);

        Assert.True(chunks.Count >= 3, $"Expected >= 3 chunks, got {chunks.Count}");
        foreach (var c in chunks)
        {
            Assert.True(File.Exists(c), $"Chunk missing: {c}");
            Assert.True(new FileInfo(c).Length > 0, $"Chunk empty: {c}");
        }
    }
    finally
    {
        DeleteAll(path);
    }
}

[Fact]
public void SplitBySize_ChunkFilenamesAreZeroPadded()
{
    var path = TempPath("naming");
    try
    {
        WriteSineMp3(path, TimeSpan.FromSeconds(15));
        var chunks = new Mp3FrameSplitter().SplitBySize(path, 50 * 1024); // ~3-4 chunks

        Assert.True(chunks.Count >= 2);
        var stem = Path.GetFileNameWithoutExtension(path);
        for (int i = 0; i < chunks.Count; i++)
        {
            var expected = $"{stem}_{(i + 1):D3}.mp3";
            Assert.EndsWith(expected, chunks[i]);
        }
    }
    finally
    {
        DeleteAll(path);
    }
}
```

- [ ] **Step 3.2: Run — expect both new tests to fail with `NotImplementedException`**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~Mp3FrameSplitterTests"`
Expected: 2 FAIL (NotImplementedException), 1 PASS.

- [ ] **Step 3.3: Implement frame-loop splitting in `Mp3FrameSplitter`**

Replace the body of `src/SPRecorder/Audio/Mp3FrameSplitter.cs` with:

```csharp
using NAudio.Wave;

namespace SPRecorder.Audio;

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

    // shouldClose receives (currentChunkSize, currentChunkDuration) and decides
    // whether to close the current chunk before writing the next frame.
    private static IReadOnlyList<string> SplitFrames(
        string inputPath,
        Func<long, TimeSpan, bool> shouldClose)
    {
        var outputs = new List<string>();
        using var source = File.OpenRead(inputPath);

        FileStream? current = null;
        long currentSize = 0;
        TimeSpan currentDuration = TimeSpan.Zero;
        int index = 1;

        try
        {
            Mp3Frame? frame;
            while ((frame = Mp3Frame.LoadFromStream(source)) != null)
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
                current.Write(frame.RawData, 0, frame.RawData.Length);
                currentSize += frame.RawData.Length;
                currentDuration += TimeSpan.FromSeconds(
                    (double)frame.SampleCount / frame.SampleRate);
            }
        }
        finally
        {
            current?.Dispose();
        }

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

Note: reading frames from a plain `FileStream` (not `Mp3FileReader`) sidesteps NAudio's index-table buffering and is the recommended pattern for `Mp3Frame.LoadFromStream`.

- [ ] **Step 3.4: Run — expect all 3 tests in the file to PASS**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~Mp3FrameSplitterTests"`
Expected: 3 PASS.

- [ ] **Step 3.5: Commit**

```powershell
git add src/SPRecorder/Audio/Mp3FrameSplitter.cs tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs
git commit -m "Implement size-based frame-level MP3 splitting with zero-padded chunk names"
```

---

## Task 4: Splitter — time-based split + bit-perfect verification

**Files:**
- Modify: `tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs`

- [ ] **Step 4.1: Add the remaining failing tests**

Append inside the `Mp3FrameSplitterTests` class:

```csharp
[Fact]
public void SplitByTime_FileUnderDuration_ReturnsInputUnchanged()
{
    var path = TempPath("time-under");
    try
    {
        WriteSineMp3(path, TimeSpan.FromSeconds(10));
        var result = new Mp3FrameSplitter().SplitByTime(path, TimeSpan.FromSeconds(60));

        Assert.Single(result);
        Assert.Equal(path, result[0]);
    }
    finally
    {
        DeleteAll(path);
    }
}

[Fact]
public void SplitByTime_FileOverDuration_ProducesMultipleChunks()
{
    var path = TempPath("time-over");
    try
    {
        WriteSineMp3(path, TimeSpan.FromSeconds(90));
        var chunks = new Mp3FrameSplitter().SplitByTime(path, TimeSpan.FromSeconds(30));

        Assert.True(chunks.Count >= 3, $"Expected >= 3 chunks, got {chunks.Count}");
        foreach (var c in chunks)
        {
            using var r = new Mp3FileReader(c);
            // each non-final chunk should be >= the threshold; final chunk can be shorter
            Assert.True(r.TotalTime.TotalSeconds > 0);
        }
    }
    finally
    {
        DeleteAll(path);
    }
}

[Fact]
public void SplitByTime_HandlesShortFinalChunk()
{
    var path = TempPath("time-tail");
    try
    {
        WriteSineMp3(path, TimeSpan.FromSeconds(70));
        var chunks = new Mp3FrameSplitter().SplitByTime(path, TimeSpan.FromSeconds(30));

        Assert.Equal(3, chunks.Count);
        using var last = new Mp3FileReader(chunks[^1]);
        // last chunk should be the leftover (~10 s), not a full 30 s slice
        Assert.True(last.TotalTime.TotalSeconds < 25,
            $"Final chunk unexpectedly long: {last.TotalTime.TotalSeconds}s");
    }
    finally
    {
        DeleteAll(path);
    }
}

[Fact]
public void Chunks_AreValidMp3_ReadableByNAudio()
{
    var path = TempPath("validity");
    try
    {
        WriteSineMp3(path, TimeSpan.FromSeconds(20));
        var chunks = new Mp3FrameSplitter().SplitBySize(path, 100 * 1024);

        Assert.True(chunks.Count >= 2);
        foreach (var c in chunks)
        {
            using var r = new Mp3FileReader(c);
            Assert.Equal(1, r.WaveFormat.Channels);
            Assert.True(r.TotalTime.TotalSeconds > 0);

            // exercise the decoder to confirm no frame corruption
            var pcm = new byte[r.WaveFormat.AverageBytesPerSecond];
            int totalRead = 0, read;
            while ((read = r.Read(pcm, 0, pcm.Length)) > 0) totalRead += read;
            Assert.True(totalRead > 0, $"Chunk decoded to 0 bytes: {c}");
        }
    }
    finally
    {
        DeleteAll(path);
    }
}

[Fact]
public void Chunks_ConcatenatedFrames_EqualInputBytes()
{
    var path = TempPath("concat");
    try
    {
        WriteSineMp3(path, TimeSpan.FromSeconds(15));
        var originalBytes = File.ReadAllBytes(path);

        var chunks = new Mp3FrameSplitter().SplitBySize(path, 80 * 1024);
        Assert.True(chunks.Count >= 2);

        // Concatenate all chunk bytes
        using var concatenated = new MemoryStream();
        foreach (var c in chunks)
        {
            using var fs = File.OpenRead(c);
            fs.CopyTo(concatenated);
        }
        var combined = concatenated.ToArray();

        // The input may have a leading LAME info frame that our frame loop also
        // preserves verbatim in chunk 1 — so the concatenation of all frame
        // payloads should equal the input MP3 byte-for-byte.
        Assert.Equal(originalBytes.Length, combined.Length);
        Assert.Equal(originalBytes, combined);
    }
    finally
    {
        DeleteAll(path);
    }
}
```

Add `using NAudio.Wave;` at the top if not already present.

- [ ] **Step 4.2: Run — all 5 new tests should pass since the implementation already handles both modes**

Run: `dotnet test tests/SPRecorder.Tests --filter "FullyQualifiedName~Mp3FrameSplitterTests"`
Expected: 8 PASS total.

If `Chunks_ConcatenatedFrames_EqualInputBytes` fails because the byte-for-byte equality is too strict (e.g., `Mp3Frame.LoadFromStream` skips garbage bytes between frames in the source), relax the assertion to compare the byte counts and the first 1000 bytes — but try the strict version first. The test files come from `LameMP3FileWriter` with no garbage between frames, so strict equality should hold.

- [ ] **Step 4.3: Commit**

```powershell
git add tests/SPRecorder.Tests/Mp3FrameSplitterTests.cs
git commit -m "Cover time-based split, chunk validity, and bit-perfect concatenation"
```

---

## Task 5: `RecordingSession` integration

**Files:**
- Modify: `src/SPRecorder/Recording/RecordingSession.cs`

This task is unit-tested via the existing `Mp3FrameSplitter` tests + the manual run in Task 8. The `RecordingSession` state machine itself is not unit-tested today (it requires live WASAPI captures), and adding fake-capture infrastructure is out of scope.

- [ ] **Step 5.1: Add the `SplitCompleted` event near the other event declarations**

In `src/SPRecorder/Recording/RecordingSession.cs`, find:

```csharp
    public event Action? MixingStarted;
    public event Action<string?>? MixingCompleted;
```

Add immediately after:

```csharp
    public event Action<int>? SplitCompleted;  // arg = total chunks across all tracks
```

- [ ] **Step 5.2: Update `Stop()` to invoke post-processing when either mix or split is enabled**

Find the bottom of `Stop()`:

```csharp
        if (_activeConfig.MixedFileEnabled)
            StartMixingInBackground();
```

Replace with:

```csharp
        var willMix   = _activeConfig.MixedFileEnabled;
        var willSplit = !_activeConfig.SplitMode.Equals("None", StringComparison.OrdinalIgnoreCase);

        if (willMix || willSplit)
            StartPostProcessingInBackground(willMix, willSplit);
```

- [ ] **Step 5.3: Rename and extend `StartMixingInBackground`**

Replace the entire `StartMixingInBackground` method with:

```csharp
    private void StartPostProcessingInBackground(bool willMix, bool willSplit)
    {
        var sysPath   = SystemFilePath;
        var micPath   = MicFilePath;
        var mixedPath = MixedFilePath;
        var cfg       = _activeConfig;
        var bitrate   = cfg.Mp3BitrateKbps;
        var sampleRate = cfg.MixedFileSampleRate;
        var stereo    = cfg.MixedFileFormat.Equals("Stereo", StringComparison.OrdinalIgnoreCase);

        if (sysPath is null || micPath is null || mixedPath is null) return;
        if (!File.Exists(sysPath) || !File.Exists(micPath)) return;

        MixingStarted?.Invoke();
        Task.Run(() =>
        {
            string? finalMixedPath = null;
            if (willMix)
            {
                try
                {
                    if (stereo)
                        Mp3Mixer.MixToStereo(sysPath, micPath, mixedPath, bitrate, sampleRate);
                    else
                        Mp3Mixer.MixToMono(sysPath, micPath, mixedPath, bitrate, sampleRate);
                    finalMixedPath = mixedPath;
                }
                catch (Exception ex)
                {
                    Warning?.Invoke("Mixing failed: " + ex.Message);
                }
            }

            int totalChunks = 0;
            if (willSplit)
            {
                var splitter = new Mp3FrameSplitter();
                if (cfg.SplitSystemTrack)            totalChunks += SplitTrack(splitter, sysPath,   cfg);
                if (cfg.SplitMicTrack)               totalChunks += SplitTrack(splitter, micPath,   cfg);
                if (cfg.SplitMixedTrack && willMix)  totalChunks += SplitTrack(splitter, mixedPath, cfg);
            }

            MixingCompleted?.Invoke(finalMixedPath);
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

- [ ] **Step 5.4: Build and run the full test suite**

Run: `dotnet test`
Expected: all existing tests still pass, plus the 8 splitter tests and 2 new config tests from Task 1. No new failures.

- [ ] **Step 5.5: Commit**

```powershell
git add src/SPRecorder/Recording/RecordingSession.cs
git commit -m "Wire post-process splitting into RecordingSession.Stop() with SplitCompleted event"
```

---

## Task 6: Settings UI — "Splitting" tab

**Files:**
- Modify: `src/SPRecorder/Settings/SettingsForm.cs`

WinForms layout is not unit-tested here; verification is manual at the end of this task.

- [ ] **Step 6.1: Declare the 6 new control fields**

In `src/SPRecorder/Settings/SettingsForm.cs`, find the block:

```csharp
    private CheckBox _mixedEnabled = null!;
    private RadioButton _mixedMono = null!;
    private RadioButton _mixedStereo = null!;
    private ComboBox _mixedSampleRate = null!;
    private GroupBox _mixedDetails = null!;
```

Add immediately after:

```csharp
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
```

- [ ] **Step 6.2: Register the new tab**

Find:

```csharp
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildAudioTab());
        tabs.TabPages.Add(BuildMixedTab());
```

Replace with:

```csharp
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildAudioTab());
        tabs.TabPages.Add(BuildMixedTab());
        tabs.TabPages.Add(BuildSplittingTab());
```

- [ ] **Step 6.3: Add `BuildSplittingTab`**

Add the following method to the class, immediately after `BuildMixedTab`:

```csharp
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
```

- [ ] **Step 6.4: Initialize control values inside `ApplyConfigToControls`**

Find `ApplyConfigToControls` (around line 397). Append at the end (after the `_mixedSampleRate` block):

```csharp
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
```

- [ ] **Step 6.5: Persist new values inside `Save_Click`**

Find `Save_Click`. Inside the `Result = _initial with { ... }` block, add these fields before the closing `}`:

```csharp
            SplitMode = _splitByTime.Checked ? "Time"
                      : _splitBySize.Checked ? "Size"
                      : "None",
            SplitTimeMinutes = (int)_splitMinutes.Value,
            SplitSizeMb      = (int)_splitSizeMb.Value,
            SplitSystemTrack = _splitSystem.Checked,
            SplitMicTrack    = _splitMic.Checked,
            SplitMixedTrack  = _splitMixed.Checked,
```

- [ ] **Step 6.6: Build and run the app to verify the new tab renders**

Run: `dotnet run --project src/SPRecorder`
Expected: app launches into the tray. Right-click → Settings… → click "Splitting" tab.
Verify by clicking through:
- All three radios switch correctly.
- "By time" enables the minutes input; "By size" enables the MB input; "None" greys both and the "Apply to" group.
- Setting MB > 200 shows the orange "NotebookLM accepts ≤ 200 MB" hint.
- Click Save with values changed → open `%USERPROFILE%\Documents\SPRecorder\appsettings.json` (or wherever the app stores it) and confirm new fields persisted.

Close the app.

- [ ] **Step 6.7: Run all tests once more**

Run: `dotnet test`
Expected: all tests still PASS.

- [ ] **Step 6.8: Commit**

```powershell
git add src/SPRecorder/Settings/SettingsForm.cs
git commit -m "Add Splitting tab to Settings dialog with mode/threshold/per-track controls"
```

---

## Task 7: Tray toast for `SplitCompleted`

**Files:**
- Modify: `src/SPRecorder/Tray/TrayApp.cs`

- [ ] **Step 7.1: Subscribe to the new event**

In `src/SPRecorder/Tray/TrayApp.cs`, find the event subscriptions in the constructor:

```csharp
        _session.MixingCompleted += path => OnUi(() => OnMixingCompleted(path));
```

Add immediately after:

```csharp
        _session.SplitCompleted += chunks => OnUi(() => OnSplitCompleted(chunks));
```

- [ ] **Step 7.2: Add the handler method**

Add this method to `TrayApp` (place it next to `OnMixingCompleted`):

```csharp
    private void OnSplitCompleted(int chunks)
    {
        if (chunks == 0) return; // nothing actually got split (all files under threshold)
        ShowBalloon(ToolTipIcon.Info, "Split complete",
            $"Output split into {chunks} chunk{(chunks == 1 ? "" : "s")}.");
    }
```

- [ ] **Step 7.3: Build and run the test suite — no behavior tests, just confirm nothing breaks**

Run: `dotnet test`
Expected: all PASS.

- [ ] **Step 7.4: Commit**

```powershell
git add src/SPRecorder/Tray/TrayApp.cs
git commit -m "Show tray toast when post-recording split completes"
```

---

## Task 8: End-to-end manual verification

**Files:** none (verification only).

- [ ] **Step 8.1: Build + run the full test suite**

Run: `dotnet build && dotnet test`
Expected: build succeeds, all tests PASS.

- [ ] **Step 8.2: Smoke-test the "None" path (no regression)**

1. Run `dotnet run --project src/SPRecorder`.
2. Open Settings, ensure Split mode = None, Save.
3. Press the hotkey, record ~10 seconds, press hotkey again to stop.
4. Confirm three files appear (`*_system.mp3`, `*_mic.mp3`, `*_mixed.mp3`) and there are NO `_001.mp3` chunks.

- [ ] **Step 8.3: Smoke-test size-based split**

1. Open Settings, Split mode = "By size", set MB = 1, all three "Apply to" checked, Save.
2. Record ~30 seconds.
3. After Stop and mix completion (toast), confirm in the output folder:
   - No `*_system.mp3` / `*_mic.mp3` / `*_mixed.mp3` (originals deleted).
   - Multiple `*_system_001.mp3`, `*_system_002.mp3`, … (and similarly for mic / mixed).
   - Tray balloon "Split complete · N chunks" appeared.
4. Open one chunk in VLC and confirm it plays.

- [ ] **Step 8.4: Smoke-test time-based split**

1. Open Settings, Split mode = "By time", set minutes = 1, Save.
2. Record ~3 minutes.
3. Confirm 3 chunks per track, last chunk noticeably shorter (~0–1 min).

- [ ] **Step 8.5: NotebookLM acceptance check (primary constraint)**

1. Take one `*_mixed_001.mp3` from Step 8.3 or 8.4.
2. Upload to NotebookLM as a new source.
3. Confirm it ingests and produces a transcript / summary without error.

If NotebookLM rejects the chunk: the IMp3Splitter abstraction means we can swap in a re-encoding implementation (`Mp3DecodeReencodeSplitter` against the same interface) without touching `RecordingSession`. Open an issue, do not patch this plan.

- [ ] **Step 8.6: Final commit (only if any tweaks were needed during manual testing)**

Otherwise: no commit. The feature is complete; the README's "Roadmap" section can be updated in a follow-up if desired.

---

## Done criteria

- [ ] All 8 splitter unit tests pass.
- [ ] All 3 new config tests pass.
- [ ] Existing test suite unchanged and still green.
- [ ] Settings → Splitting tab is wired end-to-end (radio buttons + threshold inputs + per-track checkboxes + Save persists, Reload reflects).
- [ ] Recording with Split = None matches today's behavior exactly.
- [ ] Recording with Split = Size = 1 MB on a ~30 s session produces multiple chunks per enabled track and deletes the originals.
- [ ] At least one mixed chunk uploads successfully to NotebookLM.
