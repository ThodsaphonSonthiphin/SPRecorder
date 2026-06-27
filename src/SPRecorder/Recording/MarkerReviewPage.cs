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
