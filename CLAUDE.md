# CLAUDE.md

Guidance for Claude Code working in this repo. Future work and sequencing live in
`docs/plan.md`.

## What this is

**Yohaku** (余白) is a Windows 11 tray app that reserves a configurable margin
around every monitor so maximised/snapped windows get a gap from the screen
edges, while staying **genuinely maximised** (real `SIZE_MAXIMIZED`, correct
restore-down glyph, no injection, no maximise-button hijacking). It does this by
registering four thin **appbars** (`SHAppBarMessage`) per monitor that shrink the
work area; windows then maximise to the inset rect natively.

## Settled decisions (don't re-litigate)

1. **Rounded + inset + truly-maximised cannot coexist on Windows.** DWM suppresses
   rounded corners for the maximised *state* (not geometry-based), confirmed by MS
   docs and an MS engineer. Insetting a real maximised window via `WM_GETMINMAXINFO`
   also loses the shadow and corners. Square corners are accepted for now; rounding
   is a deferred follow-up (see `docs/plan.md`).
2. **Fake-maximise was built first, then rejected.** It un-maximised windows and
   re-placed them as rounded inset "floating" windows. That worked, but hijacking the
   maximise button gave a wrong/unfixable button glyph on other apps' windows, plus
   flash and a weird toggle. The appbar approach never touches the maximise button.
3. **Appbar approach is the chosen design.** Truly maximised, no injection; works on
   elevated/protected apps. Insight (from komorebi): change the monitor work area and
   every window maximises to the inset rect natively.
4. **The margin is global**, not per-app: it applies to all maximised/snapped
   windows and reflows the desktop work area.

## Build / test / run

```powershell
dotnet build Yohaku.slnx -c Release      # must stay 0 warnings
dotnet test  Yohaku.slnx                 # 19 xUnit tests (pure geometry + config)
.\scripts\run.ps1                        # build Release + launch (-NoBuild to skip build)
```

Integration checks need a live desktop (not headless-CI) and Yohaku running:

```powershell
.\tests\integration\measure_workarea.ps1            # print each monitor's work area
.\tests\integration\verify_maximize.ps1 -Inset 12   # assert a window is truly maximised + inset
```

## Layout

- `src/Yohaku/`: the app (WinExe, `net8.0-windows`, PerMonitorV2 via `app.manifest`)
  - `Program.cs`: tray, single-instance mutex, config hot-reload, global exception logging
  - `AppBarManager.cs`: enumerates monitors, owns strips, rebuild/reposition (debounced)
  - `AppBarStrip.cs`: one reserved strip = a hidden appbar `NativeWindow`
  - `StripGeometry.cs`: **pure** geometry (DPI scale, per-edge rects); unit-tested
  - `Config.cs`: per-edge insets, JSON load/save
  - `NativeMethods.cs`: P/Invoke (appbar, monitor enum, DPI, DWM corner [reserved])
- `tests/Yohaku.Tests/`: xUnit; internals exposed via `InternalsVisibleTo`
- `scripts/`: `run.ps1` / `run.bat` launchers
- `tools/`: icon SVG + `generate-icon.ps1` (regenerates `src/Yohaku/Yohaku.ico`)

Runtime data: `%APPDATA%\Yohaku\config.json` + `yohaku.log`.

## Gotchas (don't relearn these the hard way)

- **AppBar rebuild must be deferred.** `AppBarManager.Rebuild()` removes then
  re-creates strips on a *later* message-loop turn (one-shot timer). Doing it
  synchronously leaves the new reservations inert.
- **Don't marshal via a control without a created handle.** Background-thread work
  (e.g. `FileSystemWatcher.Changed`) marshals through the hidden always-realised
  `_syncRoot` form, never the `ContextMenuStrip`.
- **A maximised `GetWindowRect` overhangs the work area by ~8px/edge** (invisible
  resize border). Assert visible bounds via `DWMWA_EXTENDED_FRAME_BOUNDS` (attr 9).
- **Always release on exit.** Quitting must remove every appbar or the desktop work
  area stays shrunk; `OnExit` handles this. A force-kill is also safe: Windows
  reclaims the space on the next work-area recalc.
- **Repositioning loop guard.** The `_repositioning` flag stops our own `SETPOS`
  (which fires `ABN_POSCHANGED`) from cascading into an endless reposition loop.
- **Secondary-monitor appbars** work on the dev machine (3 monitors) but aren't
  guaranteed across every setup; verify if behaviour looks off.
- **Taskbar-inset presence check reads `rcWork` with our own strips absent.** It
  works because `EnumerateMonitors` runs inside `BuildStrips`, after `RemoveAll`
  (and the deferred settle), so the monitor↔work gap reflects only the taskbar.
  Reordering teardown would silently corrupt the override decision.
- **`.slnx`, not `.sln`**: modern XML solution format; `dotnet build/test Yohaku.slnx`
  work natively.

## Conventions

- Keep the build at **0 warnings**.
- `StripGeometry` and `Config` logic stay pure and unit-tested; anything needing a
  live desktop is verified by the `tests/integration` scripts.
- Geometry units: insets are logical (96-DPI) px, scaled per monitor by its DPI.

## Git

- Do not run git actions (commit, push, branch, reset, etc.) unless explicitly
  directed to. Staging, history, and remotes are managed by the user.

## Comments

- Write conservatively. Default to no comment; add one only when the WHY is non-obvious (a hidden constraint, a subtle invariant, a workaround for a specific bug).
- When a comment is warranted, keep `//` comments to a single concise line.
- Class and method `///` summaries may be multi-line.
- Describe what the target *is* / *does*; broader context belongs in external docs, not in code comments.
- Keep them self-contained and stable.
- **Comments shouldn't have dependencies. The acid test: a comment should not need to be edited unless the code immediately below it changes.**
- A reader who has never seen the rest of the codebase should be able to verify the comment against the local code alone.
- Do not refer to file paths or file names.