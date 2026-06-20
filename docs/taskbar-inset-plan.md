# Taskbar-side inset — implementation plan

**Status: implemented (Phases 1 & 2).** Unit-tested; live integration PASS path
still to be run. Kept as the design record.

A taskbar-aware inset override: wherever the taskbar **actually reserves space**,
use a separate inset value instead of that edge's normal per-edge inset.

## Resolved decisions

- **Role-based, not edge-based** (Option B). The override binds to the taskbar's
  location at runtime, not a fixed geometric edge — so it follows the taskbar
  across edges and is correct per-monitor.
- **Additive semantics.** The override is our inset contribution on the taskbar
  edge; the total gap there is `taskbar thickness + TaskbarInset`. (Same model as
  the existing per-edge insets.)
- **Auto-hide policy: "only when it reserves space."** An edge counts as having a
  taskbar only if its work area is reserved by more than a thin sliver. Auto-hidden
  taskbars (which reserve ~1px) and monitors that don't show the taskbar both fall
  back to the normal per-edge inset.

## Design summary

At build time, for each monitor and each edge, the per-edge thickness becomes:

```
useOverride = (edge == taskbarEdge) && taskbarReservesSpaceHere && TaskbarInset.HasValue
inset       = useOverride ? TaskbarInset.Value : InsetFor(edge)   // logical px
thickness   = Scale(inset, monitor.DpiScale)                      // physical px
```

