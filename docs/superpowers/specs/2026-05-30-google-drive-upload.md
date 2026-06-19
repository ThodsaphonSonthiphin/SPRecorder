# SPRecorder — Upload to Google Drive

## Context

Today every recording stays on the local machine. The user's workflow is to take
the **Mixed file** (`_mixed.mp3`) and upload it to an AI summarizer (NotebookLM)
by hand. This work makes that automatic: after each recording is mixed, the
Mixed file is uploaded to the user's **own Google Drive**.

The intended audience is **non-technical users** running the app on machines
where they have **no admin rights and cannot install software** — so the feature
must preserve the existing distribution model (a self-contained, copy-and-run
single-file `.exe`) and must not assume any technical setup by the end user.

The architectural decision behind this feature — OAuth installed-app flow to each
user's own Drive, with a production-verified app — is recorded in
[ADR-0001](../../adr/0001-google-drive-upload-via-oauth-installed-app.md). The
domain vocabulary (Mixed file, Upload, Connected account, Pending upload) is in
[CONTEXT.md](../../../CONTEXT.md). This spec is the implementation detail those
two documents deliberately exclude.

## Tech additions

- **`Google.Apis.Drive.v3`** + **`Google.Apis.Auth`** — official Google .NET
  client. Provides OAuth installed-app flow (loopback redirect) and resumable
  media upload. Breaks the prior "no new NuGet packages" stance — see ADR-0001.
- **`System.Security.Cryptography.ProtectedData`** — DPAPI wrapper to encrypt the
  stored refresh token at rest.
- Both bundle into the existing self-contained single-file publish — **no
  installer, no admin rights** required of the end user.

### OAuth client (developer-side, not per-user)

A single Google Cloud OAuth **client ID/secret** (Desktop app type) is created
once by the maintainer and **embedded in the build**. For an installed app the
"secret" is not confidential (PKCE protects the flow), but it is **not committed
to the repo** — it is injected at build time / read from a non-committed
`google-oauth-client.json` and `.gitignore`d. The OAuth app must be taken to
**"In production" publishing status and verified** for the `drive.file` scope
before distribution; otherwise refresh tokens expire after 7 days, there is a
100-user cap, and users see an "unverified app" warning.

Scope requested: **`https://www.googleapis.com/auth/drive.file`** only
(least privilege — the app can only see/manage files it created).

## Data model — extended `AppConfig`

`Configuration/AppConfig.cs` gains three fields. All have defaults so existing
`appsettings.json` files keep loading.

```csharp
public sealed record AppConfig
{
    // ... existing fields ...

    // new — Google Drive upload
    public bool DriveUploadEnabled            { get; init; } = false;
    public bool DriveUploadConsentAcknowledged { get; init; } = false;
    public string DriveConnectedEmail         { get; init; } = ""; // display only; "" = not connected
}
```

Notes:
- The **refresh token is NOT stored in `appsettings.json`** — see Token storage.
  `DriveConnectedEmail` is a display convenience only (so the Settings tab and
  tray can show "Connected as …" without a network call).
- `appsettings.json` lives next to the portable `.exe`, which may be on a shared
  or read-only location — never put a credential there.

## New components

```
src/SPRecorder/Drive/
  DriveAuthenticator.cs    Runs the OAuth loopback flow (GoogleWebAuthorizationBroker
                           / installed-app), revokes on disconnect, exposes
                           IsConnected + connected email. Wraps the token store.
  TokenStore.cs            Reads/writes the refresh token under %AppData%\SPRecorder,
                           encrypted with DPAPI (ProtectedData, CurrentUser).
  DriveUploader.cs         Ensures the "SPRecorder" folder exists (cached id),
                           dedup-checks by filename, performs resumable upload,
                           returns the Drive file id. Classifies failures as
                           Auth vs Transient.
  UploadQueue.cs           Durable queue of Pending uploads (JSON under %AppData%).
                           Enqueue, drain-on-demand, background drain w/ backoff,
                           pause-on-auth-failure. Raises status events.
  DriveUploadState.cs      enum { NotConnected, UpToDate, Uploading, Pending,
                           ReconnectNeeded } + a small status record for the UI.
```

