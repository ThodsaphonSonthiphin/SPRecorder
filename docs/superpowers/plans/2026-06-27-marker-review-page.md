# Marker Review Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a Recording Session has ≥ 1 marker, generate a self-contained HTML "Marker review page" next to the tracks; clicking a marker seeks an embedded player to that marker's elapsed offset in the Screen recording or the Mixed file.

**Architecture:** A new pure `MarkerReviewPage` renders the HTML (browser owns playback — no media code in C#). `MarkerLog` retains its entries; `RecordingSession` writes the page after post-processing, keeps the whole Mixed file when markers exist, and raises `ReviewPageReady`. `TrayApp` surfaces a balloon + "Open marker review" item (optional auto-open via a new config flag).

**Tech Stack:** C# / .NET 10, WinForms, xUnit, System.Text.Json (BCL). No new NuGet/binary dependencies.

## Global Constraints

- **Platform:** Windows 10/11 x64, .NET 10, WinForms. Build: `dotnet build`. Test: `dotnet test`.
- **Self-contained / portable:** no new binary dependencies; ships in the existing published folder. `MarkerReviewPage` uses only the BCL.
- **Media by reference, never embedded:** the page references tracks by relative filename; it must live in the same folder as the tracks.
- **Browser dependency:** playback relies on the browser supporting H.264+AAC MP4 and MP3 (true for Chrome/Edge — the Windows default).
- **Test boundary (follow the existing repo):** only pure pieces are unit-tested (`FileNameBuilder`, `MarkerLog`, `MarkerReviewPage`, `AppConfig`). `RecordingSession`, `TrayApp`, and `SettingsForm` are verified by build + manual run — they need live WASAPI/UI and the repo has no tests for them.
- **Commits:** conventional-commit style (`feat:`, `refactor:`), and every commit message ends with the trailer:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- **Decisions:** ADR 0019 (HTML page), 0020 (scope = video + Mixed), 0021 (keep whole Mixed when markers exist), 0022 (balloon/tray + opt-in auto-open). Spec: `docs/superpowers/specs/2026-06-27-marker-review-page-design.md`. Confirmed UI: `docs/markers-review-mockup.html`.

---

### Task 1: `AppConfig.AutoOpenMarkerReview`

**Files:**
- Modify: `src/SPRecorder/Configuration/AppConfig.cs:13`
- Modify: `src/SPRecorder/appsettings.json:27`
- Test: `tests/SPRecorder.Tests/AppConfigStoreTests.cs`

**Interfaces:**
- Produces: `bool AppConfig.AutoOpenMarkerReview` (default `false`).

- [ ] **Step 1: Write the failing tests**

Add to `tests/SPRecorder.Tests/AppConfigStoreTests.cs` (before the final closing brace):

```csharp
    [Fact]
    public void Default_AutoOpenMarkerReview_IsFalse()
        => Assert.False(new AppConfig().AutoOpenMarkerReview);

    [Fact]
    public void Save_RoundtripsAutoOpenMarkerReview()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppConfigStore(path, new AppConfig());
            store.Save(new AppConfig { AutoOpenMarkerReview = true });

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.GetProperty("AutoOpenMarkerReview").GetBoolean());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AppConfigStoreTests"`
Expected: FAIL — `AppConfig` has no member `AutoOpenMarkerReview`.

- [ ] **Step 3: Add the field**

In `src/SPRecorder/Configuration/AppConfig.cs`, immediately after the `MarkerLogFormat` line (line 13):

```csharp
    public bool AutoOpenMarkerReview { get; init; } = false;
```

(No `Load` normalization — a bool binds directly.)

- [ ] **Step 4: Add the appsettings.json key**

In `src/SPRecorder/appsettings.json`, after the `"MarkerLogFormat": "Markdown"` line, add a comma to that line and append:

```json
  "AutoOpenMarkerReview": false
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AppConfigStoreTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SPRecorder/Configuration/AppConfig.cs src/SPRecorder/appsettings.json tests/SPRecorder.Tests/AppConfigStoreTests.cs
git commit -m "feat: add AutoOpenMarkerReview config flag (default off)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `FileNameBuilder.BuildReviewPage`

**Files:**
- Modify: `src/SPRecorder/Recording/FileNameBuilder.cs:33`
- Test: `tests/SPRecorder.Tests/FileNameBuilderTests.cs`

**Interfaces:**
- Produces: `static string FileNameBuilder.BuildReviewPage(string pattern, DateTime timestamp)` → `"{prefix}_review.html"`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/SPRecorder.Tests/FileNameBuilderTests.cs` (before the final closing brace):

```csharp
    [Fact]
    public void BuildReviewPage_ForcesHtmlExtension()
    {
        var name = FileNameBuilder.BuildReviewPage("{timestamp:yyyy-MM-dd_HH-mm-ss}_{track}.mp3", T);
        Assert.Equal("2026-04-27_14-30-22_review.html", name);
    }

    [Fact]
    public void BuildReviewPage_AddsHtml_WhenPatternHasNoExtension()
    {
        var name = FileNameBuilder.BuildReviewPage("{timestamp:yyyy-MM-dd}_{track}", T);
        Assert.Equal("2026-04-27_review.html", name);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~FileNameBuilderTests"`
Expected: FAIL — `BuildReviewPage` not defined.

- [ ] **Step 3: Add the method**

In `src/SPRecorder/Recording/FileNameBuilder.cs`, after `BuildMarker` (after line 33):

```csharp
    public static string BuildReviewPage(string pattern, DateTime timestamp)
    {
        var baseName = Build(pattern, timestamp, "review");
        return Path.ChangeExtension(baseName, ".html");
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~FileNameBuilderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SPRecorder/Recording/FileNameBuilder.cs tests/SPRecorder.Tests/FileNameBuilderTests.cs
git commit -m "feat: add FileNameBuilder.BuildReviewPage for _review.html" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: `MarkerLog` retains entries

**Files:**
- Modify: `src/SPRecorder/Recording/MarkerLog.cs`
- Test: `tests/SPRecorder.Tests/MarkerLogTests.cs`

**Interfaces:**
- Produces: `readonly record struct MarkerEntry(int Seq, TimeSpan Elapsed, DateTime WallClock, string? Note)` in namespace `SPRecorder.Recording`.
- Produces: `IReadOnlyList<MarkerEntry> MarkerLog.Entries`.
- Consumes: existing `MarkerLog.Append(MarkerStamp, string?)`.

- [ ] **Step 1: Write the failing test**

Add to `tests/SPRecorder.Tests/MarkerLogTests.cs` (before the final closing brace):

```csharp
    [Fact]
    public void Append_AccumulatesEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sprec_mk_{Guid.NewGuid():N}.md");
        try
        {
            using var log = new MarkerLog(path, "Markdown");
            log.Append(Stamp(0, 12, 34, 14, 32, 39), "decision");
            log.Append(Stamp(0, 25, 10, 14, 45, 15), null);

            Assert.Equal(2, log.Entries.Count);
            Assert.Equal(1, log.Entries[0].Seq);
            Assert.Equal(new TimeSpan(0, 12, 34), log.Entries[0].Elapsed);
            Assert.Equal("decision", log.Entries[0].Note);
            Assert.Equal(2, log.Entries[1].Seq);
            Assert.Null(log.Entries[1].Note);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MarkerLogTests.Append_AccumulatesEntries"`
Expected: FAIL — `MarkerLog` has no `Entries`.

- [ ] **Step 3: Add the entry type and accumulation**

In `src/SPRecorder/Recording/MarkerLog.cs`, after the existing `MarkerStamp` record (line 6), add:

```csharp
public readonly record struct MarkerEntry(int Seq, TimeSpan Elapsed, DateTime WallClock, string? Note);
```

Inside the `MarkerLog` class, after the `private int _count;` field, add:

```csharp
    private readonly List<MarkerEntry> _entries = new();
    public IReadOnlyList<MarkerEntry> Entries => _entries;
```

In `Append`, immediately after `_count++;`, add:

```csharp
        _entries.Add(new MarkerEntry(_count, stamp.Elapsed, stamp.WallClock, note));
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MarkerLogTests"`
Expected: PASS (the new test and all existing MarkerLog tests).

- [ ] **Step 5: Commit**

```bash
git add src/SPRecorder/Recording/MarkerLog.cs tests/SPRecorder.Tests/MarkerLogTests.cs
git commit -m "feat: MarkerLog retains marker entries for the review page" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: `MarkerReviewPage` HTML renderer

**Files:**
- Create: `src/SPRecorder/Recording/MarkerReviewPage.cs`
- Test: `tests/SPRecorder.Tests/MarkerReviewPageTests.cs`

**Interfaces:**
- Consumes: `MarkerEntry` (Task 3).
- Produces: `readonly record struct ReviewMedia(string Kind, string RelativeFile)` — `Kind` is `"video"` or `"audio"`.
- Produces: `static string MarkerReviewPage.Render(string title, DateTime startedAt, IReadOnlyList<MarkerEntry> markers, IReadOnlyList<ReviewMedia> media)`.
- Produces: `static void MarkerReviewPage.Write(string path, string title, DateTime startedAt, IReadOnlyList<MarkerEntry> markers, IReadOnlyList<ReviewMedia> media)`.
- Produces: `static string MarkerReviewPage.HtmlEscape(string s)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/SPRecorder.Tests/MarkerReviewPageTests.cs`:

```csharp
using SPRecorder.Recording;

namespace SPRecorder.Tests;

public class MarkerReviewPageTests
{
    private static readonly DateTime Start = new(2026, 6, 25, 14, 20, 5);

    private static MarkerEntry E(int seq, int mm, int ss, string? note) =>
        new(seq, new TimeSpan(0, mm, ss), new DateTime(2026, 6, 25, 14, 32, 39), note);

    private static readonly ReviewMedia Video = new("video", "sess_screen.mp4");
    private static readonly ReviewMedia Audio = new("audio", "sess_mixed.mp3");

    [Fact]
    public void HtmlEscape_EscapesEntities()
        => Assert.Equal("&amp;&lt;&gt;&quot;", MarkerReviewPage.HtmlEscape("&<>\""));

    [Fact]
    public void Render_IncludesTitleAndCount()
    {
        var html = MarkerReviewPage.Render("Q2 Planning", Start,
            new[] { E(1, 12, 34, "x") }, new[] { Video });
        Assert.Contains("Q2 Planning", html);
        Assert.Contains("1 marker", html);
    }

    [Fact]
    public void Render_EmbedsMediaFilename()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 5, null) }, new[] { Video });
        Assert.Contains("sess_screen.mp4", html);
    }

    [Fact]
    public void Render_EmbedsElapsedInSeconds()
    {
        // 00:12:34 == 754 seconds in the JS marker array
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 12, 34, "note") }, new[] { Audio });
        Assert.Contains("754", html);
    }

    [Fact]
    public void Render_DefaultsToVideoWhenPresent()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 1, null) }, new[] { Audio, Video });
        Assert.Contains("let activeKind = \"video\"", html);
    }

    [Fact]
    public void Render_AudioOnly_DefaultsToAudio_NoVideoTag()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 1, null) }, new[] { Audio });
        Assert.Contains("let activeKind = \"audio\"", html);
        Assert.DoesNotContain("<video", html);
    }

    [Fact]
    public void Render_NoteIsNotInjectedRaw()
    {
        var html = MarkerReviewPage.Render("t", Start,
            new[] { E(1, 0, 1, "</script><XSS>") }, new[] { Audio });
        Assert.DoesNotContain("<XSS>", html);   // JSON-encoded, never raw markup
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MarkerReviewPageTests"`
Expected: FAIL — `MarkerReviewPage`/`ReviewMedia` not defined.

- [ ] **Step 3: Create the renderer**

Create `src/SPRecorder/Recording/MarkerReviewPage.cs`. (The `Template` field is a raw string literal — keep the closing `"""` at the same indentation as the content's left margin so .NET strips it correctly.)

```csharp
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SPRecorder.Recording;

/// <summary>
/// A track the review page can play. <paramref name="Kind"/> is "video" or "audio";
/// <paramref name="RelativeFile"/> is the track's filename relative to the page
/// (the page lives in the same folder as the tracks).
/// </summary>
public readonly record struct ReviewMedia(string Kind, string RelativeFile);

/// <summary>
/// Renders a self-contained HTML "Marker review page": an embedded player plus a
/// clickable marker list. Clicking a marker seeks the player to that marker's elapsed
/// offset. Media is referenced by relative filename, never embedded. Pure (string only);
/// the browser owns playback.
/// </summary>
public static class MarkerReviewPage
{
    public static void Write(string path, string title, DateTime startedAt,
                             IReadOnlyList<MarkerEntry> markers, IReadOnlyList<ReviewMedia> media)
        => File.WriteAllText(path, Render(title, startedAt, markers, media), Encoding.UTF8);

    public static string Render(string title, DateTime startedAt,
                                IReadOnlyList<MarkerEntry> markers, IReadOnlyList<ReviewMedia> media)
    {
        var subtitle = $"{startedAt:yyyy-MM-dd HH:mm:ss} · {markers.Count} {(markers.Count == 1 ? "marker" : "markers")}";

        var markersJson = JsonSerializer.Serialize(markers.Select(m => new
        {
            seq = m.Seq,
            elapsed = (int)m.Elapsed.TotalSeconds,
            wall = m.WallClock.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            note = m.Note ?? "",
        }));

        string defaultKind = media.Any(m => m.Kind == "video") ? "video" : "audio";

        var mediaTags = new StringBuilder();
        var switcher = new StringBuilder();
        foreach (var m in media)
        {
            bool isDefault = m.Kind == defaultKind;
            string hidden = isDefault ? "" : " style=\"display:none\"";
            string src = HtmlEscape(m.RelativeFile);
            mediaTags.Append(m.Kind == "video"
                ? $"<video id=\"m-video\" class=\"media\" preload=\"metadata\" src=\"{src}\"{hidden}></video>"
                : $"<audio id=\"m-audio\" class=\"media\" preload=\"metadata\" src=\"{src}\"{hidden}></audio>");

            string label = m.Kind == "video" ? "▶  Video (screen)" : "🔊  Mixed audio";
            string active = isDefault ? " class=\"active\"" : "";
            switcher.Append($"<button data-kind=\"{m.Kind}\"{active}>{label}</button>");
        }

        // Switcher only appears when there is more than one track to choose.
        string switcherHtml = media.Count > 1 ? switcher.ToString() : "";

        return Template
            .Replace("__TITLE__", HtmlEscape(title))
            .Replace("__SUBTITLE__", HtmlEscape(subtitle))
            .Replace("__MARKERS_JSON__", markersJson)
            .Replace("__MEDIA__", mediaTags.ToString())
            .Replace("__SWITCHER__", switcherHtml)
            .Replace("__DEFAULT_KIND__", defaultKind);
    }

    public static string HtmlEscape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private const string Template = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__ — marker review</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: #f0f0f0; color: #1f1f1f; padding: 28px 20px 48px; line-height: 1.45; }
  .page { max-width: 1040px; margin: 0 auto; }
  .header { margin-bottom: 18px; }
  .header h1 { font-size: 22px; font-weight: 600; }
  .header .sub { color: #666; font-size: 13px; margin-top: 3px; }
  .layout { display: flex; gap: 20px; align-items: flex-start; }
  .player-col { flex: 1 1 58%; min-width: 0; position: sticky; top: 20px; }
  .markers-col { flex: 1 1 42%; min-width: 0; }
  @media (max-width: 760px) { .layout { flex-direction: column; } .player-col { position: static; width: 100%; } }
  .card { background: #fff; border: 1px solid #d8d8d8; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.06); overflow: hidden; }
  .switch { display: flex; gap: 6px; margin-bottom: 12px; }
  .switch:empty { display: none; }
  .switch button { background: #fff; border: 1px solid #c8c8c8; color: #444; font: inherit; font-size: 13px; padding: 7px 16px; border-radius: 6px; cursor: pointer; }
  .switch button.active { background: #0078d4; border-color: #0078d4; color: #fff; }
  .stage { background: #14171c; display: flex; align-items: center; justify-content: center; }
  .media { width: 100%; display: block; }
  video.media { aspect-ratio: 16 / 9; background: #14171c; }
  audio.media { padding: 22px 14px; }
  .controls { padding: 12px 14px 14px; }
  .timerow { display: flex; justify-content: space-between; font-size: 12px; color: #555; font-family: 'Cascadia Code', Consolas, monospace; margin-bottom: 7px; }
  .timerow .nowat { color: #0078d4; font-weight: 600; }
  .scrub { position: relative; height: 18px; cursor: pointer; }
  .scrub .track-line { position: absolute; top: 7px; left: 0; right: 0; height: 4px; background: #d9d9d9; border-radius: 2px; }
  .scrub .fill { position: absolute; top: 7px; left: 0; height: 4px; width: 0; background: #0078d4; border-radius: 2px; }
  .scrub .head { position: absolute; top: 2px; width: 14px; height: 14px; margin-left: -7px; background: #0078d4; border: 2px solid #fff; border-radius: 50%; left: 0; box-shadow: 0 1px 3px rgba(0,0,0,.3); }
  .scrub .tick { position: absolute; top: 3px; width: 3px; height: 12px; background: #f0a30a; border-radius: 1px; margin-left: -1.5px; cursor: pointer; }
  .play-btn { display: flex; align-items: center; gap: 12px; margin-top: 12px; }
  .play-btn #playBtn { width: 38px; height: 38px; border-radius: 50%; border: none; background: #0078d4; color: #fff; font-size: 15px; cursor: pointer; }
  .play-btn .hint { font-size: 12px; color: #888; }
  .play-btn .fs-btn { margin-left: auto; border: 1px solid #c8c8c8; background: #fff; color: #333; border-radius: 6px; font: inherit; font-size: 13px; padding: 7px 13px; cursor: pointer; }
  #reviewWrap:fullscreen { background: #14171c; padding: 26px; gap: 26px; }
  #reviewWrap:fullscreen .player-col { flex: 1 1 72%; }
  #reviewWrap:fullscreen .markers-col { flex: 1 1 28%; max-height: 100vh; overflow: auto; }
  #reviewWrap:fullscreen .markers-col h2 { color: #aebccd; }
  .markers-col h2 { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: .8px; color: #888; margin: 4px 2px 10px; }
  .mlist { display: flex; flex-direction: column; }
  .marker { display: flex; gap: 12px; align-items: flex-start; padding: 12px 14px; border: 1px solid #e2e2e2; border-bottom: none; cursor: pointer; background: #fff; }
  .marker:first-child { border-radius: 8px 8px 0 0; }
  .marker:last-child { border-bottom: 1px solid #e2e2e2; border-radius: 0 0 8px 8px; }
  .marker:hover { background: #f5f9fe; }
  .marker.active { background: #e8f2fc; border-left: 3px solid #0078d4; padding-left: 11px; }
  .marker .seq { flex: none; width: 30px; height: 30px; border-radius: 50%; background: #eef1f4; color: #555; font-size: 13px; font-weight: 600; display: flex; align-items: center; justify-content: center; }
  .marker.active .seq { background: #0078d4; color: #fff; }
  .marker .ts { font-family: 'Cascadia Code', Consolas, monospace; font-size: 14px; color: #1f1f1f; }
  .marker .wall { color: #9a9a9a; font-size: 12px; margin-left: 6px; font-family: 'Cascadia Code', Consolas, monospace; }
  .marker .note { font-size: 13px; color: #444; margin-top: 3px; }
  .marker .note.empty { color: #b3b3b3; font-style: italic; }
</style>
</head>
<body>
<div class="page">
  <div class="header">
    <h1>__TITLE__</h1>
    <div class="sub">__SUBTITLE__</div>
  </div>
  <div class="layout" id="reviewWrap">
    <div class="player-col">
      <div class="switch" id="switch">__SWITCHER__</div>
      <div class="card">
        <div class="stage" id="stage">__MEDIA__</div>
        <div class="controls">
          <div class="timerow"><span class="nowat" id="nowAt">00:00:00</span><span id="dur">00:00:00</span></div>
          <div class="scrub" id="scrub"><div class="track-line"></div><div class="fill" id="fill"></div><div class="head" id="head"></div></div>
          <div class="play-btn"><button id="playBtn">▶</button><span class="hint" id="playHint"></span><button class="fs-btn" id="fsBtn">⛶ Fullscreen</button></div>
        </div>
      </div>
    </div>
    <div class="markers-col">
      <h2>Markers — click to jump</h2>
      <div class="mlist" id="mlist"></div>
    </div>
  </div>
</div>
<script>
const MARKERS = __MARKERS_JSON__;
let activeKind = "__DEFAULT_KIND__";
const $ = id => document.getElementById(id);
const mediaEl = () => $("m-" + activeKind);
const pad = n => String(n).padStart(2, "0");
const fmt = s => { s = Math.max(0, Math.floor(s)); return pad(s / 3600 | 0) + ":" + pad((s % 3600) / 60 | 0) + ":" + pad(s % 60); };

function positionTicks() {
  const scrub = $("scrub");
  scrub.querySelectorAll(".tick").forEach(t => t.remove());
  const dur = mediaEl().duration;
  if (!isFinite(dur) || dur <= 0) return;
  MARKERS.forEach(mk => {
    const tick = document.createElement("div");
    tick.className = "tick";
    tick.style.left = Math.min(100, mk.elapsed / dur * 100) + "%";
    tick.title = "#" + mk.seq + " · " + fmt(mk.elapsed);
    tick.addEventListener("click", e => { e.stopPropagation(); jumpTo(mk); });
    scrub.insertBefore(tick, $("head"));
  });
  $("dur").textContent = fmt(dur);
}

function refreshProgress() {
  const m = mediaEl();
  const dur = m.duration || 0;
  const pct = dur ? m.currentTime / dur * 100 : 0;
  $("fill").style.width = pct + "%";
  $("head").style.left = pct + "%";
  $("nowAt").textContent = fmt(m.currentTime);
}

function jumpTo(mk) {
  const m = mediaEl();
  m.currentTime = mk.elapsed;
  m.play().catch(() => {});
  $("playHint").textContent = "Jumped to #" + mk.seq + " — " + fmt(mk.elapsed);
  document.querySelectorAll(".marker").forEach(el => el.classList.remove("active"));
  const row = document.querySelector('.marker[data-seq="' + mk.seq + '"]');
  if (row) row.classList.add("active");
}

const mlist = $("mlist");
MARKERS.forEach((mk, i) => {
  const row = document.createElement("div");
  row.className = "marker";
  row.dataset.seq = mk.seq;
  if (i === 0) row.classList.add("active");
  const seq = document.createElement("div");
  seq.className = "seq";
  seq.textContent = mk.seq;
  const meta = document.createElement("div");
  meta.className = "meta";
  const line = document.createElement("div");
  const ts = document.createElement("span");
  ts.className = "ts";
  ts.textContent = fmt(mk.elapsed);
  const wall = document.createElement("span");
  wall.className = "wall";
  wall.textContent = "(" + mk.wall + ")";
  line.appendChild(ts);
  line.appendChild(wall);
  const note = document.createElement("div");
  note.className = mk.note ? "note" : "note empty";
  note.textContent = mk.note ? mk.note : "no note";
  meta.appendChild(line);
  meta.appendChild(note);
  row.appendChild(seq);
  row.appendChild(meta);
  row.addEventListener("click", () => jumpTo(mk));
  mlist.appendChild(row);
});

function bind(el) {
  el.addEventListener("loadedmetadata", () => { if (el === mediaEl()) { positionTicks(); refreshProgress(); } });
  el.addEventListener("timeupdate", () => { if (el === mediaEl()) refreshProgress(); });
  el.addEventListener("play", () => { if (el === mediaEl()) $("playBtn").textContent = "⏸"; });
  el.addEventListener("pause", () => { if (el === mediaEl()) $("playBtn").textContent = "▶"; });
}
["video", "audio"].forEach(k => { const el = $("m-" + k); if (el) bind(el); });
if (mediaEl().readyState >= 1) { positionTicks(); refreshProgress(); }

$("playBtn").addEventListener("click", () => { const m = mediaEl(); if (m.paused) m.play(); else m.pause(); });

$("scrub").addEventListener("click", e => {
  const m = mediaEl();
  const dur = m.duration;
  if (!dur) return;
  const rect = e.currentTarget.getBoundingClientRect();
  m.currentTime = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width)) * dur;
});

document.querySelectorAll("#switch button").forEach(btn => btn.addEventListener("click", () => {
  document.querySelectorAll("#switch button").forEach(b => b.classList.remove("active"));
  btn.classList.add("active");
  const cur = mediaEl();
  cur.pause();
  cur.style.display = "none";
  activeKind = btn.dataset.kind;
  const next = mediaEl();
  next.style.display = "";
  if (next.readyState >= 1) { positionTicks(); refreshProgress(); }
}));

const fsBtn = $("fsBtn"), wrap = $("reviewWrap");
fsBtn.addEventListener("click", () => {
  if (document.fullscreenElement) document.exitFullscreen();
  else if (wrap.requestFullscreen) wrap.requestFullscreen();
});
document.addEventListener("fullscreenchange", () => {
  fsBtn.textContent = document.fullscreenElement ? "⛶ Exit fullscreen" : "⛶ Fullscreen";
});
</script>
</body>
</html>
""";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MarkerReviewPageTests"`
Expected: PASS (all 7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SPRecorder/Recording/MarkerReviewPage.cs tests/SPRecorder.Tests/MarkerReviewPageTests.cs
git commit -m "feat: add MarkerReviewPage HTML renderer" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: `RecordingSession` generates the page

**Files:**
- Modify: `src/SPRecorder/Recording/RecordingSession.cs`

**Interfaces:**
- Consumes: `FileNameBuilder.BuildReviewPage` (Task 2), `MarkerLog.Entries`/`MarkerEntry` (Task 3), `MarkerReviewPage.Write`/`ReviewMedia` (Task 4).
- Produces: `string? RecordingSession.ReviewPagePath`.
- Produces: `event Action<string>? RecordingSession.ReviewPageReady` — arg is the final review-page path; fired once after the page is written.

> **Testing:** verified by build + manual run (Step 7) — `RecordingSession` requires live WASAPI capture and is not unit-tested, consistent with the audio-splitting work.

- [ ] **Step 1: Add the property and event**

In `src/SPRecorder/Recording/RecordingSession.cs`, after `public string? MarkerLogPath { get; private set; }` (line 32):

```csharp
    public string? ReviewPagePath { get; private set; }
```

After `public event Action<int, TimeSpan>? MarkerAdded;` (line 39):

```csharp
    public event Action<string>? ReviewPageReady;   // arg = final review-page path
```

- [ ] **Step 2: Compute the path at Start()**

In `Start()`, immediately after the `_markerLog = new MarkerLog(...)` line (line 70), add:

```csharp
        ReviewPagePath = Path.Combine(_activeConfig.OutputDirectory,
            FileNameBuilder.BuildReviewPage(_activeConfig.FileNamePattern, _startedAt));
```

- [ ] **Step 3: Repoint the path on session-folder rename**

In `TryRenameToSessionFolder()`, inside the `try` block after the marker-log move block (after line 231, before the closing `}` of `try`), add:

```csharp
            if (ReviewPagePath is not null)
                ReviewPagePath = Path.Combine(folder, $"{folderName}_review.html");
```

(The page does not exist yet, so this only repoints the string to its final location.)

- [ ] **Step 4: Add the `preserveOriginal` parameter to `SplitTrack`**

Replace the `SplitTrack` method (lines 287–308) with:

```csharp
    private int SplitTrack(IMp3Splitter splitter, string path, AppConfig cfg, bool preserveOriginal = false)
    {
        if (!File.Exists(path)) return 0;
        try
        {
            var chunks = cfg.SplitMode.Equals("Time", StringComparison.OrdinalIgnoreCase)
                ? splitter.SplitByTime(path, TimeSpan.FromMinutes(cfg.SplitTimeMinutes))
                : splitter.SplitBySize(path, (long)cfg.SplitSizeMb * 1024L * 1024L);

            if (chunks.Count > 1)
            {
                if (!preserveOriginal) File.Delete(path);   // ADR 0021: keep whole Mixed when markers exist
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

- [ ] **Step 5: Add the page-writing helper**

Add this method to `RecordingSession` (e.g. after `SplitTrack`):

```csharp
    private void WriteReviewPage(string? reviewPath, string? label, DateTime startedAt,
                                 IReadOnlyList<MarkerEntry> entries, string? screenPath, string? mixedPath)
    {
        if (reviewPath is null || entries.Count == 0) return;

        var media = new List<ReviewMedia>();
        if (screenPath is not null && File.Exists(screenPath))
            media.Add(new ReviewMedia("video", Path.GetFileName(screenPath)));
        if (mixedPath is not null && File.Exists(mixedPath))
            media.Add(new ReviewMedia("audio", Path.GetFileName(mixedPath)));
        if (media.Count == 0) return;   // no playable track (ADR 0020) — marker log still stands

        var title = string.IsNullOrWhiteSpace(label)
            ? startedAt.ToString("yyyy-MM-dd HH:mm:ss")
            : label!;
        try
        {
            MarkerReviewPage.Write(reviewPath, title, startedAt, entries, media);
            ReviewPageReady?.Invoke(reviewPath);
        }
        catch (Exception ex)
        {
            Warning?.Invoke("Marker review page could not be written: " + ex.Message);
        }
    }
```

- [ ] **Step 6: Wire generation into Stop() and the background task**

In `Stop()`, replace the tail (lines 184–188):

```csharp
        var willMix   = _activeConfig.MixedFileEnabled;
        var willSplit = !_activeConfig.SplitMode.Equals("None", StringComparison.OrdinalIgnoreCase);

        if (willMix || willSplit)
            StartPostProcessingInBackground(willMix, willSplit);
```

with:

```csharp
        var willMix   = _activeConfig.MixedFileEnabled;
        var willSplit = !_activeConfig.SplitMode.Equals("None", StringComparison.OrdinalIgnoreCase);

        if (willMix || willSplit)
            StartPostProcessingInBackground(willMix, willSplit);
        else
            WriteReviewPage(ReviewPagePath, _sessionLabel, _startedAt,
                _markerLog?.Entries ?? (IReadOnlyList<MarkerEntry>)Array.Empty<MarkerEntry>(),
                ScreenFilePath, null);   // no mix/split → only the screen video can be a track
```

In `StartPostProcessingInBackground`, after the existing local captures (after line 247, `var stereo = ...`), add:

```csharp
        var screenPath = ScreenFilePath;
        var label      = _sessionLabel;
        var startedAt  = _startedAt;
        var reviewPath = ReviewPagePath;
        var entries    = _markerLog is { } ml
            ? new List<MarkerEntry>(ml.Entries)
            : new List<MarkerEntry>();
        bool keepMixedWhole = entries.Count > 0;   // ADR 0021
```

Inside the `Task.Run` lambda, change the Mixed split call (line 278–279) to pass `preserveOriginal`:

```csharp
                if (cfg.SplitMixedTrack && finalMixedPath is not null)
                                             totalChunks += SplitTrack(splitter, finalMixedPath, cfg, keepMixedWhole);
```

Still inside the lambda, after `if (willSplit) SplitCompleted?.Invoke(totalChunks);` (line 283), add:

```csharp
            WriteReviewPage(reviewPath, label, startedAt, entries, screenPath, finalMixedPath);
```

- [ ] **Step 7: Build, test, and manually verify**

Run: `dotnet build` then `dotnet test`
Expected: build succeeds; all tests pass (no regressions).

Manual verification (record short sessions):
- Screen on + drop 2 markers + stop → a `_review.html` appears beside the tracks; opening it plays the video and clicking a marker jumps it; ⛶ Fullscreen enlarges the page and markers still jump; switch to **Mixed audio** works.
- Audio-only (screen off, Mixed on) + markers → page plays `_mixed.mp3`, jump is exact, no switcher.
- `SplitMode = Size`, tiny size, + markers → the whole `_mixed.mp3` is **kept** alongside `_mixed_001.mp3…`; the page references and jumps the whole file.
- No markers → no `_review.html` is created.

- [ ] **Step 8: Commit**

```bash
git add src/SPRecorder/Recording/RecordingSession.cs
git commit -m "feat: generate marker review page after recording; keep whole Mixed when markers exist" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: `TrayApp` balloon + "Open marker review"

**Files:**
- Modify: `src/SPRecorder/Tray/TrayApp.cs`

**Interfaces:**
- Consumes: `RecordingSession.ReviewPageReady` (Task 5), `AppConfig.AutoOpenMarkerReview` (Task 1).

> **Testing:** build + manual run — `TrayApp` is UI-bound and not unit-tested in this repo.

- [ ] **Step 1: Add fields**

In `src/SPRecorder/Tray/TrayApp.cs`, after `private int _markerCount;` (line 38):

```csharp
    private ToolStripMenuItem _openReviewItem = null!;
    private string? _lastReviewPagePath;
    private Action? _onBalloonClick;
```

- [ ] **Step 2: Subscribe to the event**

In the constructor, after `_session.MarkerAdded += ...` (line 59):

```csharp
        _session.ReviewPageReady += path => OnUi(() => OnReviewPageReady(path));
```

- [ ] **Step 3: Create the menu item and insert it after "Open recordings folder"**

Before `var menu = new ContextMenuStrip();` (line 82), add:

```csharp
        _openReviewItem = new ToolStripMenuItem("Open marker review", null, (_, _) => OpenReviewPage())
        {
            Visible = false,
        };
```

Replace the line `menu.Items.Add("Open recordings folder", null, (_, _) => OpenFolder());` (line 90) with:

```csharp
        menu.Items.Add("Open recordings folder", null, (_, _) => OpenFolder());
        menu.Items.Add(_openReviewItem);
```

- [ ] **Step 4: Wire the balloon click**

After the `_notifyIcon.MouseClick += ...` line (line 103), add:

```csharp
        _notifyIcon.BalloonTipClicked += (_, _) => { var a = _onBalloonClick; _onBalloonClick = null; a?.Invoke(); };
```

- [ ] **Step 5: Clear the click handler on every balloon**

In `ShowBalloon` (line 473), add as the **first** statement of the method body:

```csharp
        _onBalloonClick = null;
```

- [ ] **Step 6: Add the handlers**

Add these methods to `TrayApp` (e.g. after `OnMixingCompleted`):

```csharp
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
```

- [ ] **Step 7: Build and manually verify**

Run: `dotnet build`
Expected: build succeeds.

Manual verification:
- Record with markers → after post-processing, a "Marker review ready" balloon appears; clicking it opens the page; the tray menu shows **Open marker review** which opens the last page.
- Set `AutoOpenMarkerReview = true` (Settings, Task 7) → the page opens automatically on stop.
- Record with no markers → no balloon, no menu item appears.

- [ ] **Step 8: Commit**

```bash
git add src/SPRecorder/Tray/TrayApp.cs
git commit -m "feat: tray balloon + Open marker review item; optional auto-open" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Settings — "Open review page" checkbox

**Files:**
- Modify: `src/SPRecorder/Settings/SettingsForm.cs`

**Interfaces:**
- Consumes: `AppConfig.AutoOpenMarkerReview` (Task 1).

> **Testing:** build + manual run — `SettingsForm` is UI-bound and not unit-tested in this repo.

- [ ] **Step 1: Add the field**

In `src/SPRecorder/Settings/SettingsForm.cs`, near the other marker controls (the field declarations around line 30), add:

```csharp
    private CheckBox _autoOpenReview = null!;
```

- [ ] **Step 2: Add the checkbox to the Markers tab**

In `BuildMarkersTab()`, replace the final lines (the `page.Controls.Add(_markerCsv);` line and `return page;`) with:

```csharp
        page.Controls.Add(_markerCsv);
        y += 26 + FieldGap;

        _autoOpenReview = new CheckBox
        {
            Text = "Open review page in browser when recording stops",
            Location = new Point(TabPad, y),
            Size = new Size(InputWidth, 24),
        };
        page.Controls.Add(_autoOpenReview);
        y += 24 + 2;
        page.Controls.Add(MakeHint(
            "A clickable page whose markers jump to that moment in the video/audio. A balloon and tray item always appear; this also auto-opens it.",
            TabPad + 22, y));

        return page;
```

- [ ] **Step 3: Load the value into the control**

In `ApplyConfigToControls`, after the marker-format lines (after line 711, `_markerMarkdown.Checked = !csv;`):

```csharp
        _autoOpenReview.Checked = cfg.AutoOpenMarkerReview;
```

- [ ] **Step 4: Persist the value on Save**

In `Save_Click`, in the `Result = _initial with { ... }` initializer, after the `MarkerLogFormat = ...` line (line 827):

```csharp
            AutoOpenMarkerReview = _autoOpenReview.Checked,
```

- [ ] **Step 5: Build and manually verify**

Run: `dotnet build`
Expected: build succeeds.

Manual verification: open Settings → Markers tab → the checkbox appears, reflects the saved value, and toggling + Save persists it (re-open Settings to confirm; check `appsettings.json`).

- [ ] **Step 6: Commit**

```bash
git add src/SPRecorder/Settings/SettingsForm.cs
git commit -m "feat: Settings toggle to auto-open the marker review page" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:**
- Navigable HTML page (ADR 0019) → Tasks 4 + 5.
- Scope video + Mixed, default video (ADR 0020) → Task 4 (`defaultKind`, switcher) + Task 5 (`WriteReviewPage` media list).
- Keep whole Mixed when markers exist (ADR 0021) → Task 5 (`preserveOriginal`/`keepMixedWhole`).
- Balloon + tray item + opt-in auto-open (ADR 0022) → Tasks 1, 6, 7.
- Lazy generation, only with ≥1 marker + a playable track → Task 5 (`WriteReviewPage` guards).
- Written after post-processing to its final location; `_review.html` naming → Tasks 2 + 5.
- Fullscreen, marker ticks, clickable list, escaping → Task 4.
- Edge cases (no markers, no playable track, note escaping, split kept) → Task 4 tests + Task 5 guards + Task 5 manual checks.

**Placeholder scan:** none — every step carries full code/commands.

**Type consistency:** `MarkerEntry` (Task 3) is consumed unchanged by Tasks 4 + 5; `ReviewMedia(Kind, RelativeFile)` (Task 4) is constructed in Task 5; `ReviewPageReady(string)` (Task 5) is subscribed in Task 6; `AutoOpenMarkerReview` (Task 1) is read in Tasks 6 + 7; `BuildReviewPage` (Task 2) is called in Task 5. Names and signatures match across tasks.
