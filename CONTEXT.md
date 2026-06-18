# SPRecorder

A Windows tray app that records a meeting as separate audio tracks (and,
optionally, the screen) for later upload to an AI summarizer such as NotebookLM.

## Language

**Track**:
One captured stream saved as its own file. The audio tracks are `system` (what
other participants say, via loopback) and `mic` (your voice). The post-mixed
audio is the `mixed` track. The new video output is the `screen` track.
_Avoid_: channel, stream, source

**Screen recording**:
The optional `screen` track — an MP4 of one chosen monitor (default primary)
with the meeting audio (system + mic) embedded, so the file plays back on its
own.
_Avoid_: video capture, screencast, screen grab

**Recorded monitor**:
The single display the `screen` track captures, picked in Settings (default
primary). The key caster overlay is pinned to this monitor — not the one the
user is focused on — so it appears in the video.
_Avoid_: target screen, active monitor

**Self-contained MP4**:
A single `.mp4` that carries both the screen video and the embedded audio, so it
is watchable without the separate MP3 tracks.
_Avoid_: muxed file, combined video

**Input highlight**:
The on-screen visual feedback drawn over the desktop while the `screen` track
records — a ripple where the mouse is clicked, and an on-screen caption of the
keys being pressed. Rendered as transparent overlay windows so the screen
recorder captures them into the video.
_Avoid_: cursor effect, keystroke display, visualizer

**Mouse highlight**:
The click ripple part of the input highlight (drawn by the screen-capture
library).

**Key caster**:
The keyboard part of the input highlight — a KeyCastr-style caption showing the
keys pressed. Shows every key (privacy trade-off accepted; see ADR 0003).
_Avoid_: keylogger, key display

**Record-screen toggle**:
The single opt-in switch for the `screen` track — a persisted setting reachable
both from the Settings dialog and a checkable tray menu item. There is no
per-recording prompt.
_Avoid_: screen checkbox, video flag

**Session folder**:
The `OutputDirectory/<name>_<timestamp>/` folder created when the user opts into
naming a recording. All tracks for that recording — including `screen` — move
into it.