## Updates to existing files

| File | Change |
|---|---|
| `Configuration/AppConfig.cs` | Add the three fields above. |
| `Recording/RecordingSession.cs` | No structural change. The existing `MixingCompleted(string? path)` event is the upload hook — `TrayApp` enqueues on success when enabled. |
| `Tray/TrayApp.cs` | Own a `DriveAuthenticator` + `UploadQueue`. On `MixingCompleted(path)` with `DriveUploadEnabled && IsConnected` → enqueue. Add tray menu items + disabled status line (below). Subscribe to queue status events → toasts + status line. Drive recordings are **never** auto-deleted. |
| `Settings/SettingsForm.cs` | Add a 4th tab "Google Drive" (below). |
| `Program.cs` | Construct `DriveAuthenticator` (loads token store) and `UploadQueue`; pass to `TrayApp`. On startup, if connected and there are Pending items, kick a background drain. |
| `README.md` | New "Google Drive upload" section; one-time Google Cloud setup is maintainer-only (users just click Connect). |
| `.gitignore` | Add `google-oauth-client.json`. |

## Behavior

### Upload trigger

- **Auto (opt-in).** `DriveUploadEnabled == true` **and** an account is connected:
  on `MixingCompleted(path != null)`, enqueue `path`; the background drainer
  uploads it.
- **Auto off.** Nothing is auto-enqueued. The tray offers **"Upload last
  recording to Drive"** (enqueues + drains the most recent Mixed file) and
  **"Upload pending now"** (force-drains anything left from earlier failures).
  Manual actions are available whenever an account is connected, regardless of
  the toggle.
- **Not connected.** Feature dormant; nothing is enqueued.
- Only the **Mixed file** is uploaded. If `MixedFileEnabled == false`, there is
  nothing to upload and the feature is effectively inert.

### Upload mechanics (`DriveUploader`)

1. **Ensure folder.** Find a folder named `SPRecorder` created by this app
   (`files.list q: name='SPRecorder' and mimeType='application/vnd.google-apps.folder' and trashed=false`);
   create it if absent. Cache the folder id (re-resolve if a later call 404s).
2. **Dedup.** Before uploading, list the folder for a file with the exact target
   filename. **If present → treat as already uploaded** (record its id, mark the
   queue entry done). This closes the crash-window between a successful upload
   and recording success locally — retries can never create duplicates.
3. **Upload.** Resumable media upload (`FilesResource.CreateMediaUpload`,
   `application/mpeg`) into the folder. Resumable survives network drops.
4. On success, store the returned Drive file id on the queue entry and mark done.

### Failure classification

`DriveUploader` maps exceptions to:
- **Transient** — `HttpRequestException`, timeouts, 5xx, 429, no network.
  → queue retries with exponential backoff (cap, e.g., 5 min), quietly.
- **Auth** — `invalid_grant`, 401 after a token refresh attempt, revoked token.
  → queue **pauses auto-retry**, sets state `ReconnectNeeded`, keeps all Pending
  items intact, surfaces a persistent "Reconnect Google Drive" prompt. A
  successful reconnect resumes the drain.

### Durable queue (`UploadQueue`)

- Persisted as JSON under `%AppData%\SPRecorder\upload-queue.json`. Each entry:
  local path, target filename, enqueued-at, attempt count, last error class,
  Drive file id (once known), state.
- Survives app restarts and offline recording. On startup, if connected and
  non-empty, drain in the background.
- The **local Mixed file is the source of truth**; the queue entry only tracks
  "not yet confirmed in Drive". A missing local file (user deleted it) → drop the
  entry with an info toast.
- Backoff retries on Transient; pauses on Auth.

### Token storage (`TokenStore`)

