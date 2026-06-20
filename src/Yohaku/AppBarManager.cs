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

    // Debounced rebuild (monitor/display changes) and reposition (appbar moves).
    private readonly System.Windows.Forms.Timer _rebuildTimer;
    private readonly System.Windows.Forms.Timer _repositionTimer;
    // One-shot defer between teardown and re-create: the appbar subsystem must process the ABM_REMOVE messages before we register replacements, or the new reservations stay inert.
    private readonly System.Windows.Forms.Timer _deferredBuildTimer;
    private bool _repositioning;
    private bool _disposed;

    // A docked taskbar reserves tens of px; an auto-hidden one only a ~1px sliver.
    private const int TaskbarMinReservePx = 4;

    // Registration order: top & bottom (full width) before left & right, so the system trims left/right to sit between them — a clean frame, no corner overlap.
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
    }

    public void ApplyConfig(Config cfg)
    {
        _cfg = cfg;
        Rebuild();
        Log.Info("Configuration applied; appbars rebuilt.");
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

        foreach (var mon in EnumerateMonitors())
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

        Log.Info($"AppBars built: {_strips.Count} strips across {_strips.Count / 4} monitor(s).");
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

    /// <summary>Debounced rebuild — call on monitor add/remove or display changes.</summary>
    public void ScheduleRebuild()
    {
        if (_disposed) return;
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    // ---- Reposition (appbar notifications) ----------------------------

    private void OnStripPosChanged()
    {
        if (_disposed || _repositioning) return; // ignore our own induced changes

        // A taskbar edge move or auto-hide toggle changes which edge gets the override, so rebuild; lesser appbar moves only need a reposition.
        if (GetTaskbarSignature() != _lastTaskbarSignature)
        {
            ScheduleRebuild();
            return;
        }
        _repositionTimer.Stop();
        _repositionTimer.Start();
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
        public RECT Monitor;
        public RECT Work;
        public double Scale = 1.0;
    }

    private static List<MonitorData> EnumerateMonitors()
    {
        var list = new List<MonitorData>();

        // Delegate kept in a local so it isn't collected during the (synchronous) call.
        MonitorEnumProc cb = (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data) =>
        {
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                double scale = 1.0;
                if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                    scale = dpiX / 96.0;
                list.Add(new MonitorData { Monitor = mi.rcMonitor, Work = mi.rcWork, Scale = scale });
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
        RemoveAll();
    }
}
