using System.Runtime.InteropServices;
using System.Windows.Forms;
using static Yohaku.NativeMethods;

namespace Yohaku;

/// <summary>
/// Owns the appbar strips across all monitors. Reserves a per-edge margin on
/// every monitor so maximised windows inset automatically. Rebuilds on monitor
/// add/remove and repositions when the taskbar/other appbars move.
/// </summary>
public sealed class AppBarManager : IDisposable
{
    private Config _cfg;
    private readonly List<AppBarStrip> _strips = new();

    // Debounced rebuild (taskbar edge move) and reposition (appbar moves).
    private readonly System.Windows.Forms.Timer _rebuildTimer;
    private readonly System.Windows.Forms.Timer _repositionTimer;
    // One-shot defer between teardown and re-create: the appbar subsystem must process the ABM_REMOVE messages before we register replacements, or the new reservations stay inert.
    private readonly System.Windows.Forms.Timer _deferredBuildTimer;
    // Debounced in-place re-pin (taskbar auto-hide toggle / config edit); avoids the teardown flash of a full rebuild.
    private readonly System.Windows.Forms.Timer _reapplyTimer;
    // Debounced reconcile of a display change: skip if monitors are unchanged, re-pin in place if only geometry changed, rebuild only on add/remove.
    private readonly System.Windows.Forms.Timer _reconcileTimer;
    private bool _repositioning;
    private bool _disposed;

    // Monitors backing the current strips, in the same order; _strips holds EdgeOrder.Length entries per monitor.
    private List<MonitorData> _monitors = new();

    // A docked taskbar reserves tens of px; an auto-hidden one only a ~1px sliver.
    private const int TaskbarMinReservePx = 4;

    // Registration order: top & bottom (full width) before left & right, so the system trims left/right to sit between them, forming a clean frame with no corner overlap.
    private static readonly uint[] EdgeOrder = { ABE_TOP, ABE_BOTTOM, ABE_LEFT, ABE_RIGHT };

    // Taskbar edge + auto-hide state at the last build; a change re-resolves the override.
    private (uint? edge, bool autoHide) _lastTaskbarSignature;

    public AppBarManager(Config cfg)
    {
        _cfg = cfg;
        _rebuildTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _rebuildTimer.Tick += (_, _) => { _rebuildTimer.Stop(); Rebuild(); };
        _repositionTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _repositionTimer.Tick += (_, _) => { _repositionTimer.Stop(); RepositionAll(); };
        _deferredBuildTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _deferredBuildTimer.Tick += (_, _) => { _deferredBuildTimer.Stop(); if (!_disposed) BuildStrips(); };
        // Short settle so a burst of taskbar notifications collapses into one re-pin.
        _reapplyTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _reapplyTimer.Tick += (_, _) => { _reapplyTimer.Stop(); if (!_disposed && !TryReapplyInPlace()) Rebuild(); };
        _reconcileTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _reconcileTimer.Tick += (_, _) => { _reconcileTimer.Stop(); ReconcileDisplays(); };
    }

    public void ApplyConfig(Config cfg)
    {
        _cfg = cfg;
        // Only thicknesses change on a config edit (same monitors/edges), so re-pin in place; fall back to a full rebuild if that isn't possible.
        if (TryReapplyInPlace())
            Log.Info("Configuration applied in place.");
        else
            Rebuild();
    }

    // ---- Build / teardown ---------------------------------------------

    /// <summary>Synchronous build for first startup (no existing strips to settle).</summary>
    public void Build()
    {
        RemoveAll();
        BuildStrips();
    }

    private void BuildStrips()
    {
        var taskbar = GetTaskbarSignature();
        _lastTaskbarSignature = taskbar;
        uint taskbarEdge = taskbar.edge ?? uint.MaxValue;

        _monitors = EnumerateMonitors();
        foreach (var mon in _monitors)
        {
            foreach (var edge in EdgeOrder)
            {
                // Use the override only on the taskbar's edge, and only where the taskbar actually reserves space (so auto-hide falls back to the normal inset).
                bool reserves = edge == taskbarEdge
                    && StripGeometry.EdgeReservesSpace(edge, mon.Monitor, mon.Work, TaskbarMinReservePx);
                int inset = StripGeometry.PickInset(edge, taskbarEdge, reserves, InsetFor(edge), _cfg.TaskbarInset);
                // Scale logical px by this monitor's DPI so the gap is consistent across mixed-DPI monitors.
                AddStrip(edge, mon.Monitor, StripGeometry.Scale(inset, mon.Scale));
            }
        }

        Log.Info($"AppBars built: {_strips.Count} strips across {_strips.Count / EdgeOrder.Length} monitor(s).");
    }

