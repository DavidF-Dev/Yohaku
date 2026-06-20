# Rounded corners — scoping & options

**Status: deferred.** Decided to keep hard corners — they look fine, and the only
viable mechanism (`SetWindowRgn`) carries real caveats (see below). This doc is
retained as the design record should it ever be revisited. For the locked-in
decisions this builds on, see `CLAUDE.md`.

## The core constraint

DWM draws Windows 11's rounded corners based on a window's **state**, not its
geometry. A **maximised** window is square by design, and the corner-preference API
(`DWMWA_WINDOW_CORNER_PREFERENCE`) is **ignored** for the maximised state. Yohaku's
windows are *genuinely* maximised (that's the whole point — real `SIZE_MAXIMIZED`,
correct restore glyph). So the OS will never round them for us; any rounding has to
be faked by clipping the window ourselves.

The appbar gap is what makes faking viable: because the window is inset, a clipped
corner exposes wallpaper instead of chopping content against the screen edge.

## Precluded by settled decisions (don't re-litigate)

- **`DWMWA_WINDOW_CORNER_PREFERENCE` (the "proper" API).** Ignored for the maximised
  state — no effect on our windows. (Decision #1.)
- **Restored-but-work-area-filling windows** (komorebi-style; DWM *would* round
  these). That's the rejected fake-maximise approach — hijacks the maximise button,
  wrong glyph, flash. (Decision #2.)
- **`WM_GETMINMAXINFO` inset of a real maximised window.** Loses the shadow and
  corners anyway. (Decision #1.)

## The one viable mechanism: `SetWindowRgn`

Clip each maximised top-level window to a rounded region
(`CreateRoundRectRgn` → `SetWindowRgn(hwnd, rgn, TRUE)`). The region's outer corners
become transparent, revealing the wallpaper in the gap.

This is the only mechanism that rounds a *truly maximised* window. Its quality and
cost caveats are the real subject of this doc:

- **Aliased corners.** Regions are 1-bit masks (no alpha), so the curve is jagged,
  not DWM's smooth anti-aliased arc. A finer region reduces but cannot remove this.
- **No drag shadow.** A region-clipped window loses the DWM drop shadow. *Open
  question:* an inset-maximised window may already have no shadow (maximised state),
  in which case there's nothing to lose — needs checking on a real window.
- **Elevated apps stay square.** UIPI blocks a non-elevated process from setting the
  region on a higher-integrity window. The appbar inset still applies (it's a global
  work-area change), so elevated apps would be **inset but square** while everything
  else is rounded — an inconsistency. Running Yohaku elevated to fix this has its own
  costs and is not recommended.
- **Cross-process region management.** We'd be calling `SetWindowRgn` on other apps'
  windows and must reliably **remove** the region on restore and on exit, or windows
  stay clipped. This is the appbar "always release on exit" discipline, again.
- **Content at the very corner is clipped** (usually title-bar/scrollbar dead space,
  but not always).

### What the full feature would need

1. **Window watcher** — `SetWinEventHook` (out-of-context, global) for maximise /
   resize / restore / foreground across all processes; filter to visible top-level
   app windows; debounce (location-change fires a lot).
2. **Apply** — on a window becoming maximised, compute the rounded region (radius
   scaled by the window's monitor DPI) and `SetWindowRgn`.
3. **Reapply** — recompute on size/DPI/monitor change.
4. **Remove on restore** — `SetWindowRgn(hwnd, NULL, TRUE)` when it un-maximises;
   track which windows we've modified.
5. **Release on exit** — strip regions from every modified window on shutdown
   (force-kill would leave them clipped — unlike appbars, the OS won't reclaim this).
6. **Config + tray toggle** — corner radius and an on/off switch.

## Considered but impractical

- **Corner overlay windows** (4 small layered windows per maximised window painting a
  smooth mask). Could anti-alias, but a layered window can only mask *to a colour* —
  it can't reveal the desktop behind the corner — so it can't match the wallpaper
  look, and it's far more complex (z-order, tracking, flicker, perf). Not worth it.

## Baseline: keep square corners

Always an option, and an honest one given the caveats above. The square corners are
already an accepted tradeoff; rounding buys polish at the cost of aliasing, a likely
shadow loss, and the elevated-app inconsistency.

## Recommendation: spike before committing

`SetWindowRgn` is the only path, but whether it's *worth shipping* hinges on things
only a real window will tell us: how bad the aliasing actually looks, whether there's
even a shadow to lose, and whether region-clipping glitches with snap previews / DWM
thumbnails. So:

1. **Spike (throwaway):** apply a rounded region to one maximised window by hand,
   eyeball corner quality, shadow, and behaviour. Cheap.
2. **Go/no-go:** if it looks good enough, build the full feature (watcher → apply →
   reapply → remove → release + config/toggle). If not, record "evaluated, kept
   square" and move on.

This avoids investing in the whole watcher/lifecycle machine before we know the
result clears the quality bar.

## Open decisions

- **Appetite:** ship rounding despite aliased corners + elevated apps staying square,
  or hold at square until/unless a spike proves the quality?
- **Spike-first or commit to the full build now?**
- **Radius** (fixed logical px, DPI-scaled?) and whether it's **configurable** + a
  **tray toggle** (default on or off?).
- **`NativeMethods`** still carries the old DWM corner-preference P/Invoke; with that
  API confirmed useless here, remove it or keep as a documented dead-end?
