# Yohaku — plans & sequencing

Forward-looking work. For how the app is built and the decisions already locked
in, see `CLAUDE.md`.

## Done: taskbar-side inset

A taskbar-aware inset override (`Config.TaskbarInset`) — a separate inset on
whichever edge actually holds the taskbar, applied only where it reserves space
(auto-hide falls back to the normal inset). Phases 1 and 2 implemented and
unit-tested; runtime re-resolves on taskbar edge move / auto-hide toggle. Verified
live: `verify_maximize.ps1 -Inset 12 -TaskbarInset 8` PASSes on a Top-edge taskbar.
Design and breakdown in `taskbar-inset-plan.md`.

## Next feature: rounded corners

The agreed next feature. Now viable via `SetWindowRgn` because the appbar gap
guarantees space around the window — a clipped corner reveals wallpaper instead of
cutting content.

- Apply a rounded region to maximised top-level windows: watch for maximise with
  `SetWinEventHook`, reapply on size change, and **remove the region on restore**.
- Caveats: aliased corners (not the smooth DWM curve) and loss of the DWM drop
  shadow.
- `NativeMethods` still carries the DWM corner P/Invoke from the old fake-maximise
  approach; `SetWindowRgn` would be new.

## Publishing readiness

Metadata, `LICENSE` (MIT), and an About tray item are done. Remaining:

- **Runnable artifact for end users.** Today the only path is `dotnet build`. Add a
  `dotnet publish` single-file setup (`PublishSingleFile`, self-contained or
  framework-dependent) producing a downloadable `Yohaku.exe`, and document a
  "Download & run" section in the README.
- **Version bump** when cutting the first public release (currently `0.2.0`).

## Quality-of-life

- **Run-at-login toggle.** Not implemented; README documents the manual
  `shell:startup` shortcut. Could add a tray toggle that manages the shortcut (or a
  `HKCU\...\Run` entry).
- **"Already running" feedback.** The single-instance guard currently exits the
  second instance silently; could surface the existing tray icon or a balloon.

## Not yet tested

- **Monitor hot-plug** — only the identical reload-rebuild path has been exercised,
  not real add/remove of a monitor.
- **Mixed-DPI monitors** — all dev monitors share a DPI. The `newScale/oldScale`
  scaling is in code but unverified on genuinely mixed-DPI setups.
- **Taskbar moved / auto-hide toggled at runtime** — Phase 2 rebuilds on a changed
  taskbar signature, but this has only been reasoned through, not exercised live.
