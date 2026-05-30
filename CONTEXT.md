# SPRecorder

A Windows tray app that records a meeting as two separate audio tracks and a
combined file, so the conversation can be handed to an AI summarizer. This
glossary fixes the language the project uses for those artifacts and for
delivering them to the cloud.

## Recording

**Recording Session**:
One start-to-stop capture, identified by its start time. Produces a System
track, a Mic track, and (optionally) a Mixed file.
_Avoid_: recording job, capture run

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

**Named session**:
An optional user-supplied label for a Recording Session, used to name its
folder and files so the topic is obvious later.
_Avoid_: title, tag, project name

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
