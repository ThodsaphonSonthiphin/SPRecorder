# Upload to each user's own Google Drive via an OAuth installed-app flow

We deliver the Mixed file to **each user's own Google Drive** using the OAuth 2.0
installed-app flow with the least-privilege `drive.file` scope, the official
`Google.Apis.Drive.v3` / `Google.Apis.Auth` libraries, and a single OAuth client
baked into the distributed `.exe`. We chose this over a central/shared Drive
because recordings belong to each user, even though it forces a one-time
per-user browser consent.

## Status

accepted

## Considered options

- **Service account writing to one central Shared Drive** — would mean zero user
  setup (no consent screen, no Google account needed), which fits the
  "non-technical users, no install" goal better. Rejected because recordings are
  meant to land in each user's *own* Drive, not be collected centrally.
- **OAuth app left in "testing" publishing status** — no Google review, fastest
  to ship. Rejected because refresh tokens issued by a testing-mode app expire
  after 7 days, silently breaking uploads for every user after a week.

## Consequences

- We must take the OAuth app to **production and complete Google verification**
  for the `drive.file` scope, otherwise users hit the 7-day token expiry, the
  100-user cap, and the "unverified app" warning. `drive.file` is the lightest
  sensitive scope and avoids the heavy restricted-scope security assessment.
- This **breaks the prior "no new NuGet packages" stance** (see the Settings UI
  spec): Google's client libraries — and `System.Security.Cryptography.ProtectedData`
  for token encryption — are now dependencies. They bundle into the existing
  self-contained single-file exe, so "no install / no admin" is preserved.
- Each user's refresh token is a long-lived credential. It is stored in
  `%AppData%\SPRecorder`, DPAPI-encrypted (`CurrentUser`) — never next to the
  portable exe, which may live on a shared or read-only location.
- Uploads are not best-effort: a durable retry queue persists Pending uploads
  and drains them in the background, because users record on flaky networks and
  are non-technical.
