# SPRecorder

A Windows tray app that records a meeting as two separate audio tracks and a
combined file — and, optionally, the screen — so the conversation can be handed
to an AI summarizer such as NotebookLM, and optionally delivered to the user's
cloud. This glossary fixes the language the project uses for those artifacts.

## Recording

**Recording Session**:
One start-to-stop capture, identified by its start time. Produces a System
track, a Mic track, and (optionally) a Mixed file and a Screen recording.
_Avoid_: recording job, capture run

**Track**:
One captured stream saved as its own file. The audio tracks are `system` and
`mic`; the post-mixed audio is the Mixed file (`mixed` track); the optional video
output is the `screen` track.
_Avoid_: channel, stream, source

**System track**:
The MP3 of system/loopback audio — what the other participants say through the
speakers.
_Avoid_: speaker track, output audio, "them"

**Mic track**:
The MP3 of the local microphone — the user's own voice.
_Avoid_: input track, "me"

**Mixed file**:
The single combined MP3 of the System track and Mic track. This is the
artifact that gets shared with an AI summarizer and uploaded to Drive; the two
tracks are kept locally as the archive.
_Avoid_: merged file, output file, final file

**Screen recording**:
The optional `screen` track — an MP4 of one chosen monitor (default primary)
with the meeting audio (system + mic) embedded, so the file plays back on its
own.
_Avoid_: video capture, screencast, screen grab

**Self-contained MP4**:
A single `.mp4` that carries both the screen video and the embedded audio, so it
is watchable without the separate MP3 tracks.
_Avoid_: muxed file, combined video

**Recorded monitor**:
The single display the `screen` track captures, picked in Settings (default
primary). The key caster overlay is pinned to this monitor — not the one the
user is focused on — so it appears in the video.
_Avoid_: target screen, active monitor

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

**Inactive hotkey**:
A configured global hotkey whose `RegisterHotKey` call failed because another app
already owns the combo — so pressing it does nothing until the user rebinds it. Its
status comes from `GlobalHotkey.IsRegistered`, not from a re-probe.
_Avoid_: broken hotkey, disabled hotkey, dead key

**Record-screen toggle**:
The single opt-in switch for the `screen` track — a persisted setting reachable
both from the Settings dialog and a checkable tray menu item. There is no
per-recording prompt.
_Avoid_: screen checkbox, video flag

**Named session**:
An optional user-supplied label for a Recording Session, used to name its
folder and files so the topic is obvious later.
_Avoid_: title, tag, project name

**Session folder**:
The `OutputDirectory/<name>_<timestamp>/` folder created when the user opts into
naming a recording. All tracks for that recording — including `screen` — move
into it.

## Markers

**Marker**:
A timestamped point of interest the user flags while a Recording Session is
running, captured as an elapsed offset from session start with an optional short
note. Used to find or summarize the important moments afterward.
_Avoid_: bookmark, flag, chapter, cue point

**Marker log**:
The sidecar text file (one per Recording Session) that lists all Markers for that
session. Kept separate from the tracks so it survives mixing and splitting, and so
it can be handed to an AI summarizer as its own source.
_Avoid_: marker file, notes file, index

## Drive delivery

**Upload**:
Delivering the Mixed file of a Recording Session to the user's own Google
Drive. Each user uploads to their own account.
_Avoid_: sync, backup, export

**Connected account**:
The Google account a user has linked once, via browser consent, to receive
their Uploads. One per installation.
_Avoid_: login, profile, Drive account

**Pending upload**:
A Mixed file that has been produced but not yet confirmed present in Drive —
e.g. recorded offline or after a failed attempt. Retried until it lands.
_Avoid_: queued file, unsent file, backlog item
