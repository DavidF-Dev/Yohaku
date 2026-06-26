# Smoother rebuilds: skip no-op display changes, re-pin in place

**Status: implemented; live verification pending.** Addresses the "occasional 1-2s
window shuffle" on display changes. Builds on the in-place re-pin machinery already
used for config edits and auto-hide toggles (`TryReapplyInPlace`).

## Problem (recap)

`SystemEvents.DisplaySettingsChanged` fires for many events beyond monitor
add/remove (fullscreen toggles, resolution/refresh blips, monitor sleep/wake, GPU
resets). Today every one schedules a full teardown `Rebuild()`:

1. 500ms debounce, then `RemoveAll()` (work area expands, windows grow back),
2. 100ms defer, then `BuildStrips()` re-registers all strips one at a time (work
   area shrinks in steps, windows walk inward), then a 250ms reposition pass.

The log confirms every recorded display-change rebuild re-built the identical
"12 strips across 3 monitor(s)" - i.e. the monitor set never actually changed, so
the teardown was unnecessary.

## Goal

- **#1** Skip the rebuild entirely when a display change leaves the monitor set and
  geometry unchanged.
- **#2** When geometry changed but the monitor *count* is the same, re-pin in place
  (smooth) instead of tearing down. Reserve the full teardown for genuine monitor
  add/remove.

## Design: reconcile instead of always-teardown

On a rebuild trigger, re-enumerate monitors and compare to the cached set, yielding
one of three outcomes:

| Outcome | When | Action |
| --- | --- | --- |
| `Unchanged` | same count, same bounds + DPI per monitor | **skip** (no-op) [#1] |
| `GeometryChanged` | same count, some bounds/DPI differ | refresh + **in-place re-pin** [#2] |
| `TopologyChanged` | monitor count differs | full teardown `Rebuild()` [#2 fallback] |

## Entry points

- **`DisplaySettingsChanged`** (kept debounced via `_rebuildTimer`): tick calls the
  new `ReconcileDisplays()` instead of `Rebuild()`. This is where #1's skip lives.
- **Manual "Rebuild margins"**: a plain click re-applies in place (refresh geometry,
  re-pin, no skip), so it is smooth. **Shift+click** forces a full teardown rebuild
  as a recovery escape hatch.
- **Unchanged paths**: startup `Build()`, `ApplyConfig` (config edits, already
  in-place), and `OnStripPosChanged` (taskbar move / auto-hide, already smart).

## Implementation steps

1. **Pure monitor-set comparison** (in `StripGeometry`, unit-tested). Given two lists
   of `(RECT bounds, dpi)` per monitor, return `Unchanged | GeometryChanged |
   TopologyChanged`. Compare the raw DPI (uint) rather than the derived `double`
   scale to avoid float-equality concerns. Positional correspondence is fine (a
   replaced monitor reads as `GeometryChanged`, which still re-pins correctly).

2. **`AppBarStrip` bounds become refreshable.** `_monitor` is currently `readonly`;
   add `UpdateMonitorBounds(RECT bounds)` so a resolution change can update the
   strip's geometry without recreating it. (`DesiredRect` is built from `_monitor`.)

3. **`AppBarManager.ReconcileDisplays()`**: re-enumerate, compare to `_monitors`,
   branch per the table. Log which path ran.

4. **Shared "refresh + in-place" routine** used by `GeometryChanged` and the manual
   button: set `_monitors` to the fresh enumeration, call `strip.UpdateMonitorBounds`
   for each strip, then `TryReapplyInPlace()` (which already re-resolves thickness
   from fresh DPI/gap and re-pins). Fall back to `Rebuild()` if it returns false.

5. **Re-wire triggers**: `_rebuildTimer.Tick` -> `ReconcileDisplays()` (was
   `Rebuild()`); manual menu item -> re-apply-now on a plain click, or `Rebuild()`
   when `Control.ModifierKeys & Keys.Shift` is set (Shift+click).

6. **Logging for observability** (so each path is verifiable):
   - `Unchanged`: "Display change; monitors unchanged, skipping rebuild."
   - `GeometryChanged` / manual: "Margins re-applied in place."
   - `TopologyChanged`: "Monitor set changed; rebuilding." then "AppBars built ...".

7. **Tests**: unit-test the pure comparison across same / resolution-change /
   DPI-change / monitor-added / monitor-removed. (The Win32 display behavior itself
   stays integration-only.)

## Resolved decisions

- **Manual "Rebuild margins" behavior.** Plain click = in-place re-apply (smooth,
  click-to-verify, re-asserts every strip's position via `SetPosition`).
  **Shift+click = full teardown rebuild**, a recovery escape hatch for the rare case
  where a registration is genuinely corrupted (in-place would not fix it, partly
  because `SetPosition` is idempotent). The modifier is read with
  `Control.ModifierKeys & Keys.Shift` in the menu handler. Only downside is
  discoverability (a hidden modifier); add a one-line README "Troubleshooting" note.

## Edge cases

- **Transient empty enumeration** during reconfiguration (0 monitors): treat as "no
  usable data, skip" rather than tearing everything down; the next event settles it.
- **DPI change**: handled by `GeometryChanged` (fresh DPI -> rescaled thickness).
- **Monitor replaced (same count)**: `GeometryChanged` -> in-place with fresh
  handles/bounds. Fine.
- **Other appbars moving / work-area changes** that are not monitor changes still
  arrive via `ABN_POSCHANGED` -> `OnStripPosChanged`, unaffected by this work. So
  skipping on unchanged monitors is safe.
- **`TryReapplyInPlace` guards** (count mismatch, register-across-zero) already exist
  and compose with the geometry-changed path.

## Verification

- **Manual (#2 mechanism):** click **Rebuild margins**. Before: ~1-2s shuffle.
  After: instant / no visible churn (log: "re-applied in place"). This is the
  click-to-verify test.
- **Shift+click Rebuild margins:** still does the full teardown (log: "AppBars
  built ..."), confirming the recovery escape hatch.
- **#1 skip:** change the display resolution and set it back (or trigger any no-op
  display event). Expect: no window flash, log: "monitors unchanged, skipping".
- **#2 geometry:** change resolution to a *different* value. Expect: smooth
  re-settle, log: "re-applied in place", correct insets at the new resolution
  (`verify_maximize.ps1`).
- **Topology:** unplug/replug a monitor. Expect: the full teardown rebuild still runs
  (acceptable; rare).
- Display-event behavior is **live-only** (cannot be unit-tested), same limitation as
  the integration scripts.

## Risks / open issues

- Making `AppBarStrip._monitor` mutable is a small change to a previously-immutable
  field; keep the setter narrow (`UpdateMonitorBounds` only).
- The manual-button recovery tradeoff above is the main decision.
- Positional monitor matching (rather than identity matching) is a deliberate
  simplification; revisit only if odd multi-monitor reconfigurations misbehave.