- Location: `%AppData%\SPRecorder\drive-token.dat` (per Windows user, follows the
  user, never next to the portable exe).
- The refresh token JSON is encrypted with **DPAPI `ProtectedData.Protect(...,
  DataProtectionScope.CurrentUser)`** — bound to the Windows account, useless if
  copied to another machine/user.
- Google's default `FileDataStore` (plaintext) is **not** used directly; we wrap
  token persistence through `TokenStore`.

## Connect / disconnect flow

### Connect

1. Entry points: the **Connect** button on the Google Drive Settings tab, or
   toggling **auto-upload ON while not connected** (which launches Connect
   immediately — no silent dead-ends).
2. **Compliance gate (first enable only).** If `DriveUploadConsentAcknowledged ==
   false`, show a one-time accept-to-continue dialog *before* the OAuth flow:
   > "Recordings will be uploaded to your Google Drive and will leave this
   > computer. Make sure meeting participants are aware and that you have
   > permission to record and store the audio."
   `[ Cancel ]  [ I understand ]`. On accept, set
   `DriveUploadConsentAcknowledged = true` (persisted) and never show again.
3. **OAuth.** `GoogleWebAuthorizationBroker.AuthorizeAsync` with the embedded
   client + `drive.file` scope. This opens the **system browser** and runs a
   temporary `127.0.0.1:<port>` loopback listener to catch the redirect
   (out-of-band copy-paste is deprecated by Google). User signs into their own
   Google account and consents.
4. On success: persist the refresh token via `TokenStore`, fetch the account
   email (`about.get` / userinfo) → store in `DriveConnectedEmail`, toast
   *"Connected to Google Drive as user@example.com"*. State → `UpToDate`.
5. On cancel/failure: state stays `NotConnected`; if Connect was triggered by the
   auto-upload toggle, revert the toggle to off.

### Disconnect

1. Call Google's **token revocation endpoint** for the stored token (access ends
   server-side, not just locally).
2. Delete `drive-token.dat`; clear `DriveConnectedEmail`; set
   `DriveUploadEnabled = false`; state → `NotConnected`.
3. **Local recordings are never touched.** Pending queue entries are kept but
   show "waiting for connection" (they drain after a future reconnect).

## Settings dialog — new tab "Google Drive"

Lean, configuration-focused (manual upload actions live in the tray, not here):

- **Connection status line** — one of:
  - `Not connected` + **[ Connect Google Drive ]** button
  - `Connected as user@example.com` + **[ Disconnect ]** button
  - `⚠ Reconnect needed` (orange) + **[ Reconnect ]** button
- `[ ] Automatically upload the mixed file to Google Drive after each recording`
  — checkbox bound to `DriveUploadEnabled`. **Disabled (grayed) until an account
  is connected.** Toggling it on while disconnected first runs Connect.
- Static note (small, gray): *"Uploaded recordings leave this computer. Confirm
  you have permission to record and store meeting audio."*

The dialog grows to **4 tabs**: General, Audio, Mixed file, **Google Drive**.

## Tray integration

- **Menu** (added below "Open recordings folder"):
  - `Upload last recording to Drive` — enabled when connected and a Mixed file
    from the last session exists.
  - `Upload pending now` — enabled when connected and the queue is non-empty.
  - A **disabled status line** (like the existing "Idle"/"Recording for…"):
    `Google Drive: up to date` / `Uploading…` / `2 waiting to upload` /
    `Reconnect needed`.
- **Toasts** (reuse `ShowBalloon`): extend/append to the existing post-mix toast:
  - success → *"Uploaded to Google Drive ✓"*
  - offline/transient → *"Will upload to Drive when online"*
  - auth failure → *"Reconnect Google Drive — uploads paused"* (Warning icon)
- **Tray icon is unchanged** — gray = idle, red = recording. Upload state is
  **never** shown on the icon, so the safety-critical "am I recording?" signal
  stays unambiguous.

## Edge cases

