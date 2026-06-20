# Yohaku — plans & sequencing

Forward-looking work. For how the app is built and the decisions already locked
in, see `CLAUDE.md`.

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
- **`verify_maximize.ps1` PASS path** — the script is generalized for any
  resolution/taskbar/DPI, but its PASS branch has only been confirmed against the
  dev machine; the FAIL path is exercised when Yohaku isn't running.