- `taskbarEdge` comes from `SHAppBarMessage(ABM_GETTASKBARPOS)` (the system
  taskbar's edge; uniform across monitors on Windows 11).
- `taskbarReservesSpaceHere` comes from this monitor's `rcMonitor` vs `rcWork`
  gap on `taskbarEdge` — which is exactly the "reserves space" policy.

This reads cleanly because `rcWork` at build time already excludes our own strips
(see the ordering note below), so the gap reflects only the taskbar (and any
foreign appbars) on that edge.

## Phase 1 — core feature

### 1. `Config.cs`

Add one nullable field:

```csharp
/// <summary>Inset for the edge that holds the taskbar, in logical (96-DPI)
/// pixels. Null means that edge uses its normal per-edge inset.</summary>
public int? TaskbarInset { get; set; }
```

- Backward-compatible: `JsonOpts` already has `WhenWritingNull`, so `null` is
  omitted from the file and existing configs deserialize unchanged.
- No new validation needed — negative thicknesses are already clamped to ≥0 where
  the strip is created.

### 2. `NativeMethods.cs`

Add the one missing message constant:

```csharp
public const uint ABM_GETTASKBARPOS = 0x00000005;
```

`ABM_GETTASKBARPOS` fills `APPBARDATA.uEdge` (an `ABE_*` value) and `.rc`. We read
`uEdge`. Fallback if `uEdge` ever proves unreliable: derive the edge from `.rc`
(its thin dimension + which side of the primary monitor it hugs).

### 3. `StripGeometry.cs` — two pure, unit-tested helpers

Keep all the decision logic pure (no Win32 calls, no `Config` dependency), in line
with the rest of this class:

```csharp
// True if `work` is inset from `monitor` on `edge` by more than `minReservePx`
// physical pixels (i.e. the taskbar genuinely reserves space there, not an
// auto-hide sliver).
public static bool EdgeReservesSpace(uint edge, RECT monitor, RECT work, int minReservePx);

// Pick the logical inset for an edge: the taskbar override when this edge holds a
// space-reserving taskbar and an override is set, otherwise the edge's own inset.
public static int PickInset(uint edge, uint taskbarEdge, bool taskbarReservesHere,
                            int edgeInset, int? taskbarInset);
```

`EdgeReservesSpace` gap per edge: `TOP = work.Top - monitor.Top`,
`BOTTOM = monitor.Bottom - work.Bottom`, `LEFT = work.Left - monitor.Left`,
`RIGHT = monitor.Right - work.Right`.

Threshold: a small physical constant (e.g. `TaskbarMinReservePx = 4`). A docked
taskbar reserves tens of px; an auto-hide one ~1px — so any threshold in between
works, and 4px is safely clear of the sliver at all DPIs.

### 4. `AppBarManager.cs` — wiring

- Add a Win32 helper:

  ```csharp
  private static uint? GetTaskbarEdge(); // ABM_GETTASKBARPOS; null if no taskbar / query fails
  ```

- In `BuildStrips`, query the taskbar edge once, then resolve each edge through the
  pure helpers. Replace the four hard-coded `AddStrip` lines with a loop over the
  edges **in the existing order** `[TOP, BOTTOM, LEFT, RIGHT]` (top/bottom full
  width must still register before left/right):

  ```csharp
  uint? taskbarEdge = GetTaskbarEdge();
  foreach (var mon in EnumerateMonitors())
  {
      foreach (var edge in new[] { ABE_TOP, ABE_BOTTOM, ABE_LEFT, ABE_RIGHT })
      {
          bool reserves = taskbarEdge is uint te && te == edge
              && StripGeometry.EdgeReservesSpace(edge, mon.Monitor, mon.Work, TaskbarMinReservePx);
          int inset = StripGeometry.PickInset(edge, taskbarEdge ?? uint.MaxValue, reserves,
                                              InsetFor(edge), _cfg.TaskbarInset);
          AddStrip(edge, mon.Monitor, StripGeometry.Scale(inset, mon.Scale));
      }
  }
  ```

- Add a private `InsetFor(uint edge)` mapping `ABE_*` → `_cfg.InsetTop/Bottom/Left/Right`,
  kept here so `Config` stays free of Win32 constants.
- If `GetTaskbarEdge()` returns null, no edge gets the override — every edge uses
  its normal inset (safe fallback, today's behaviour).

Ordering note (load-bearing): `EnumerateMonitors` runs inside `BuildStrips`, which
runs after `RemoveAll` (and, on rebuild, after the deferred turn that lets the
appbar subsystem settle the removals). So `rcWork` here excludes our own strips —
the gap measures only the taskbar. This must stay true; if strip teardown is ever
reordered, the presence check breaks.

### 5. Tests

- `ConfigTests`: `TaskbarInset` defaults to null; round-trips when set; omitted JSON
  ⇒ null; explicit value read.
- `StripGeometryTests`:
  - `EdgeReservesSpace`: docked taskbar (large gap) ⇒ true; auto-hide sliver
    (≤ threshold) ⇒ false; no reservation (gap 0) ⇒ false; all four edge
    orientations; a secondary monitor with non-zero origin.
  - `PickInset`: taskbar edge + reserves + override set ⇒ override; taskbar edge +
    reserves + override null ⇒ edge inset; non-taskbar edge ⇒ edge inset; taskbar
    edge but not reserving (auto-hide) ⇒ edge inset.

### 6. Docs

- `README.md`: document `TaskbarInset` in the Configuration section (nullable;
  applies on the taskbar edge only when it reserves space).
- This file: mark Phase 1 done when it lands.

## Phase 2 — runtime robustness (implemented)

Phase 1 re-resolves on every build/rebuild (startup, display change, config reload,
manual "Rebuild appbars"). It does **not** re-resolve when the taskbar moves edges
or toggles auto-hide mid-session without a display change, because the reposition
path reuses each strip's existing thickness.

Fix: cache a lightweight taskbar signature `(edge, autoHide)` from the last build
(`autoHide` via `ABM_GETSTATE`, which is strip-independent — unlike the per-monitor
work-area gap, which our own active strips would skew). In `OnStripPosChanged`,
recompute it:

- **Unchanged** → reposition as before (foreign appbar moved).
- **Same edge, auto-hide toggled** → debounced `TryReapplyInPlace` (below).
- **Edge moved** → `ScheduleRebuild()` (the strip layout changes).

### In-place re-pin (`TryReapplyInPlace`) — avoids the teardown flash

A full `Rebuild()` (`RemoveAll` → defer → `BuildStrips`) momentarily drops every
reservation, so the work area jumps to full and maximised windows bounce out and
back — visible on each auto-hide toggle and config edit. When the strip *set* is
unchanged, only thicknesses change, so we re-pin in place instead:

1. Two-pass for safety: compute every target thickness (bailing to a rebuild only
   on topology drift or a failed `GetMonitorInfo`), then apply.
2. The presence check needs the taskbar-only gap, but our strips are now active, so
   recover it by subtracting each strip's own `Thickness` from the current gap
   (`StripGeometry.EdgeGap`) — no teardown needed to measure cleanly.
3. `AppBarStrip.ApplyThickness` (de)registers a strip as its thickness crosses zero
   (so a `TaskbarInset` of 0 toggling on/off is smooth too), and `SetPosition` is
   idempotent, so unchanged edges never move.

`ApplyConfig` routes through the same path, so config edits are flash-free as well.
Full teardown `Rebuild()` remains for topology changes (monitor add/remove) and as
the fallback.

## Edge cases & known limitations

- **Foreign appbar on the taskbar edge of a monitor that doesn't show the taskbar.**
  The gap check would see reserved space and apply the override. Rare; acceptable.
  (`ABM_GETTASKBARPOS` already constrains this to the taskbar's edge.)
- **Auto-hide + a foreign appbar on the same edge.** Gap exceeds the threshold, so
  we treat the edge as reserving space even though the taskbar itself is hidden.
  Consistent with the "reserves space" policy; note it.
- **Per-monitor taskbar edges.** Windows 11 keeps the taskbar on one edge globally,
  so a single `taskbarEdge` is correct. If a future setup allowed different edges
  per monitor, detection would need to go per-monitor (largest reserved gap), with
  the foreign-appbar ambiguity that implies.

## Integration verification

`tests/integration/verify_maximize.ps1` currently assumes a uniform inset (asserts
the smallest monitor↔work-area gap equals the configured inset). With a smaller
override on the taskbar edge, that assumption breaks. Update it to: read the
taskbar edge, expect `TaskbarInset` on that edge and `InsetFor(edge)` elsewhere,
and assert per-edge rather than against a single value.

## Suggested commit sequence

1. `Config.TaskbarInset` + its tests.
2. `ABM_GETTASKBARPOS` constant.
3. `StripGeometry.EdgeReservesSpace` + `PickInset` + their tests.
4. `AppBarManager` wiring (`GetTaskbarEdge`, `InsetFor`, edge loop).
5. README + integration-script update.
6. (Later) Phase 2 rebuild-on-taskbar-change.