| Situation | Behavior |
|---|---|
| Recording finishes while offline, auto-upload on | Enqueued; drains automatically when network returns / on next startup. |
| App closed before an upload completes | Entry persisted in `upload-queue.json`; resumes on next launch. |
| Upload succeeds but app crashes before marking done | Next drain's dedup-by-name finds the file already in the folder → marks done, no duplicate. |
| Refresh token revoked in the user's Google account | Next attempt → Auth failure → queue pauses, `Reconnect needed` shown, Pending kept. |
| OAuth app still in "testing" status | Tokens die after 7 days → manifests as Auth failure → Reconnect prompt. (Resolved by shipping a verified, production app — ADR-0001.) |
| User deletes the local Mixed file before it uploads | Entry dropped with an info toast. |
| Two recordings produce the same filename (clock collision) | Filenames include `HH-mm-ss`; practically unique. If identical, dedup-by-name would treat the second as already-uploaded — acceptable; named sessions and seconds resolution make this vanishingly rare. |
| User toggles auto-upload on, then cancels the OAuth browser | Toggle reverts to off; state `NotConnected`. |
| `drive.file` folder deleted by the user in Drive | Cached folder id 404s → uploader re-creates `SPRecorder` folder. |
| `appsettings.json` on read-only media | Config fields still load; consent flag can't persist → acknowledgement may re-show. Acceptable (warn in README to keep the folder writable). |

## Testing

### Unit tests (new)
```
tests/SPRecorder.Tests/
  UploadQueueTests.cs
    - Enqueue then persist/reload preserves entries
    - Transient failure increments attempts, stays queued, backoff grows
    - Auth failure pauses the queue and sets ReconnectNeeded
    - Missing local file → entry dropped
    - Successful upload marks entry done and stores file id
  TokenStoreTests.cs
    - Save then load round-trips the token
    - Stored bytes are not plaintext (DPAPI-encrypted); decrypt only as CurrentUser
  DriveUploaderTests.cs   (uploader logic behind an IDriveApi seam)
    - Folder resolved once and cached; re-resolved after a simulated 404
    - Dedup: pre-existing filename → marked done, no upload call
    - Exception→classification mapping (invalid_grant/401 → Auth; 5xx/timeout → Transient)
```
Network/OAuth calls are abstracted behind interfaces (`IDriveApi`,
`ITokenStore`) so the queue/uploader logic is unit-testable without hitting
Google. DPAPI tests run on Windows CI only.

### Manual smoke test
1. Settings → Google Drive tab → **Connect** → browser opens → consent → tab
   shows *Connected as you@gmail.com*.
2. Enable auto-upload → record a short meeting → stop → after the mix toast, a
   *"Uploaded to Google Drive ✓"* toast appears → confirm the `_mixed.mp3` is in
   a `SPRecorder` folder in Drive (and the raw tracks are NOT).
3. Disconnect wifi → record → confirm *"Will upload when online"* + tray line
   *"1 waiting to upload"* → reconnect wifi → file uploads automatically.
4. Record with auto-upload **off** → tray → *Upload last recording to Drive* →
   confirm it lands in Drive.
5. Revoke the app at myaccount.google.com → trigger an upload → confirm
   *Reconnect needed* state and that Pending items survive → Reconnect → they
   drain.
6. Disconnect → confirm the app's access is gone from the Google account and
   `drive-token.dat` is deleted; confirm local recordings untouched.

## Out of scope

- Uploading the raw `system`/`mic` tracks (Mixed file only).
- Central/shared-Drive collection or service-account delivery (rejected in
  ADR-0001 — each user uploads to their own Drive).
- Mirroring named-session subfolders into Drive (flat `SPRecorder` folder; the
  filename already carries the session label + timestamp).
- Configurable Drive destination folder, upload of arbitrary files, or a Drive
  file browser inside the app.
- Auto-deleting local files after upload (local copies are the archive).
- Providers other than Google Drive.
