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

## Publishing readiness

Metadata, `LICENSE` (MIT), and an About tray item are done. Remaining:

- **Runnable artifact for end users.** Today the only path is `dotnet build`. Add a
  `dotnet publish` single-file setup (`PublishSingleFile`, self-contained or
  framework-dependent) producing a downloadable `Yohaku.exe`, and document a
  "Download & run" section in the README.
- **Version bump** when cutting the first public release (currently `0.2.0`).

## Done: "already running" feedback

A second launch signals the running instance via a named `EventWaitHandle`; the
running instance shows a tray balloon ("Yohaku is already running...") and the second
exits. The signal round-trip is verified in the log (first instance receives the ping
and calls `ShowBalloonTip`); the balloon's on-screen appearance is pending an eyeball
check. (A `HWND_BROADCAST` window-message approach was tried first but didn't reach
the hidden window; the named event is deterministic.)

## Done: run-at-login

A checkable "Start with Windows" tray item (`Startup.cs`) adds or removes a per-user
`HKCU\...\Run` entry pointing at the current executable, self-healing the path on
startup if the exe moves. Default off. Verified headlessly (default state, build);
a real sign-out/in to confirm it launches at login is still pending.

## Not yet tested

- **Monitor hot-plug**: only the identical reload-rebuild path has been exercised,
  not real add/remove of a monitor.
- **Mixed-DPI monitors**: all dev monitors share a DPI. The `newScale/oldScale`
  scaling is in code but unverified on genuinely mixed-DPI setups.
- **Auto-hide setting toggled live**: the in-place re-pin is verified via config
  reload across the zero boundary, and the OS auto-hide toggle routes through the
  same `TryReapplyInPlace`, but toggling the actual taskbar setting hasn't been
  confirmed end-to-end on the desktop.
- **Taskbar edge moved at runtime**: still triggers a full rebuild (not in-place);
  reasoned through, not exercised live.
