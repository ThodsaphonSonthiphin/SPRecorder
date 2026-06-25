---
type: daily-state
schema_version: 1
updated: '2026-06-25T19:09:00+07:00'
---

# Daily state

## What I was doing

## Next

## Log

- 2026-06-25T16:48:22+07:00 — $(cat <<'EOF'
docs: design markers feature (ADRs 0007-0014, spec, plan)

Sidecar Marker log triggered by two global hotkeys (quick-mark +
mark-with-note), Markdown/CSV format, captured during grill-then-plan.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-25T16:51:16+07:00 — feat: add marker hotkey + log-format settings to AppConfig
- 2026-06-25T16:54:43+07:00 — feat: add FileNameBuilder.BuildMarker for the marker log filename
- 2026-06-25T16:58:04+07:00 — feat: add MarkerLog (formatting + append-on-mark + Markdown finalize)
- 2026-06-25T17:02:29+07:00 — fix: harden MarkerLog finalize (doc ordering, explicit UTF-8) + add elapsed/empty-note tests
- 2026-06-25T17:05:22+07:00 — refactor: let GlobalHotkey register under a caller-supplied id
- 2026-06-25T17:09:01+07:00 — feat: capture/append markers and finalize+move the marker log in RecordingSession
- 2026-06-25T17:12:57+07:00 — feat: add modeless MarkNoteInputForm + non-recorded-monitor picker
- 2026-06-25T17:19:24+07:00 — $(cat <<'EOF'
feat: wire marker hotkeys, tray items, note window, and visual feedback into TrayApp
EOF
)
- 2026-06-25T17:23:09+07:00 — fix: commit pending marker note before call-end auto-stop
- 2026-06-25T17:27:46+07:00 — feat: add Settings Markers tab + three-hotkey distinctness validation
- 2026-06-25T17:35:32+07:00 — fix: commit marker note synchronously so it isn't lost when recording stops
- 2026-06-25T17:36:31+07:00 — $(cat <<'EOF'
docs: reconcile ADR 0014 with as-built finalize-at-Stop (title written once, after move)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-25T18:41:11+07:00 — $(cat <<'EOF'
docs: design inactive-hotkey notification (ADRs 0015-0018, spec)

Durable conflict discovery: tray badge + menu + consolidated balloon +
per-key Settings indicator, sourced from GlobalHotkey.IsRegistered.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-25T18:45:08+07:00 — $(cat <<'EOF'
docs: implementation plan for inactive-hotkey notification

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
- 2026-06-25T18:48:17+07:00 — feat: add HotkeyStatus (live registration status of the three hotkeys)
- 2026-06-25T18:51:18+07:00 — feat: add IconFactory.CreateCircleWithBadge (warning badge overlay)
- 2026-06-25T18:54:19+07:00 — feat: add HotkeyCaptureControl.SetInactiveStatus (non-probing inactive hint)
- 2026-06-25T18:56:42+07:00 — feat: SettingsForm shows per-key inactive status from live HotkeyStatus
- 2026-06-25T19:01:51+07:00 — feat: surface inactive hotkeys via tray badge, menu, consolidated balloon + Settings status
- 2026-06-25T19:05:05+07:00 — refactor: compute InactiveLabels once in RegisterHotkeys balloon
- 2026-06-25T19:09:00+07:00 — $(cat <<'EOF'
polish: drop hotkey from conflict menu label so multi-key lists read grammatically

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)