    private int InsetFor(uint edge) => edge switch
    {
        ABE_TOP => _cfg.InsetTop,
        ABE_BOTTOM => _cfg.InsetBottom,
        ABE_LEFT => _cfg.InsetLeft,
        ABE_RIGHT => _cfg.InsetRight,
        _ => 0,
    };

    // The system taskbar's edge (ABE_*, null if absent) and auto-hide state.
    private static (uint? edge, bool autoHide) GetTaskbarSignature()
    {
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        uint? edge = SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) != UIntPtr.Zero ? abd.uEdge : null;
        bool autoHide = (SHAppBarMessage(ABM_GETSTATE, ref abd).ToUInt64() & ABS_AUTOHIDE) != 0;
        return (edge, autoHide);
    }

    public void RemoveAll()
    {
        foreach (var strip in _strips)
        {
            try { strip.Remove(); }
            catch (Exception ex) { Log.Warn($"Failed to remove appbar strip: {ex.Message}"); }
        }
        _strips.Clear();
    }

    /// <summary>
    /// Tear down all strips, then re-create them on the next message-loop turn.
    /// The deferral is essential: registering new appbars in the same synchronous
    /// call that removed the old ones leaves the new reservations inert.
    /// </summary>
    public void Rebuild()
    {
        if (_disposed) return;
        RemoveAll();
        _deferredBuildTimer.Stop();
        _deferredBuildTimer.Start();
    }

    /// <summary>Debounced full rebuild; call when the taskbar edge moves.</summary>
    public void ScheduleRebuild()
    {
        if (_disposed) return;
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    /// <summary>Debounced reconcile; call on a display change to decide skip / in-place / rebuild.</summary>
    public void ScheduleReconcile()
    {
        if (_disposed) return;
        _reconcileTimer.Stop();
        _reconcileTimer.Start();
    }

    /// <summary>Re-apply the margins in place now (manual "Rebuild margins"); falls back to a full rebuild if needed.</summary>
    public void ReapplyMargins()
    {
        if (_disposed) return;
        ReapplyWithGeometry(EnumerateMonitors());
    }

    // Compare the live monitor set to the cached one: skip if unchanged, re-pin in place if only geometry changed, rebuild on add/remove.
    private void ReconcileDisplays()
    {
        if (_disposed) return;

        var fresh = EnumerateMonitors();
        if (fresh.Count == 0)
        {
            Log.Info("Display change; no monitors enumerated, skipping.");
            return;
        }

        switch (StripGeometry.Compare(Snapshot(_monitors), Snapshot(fresh)))
        {
            case StripGeometry.DisplayChange.Unchanged:
                Log.Info("Display change; monitors unchanged, skipping rebuild.");
                break;
            case StripGeometry.DisplayChange.GeometryChanged:
                Log.Info("Display geometry changed; re-applying in place.");
                ReapplyWithGeometry(fresh);
                break;
            default:
                Log.Info("Monitor set changed; rebuilding.");
                Rebuild();
                break;
        }
    }

    // Refresh cached geometry and re-pin in place when the monitor count is unchanged; otherwise a full rebuild.
    private void ReapplyWithGeometry(List<MonitorData> fresh)
    {
        if (_strips.Count == fresh.Count * EdgeOrder.Length)
        {
            _monitors = fresh;
            for (int i = 0; i < fresh.Count; i++)
                for (int e = 0; e < EdgeOrder.Length; e++)
                    _strips[i * EdgeOrder.Length + e].UpdateMonitorBounds(fresh[i].Monitor);

            if (TryReapplyInPlace()) return;
        }
        Rebuild();
    }

    private static List<StripGeometry.MonitorSnapshot> Snapshot(List<MonitorData> monitors)
    {
        var snap = new List<StripGeometry.MonitorSnapshot>(monitors.Count);
        foreach (var m in monitors) snap.Add(new StripGeometry.MonitorSnapshot(m.Monitor, m.Dpi));
        return snap;
    }

    // ---- Reposition (appbar notifications) ----------------------------

    private void OnStripPosChanged()
    {
        if (_disposed || _repositioning) return; // ignore our own induced changes

        var sig = GetTaskbarSignature();
        if (sig == _lastTaskbarSignature)
        {
            _repositionTimer.Stop();
            _repositionTimer.Start();
            return;
        }
        // Same edge, different auto-hide state: only the taskbar-edge inset flips, so re-pin in place (no teardown flash). An edge move changes the layout, so rebuild.
        if (sig.edge == _lastTaskbarSignature.edge)
        {
            _reapplyTimer.Stop();
            _reapplyTimer.Start();
        }
        else
        {
            ScheduleRebuild();
        }
    }

    /// <summary>
    /// Re-resolve and re-pin each strip's thickness in place (no teardown) when the
    /// strip set is unchanged (config edit, or taskbar auto-hide toggle). Returns false
    /// if a full rebuild is needed instead (topology drift, or a strip would have to
    /// register/unregister), leaving all strips untouched.
    /// </summary>
    private bool TryReapplyInPlace()
    {
        if (_disposed || _strips.Count != _monitors.Count * EdgeOrder.Length) return false;

        var sig = GetTaskbarSignature();
        uint taskbarEdge = sig.edge ?? uint.MaxValue;

        // Pass 1: compute every target thickness and validate; mutate nothing yet.
        var targets = new int[_strips.Count];
        for (int i = 0; i < _monitors.Count; i++)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(_monitors[i].Handle, ref mi)) return false;

            for (int e = 0; e < EdgeOrder.Length; e++)
            {
                int idx = i * EdgeOrder.Length + e;
                var strip = _strips[idx];
                uint edge = EdgeOrder[e];
                // Back our own reservation out of the current gap to recover the taskbar-only gap (our strips are active here, so the gap already includes them).
                int taskbarGap = StripGeometry.EdgeGap(edge, mi.rcMonitor, mi.rcWork) - strip.Thickness;
                bool reserves = edge == taskbarEdge && taskbarGap > TaskbarMinReservePx;
                int inset = StripGeometry.PickInset(edge, taskbarEdge, reserves, InsetFor(edge), _cfg.TaskbarInset);
                targets[idx] = StripGeometry.Scale(inset, _monitors[i].Scale);
            }
        }

        // Pass 2: apply. ApplyThickness (de)registers across zero and SetPosition is idempotent, so unchanged edges don't move.
        _repositioning = true;
        try
        {
            for (int idx = 0; idx < _strips.Count; idx++)
                _strips[idx].ApplyThickness(targets[idx]);
        }
        finally { _repositioning = false; }

        _lastTaskbarSignature = sig;
        Log.Info("AppBars re-applied in place.");
        return true;
    }

    private void RepositionAll()
    {
        if (_disposed) return;
        _repositioning = true;
        try
        {
            foreach (var strip in _strips) strip.SetPosition();
        }
        finally
        {
            _repositioning = false;
        }
    }

    // ---- Helpers -------------------------------------------------------

    private void AddStrip(uint edge, RECT monitorBounds, int thickness)
    {
        var strip = new AppBarStrip(edge, monitorBounds, thickness, OnStripPosChanged);
        strip.Register();
        _strips.Add(strip);
    }

    private sealed class MonitorData
    {
        public IntPtr Handle;
        public RECT Monitor;
        public RECT Work;
        public double Scale = 1.0;
        public uint Dpi = 96;
    }

    private static List<MonitorData> EnumerateMonitors()
    {
        var list = new List<MonitorData>();

        // Delegate kept in a local so it isn't collected during the (synchronous) call.
        MonitorEnumProc cb = (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                uint dpi = 96;
                if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                    dpi = dpiX;
                list.Add(new MonitorData { Handle = hMon, Monitor = mi.rcMonitor, Work = mi.rcWork, Scale = dpi / 96.0, Dpi = dpi });
            }
            return true;
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        return list;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rebuildTimer.Dispose();
        _repositionTimer.Dispose();
        _deferredBuildTimer.Dispose();
        _reapplyTimer.Dispose();
        _reconcileTimer.Dispose();
        RemoveAll();
    }
}
