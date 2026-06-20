# Yohaku: plans & sequencing

Forward-looking work. For how the app is built and the decisions already locked
in, see `CLAUDE.md`.

## Done: taskbar-side inset

A taskbar-aware inset override (`Config.TaskbarInset`), a separate inset on
whichever edge actually holds the taskbar, applied only where it reserves space
(auto-hide falls back to the normal inset). Phases 1 and 2 implemented and
unit-tested; runtime re-resolves on taskbar edge move / auto-hide toggle. Auto-hide
toggles and config edits re-pin strips **in place** (registering/unregistering
across a zero inset) rather than tearing down, so they no longer flash. Verified
live: `verify_maximize.ps1` PASSes on a Top-edge taskbar, and config reloads across
the zero boundary log "applied in place" with correct geometry. Design and breakdown
in `taskbar-inset-plan.md`.

## Deferred: rounded corners

Deferred by decision: hard corners look fine, and the only viable mechanism
(`SetWindowRgn`) carries real caveats (aliased corners, likely no shadow, elevated
apps stay square). Not worth the cost for now. Full option survey and a spike-first
plan kept in `rounded-corners-plan.md` if this is ever revisited.

## Done: publishing setup

Metadata, `LICENSE` (MIT), About tray item, version `1.0.0`, and `CHANGELOG.md` are in
place. `scripts/publish.ps1` produces a self-contained, single-file
`dist/Yohaku-<version>.exe` (unsigned, with a published SHA-256); `scripts/release.ps1`
cuts the GitHub release (guard rails, CHANGELOG notes, confirm prompt). The README has
a "Download & run" section. Remaining: run `release.ps1` to cut 1.0.0 once the last
live check (run-at-login) passes.

## Done: "already running" feedback

A second launch signals the running instance via a named `EventWaitHandle`; the
running instance shows a tray balloon ("Yohaku is already running...") and the second
exits. Verified live: a second launch shows the balloon and exits, leaving one
instance running. (A `HWND_BROADCAST` window-message approach was tried first but
didn't reach the hidden window; the named event is deterministic.)

## Done: run-at-login

A checkable "Start with Windows" tray item (`Startup.cs`) adds or removes a per-user
`HKCU\...\Run` entry pointing at the current executable, self-healing the path on
startup if the exe moves. Default off. A real sign-out/in to confirm it relaunches at
login is the one remaining live check (see "Not yet tested").

## Not yet tested

Verified live so far: core margin + `TaskbarInset`, the auto-hide toggle (smooth, no
flash), the "already running" balloon, clean exit, and multi-monitor. Still open:

- **Run-at-login across a real restart** (the last release-blocking check). Implemented
  and headlessly verified; a sign-out/in to confirm it relaunches is pending.
- **Monitor hot-plug**: only the identical reload-rebuild path has been exercised,
  not real add/remove of a monitor.
- **Mixed-DPI monitors**: all dev monitors share a DPI. The `newScale/oldScale`
  scaling is in code but unverified on genuinely mixed-DPI setups.
- **Taskbar edge moved at runtime**: still triggers a full rebuild (not in-place);
  reasoned through, not exercised live.
