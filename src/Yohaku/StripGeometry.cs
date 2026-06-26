using static Yohaku.NativeMethods;

namespace Yohaku;

/// <summary>
/// Pure, side-effect-free geometry for appbar strips. Extracted from the
/// Win32-coupled classes so it can be unit-tested in isolation. Operates only on
/// plain <see cref="RECT"/> values, with no system calls.
/// </summary>
internal static class StripGeometry
{
    /// <summary>Scale a logical (96-DPI) length by a monitor DPI factor, rounded.</summary>
    public static int Scale(int logical, double dpiScale) =>
        (int)Math.Round(logical * Math.Max(0.0, dpiScale));

    /// <summary>
    /// True when the work area is inset from the monitor on <paramref name="edge"/>
    /// by more than <paramref name="minReservePx"/> physical pixels, i.e. a taskbar
    /// genuinely reserves space there rather than leaving only an auto-hide sliver.
    /// </summary>
    public static bool EdgeReservesSpace(uint edge, RECT monitor, RECT work, int minReservePx) =>
        EdgeGap(edge, monitor, work) > minReservePx;

    /// <summary>
    /// The logical inset to reserve on <paramref name="edge"/>: the taskbar override
    /// when this edge holds a space-reserving taskbar and an override is set,
    /// otherwise the edge's own inset.
    /// </summary>
    public static int PickInset(uint edge, uint taskbarEdge, bool taskbarReservesHere,
                                int edgeInset, int? taskbarInset) =>
        edge == taskbarEdge && taskbarReservesHere && taskbarInset.HasValue
            ? taskbarInset.Value
            : edgeInset;

    /// <summary>
    /// How far the work area is inset from the monitor on <paramref name="edge"/>, in
    /// physical pixels: the space reserved there by the taskbar and any appbars.
    /// </summary>
    public static int EdgeGap(uint edge, RECT monitor, RECT work) => edge switch
    {
        ABE_TOP => work.Top - monitor.Top,
        ABE_BOTTOM => monitor.Bottom - work.Bottom,
        ABE_LEFT => work.Left - monitor.Left,
        ABE_RIGHT => monitor.Right - work.Right,
        _ => 0,
    };

    /// <summary>
    /// Initial desired reservation rectangle for an edge: a full-edge strip of the
    /// given thickness flush against that edge of the monitor.
    /// </summary>
    public static RECT DesiredRect(uint edge, RECT monitor, int thickness) => edge switch
    {
        ABE_TOP => new RECT { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Top + thickness },
        ABE_BOTTOM => new RECT { Left = monitor.Left, Top = monitor.Bottom - thickness, Right = monitor.Right, Bottom = monitor.Bottom },
        ABE_LEFT => new RECT { Left = monitor.Left, Top = monitor.Top, Right = monitor.Left + thickness, Bottom = monitor.Bottom },
        ABE_RIGHT => new RECT { Left = monitor.Right - thickness, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom },
        _ => monitor,
    };

    /// <summary>
    /// Re-pin our exact thickness against the outer edge after the appbar system
    /// adjusts the rectangle (ABM_QUERYPOS) in the direction of reservation.
    /// </summary>
    public static RECT PinThickness(uint edge, RECT rc, int thickness)
    {
        switch (edge)
        {
            case ABE_TOP: rc.Bottom = rc.Top + thickness; break;
            case ABE_BOTTOM: rc.Top = rc.Bottom - thickness; break;
            case ABE_LEFT: rc.Right = rc.Left + thickness; break;
            case ABE_RIGHT: rc.Left = rc.Right - thickness; break;
        }
        return rc;
    }

    /// <summary>Outcome of comparing two monitor enumerations.</summary>
    public enum DisplayChange { Unchanged, GeometryChanged, TopologyChanged }

    /// <summary>A monitor's full bounds and effective DPI, for detecting display changes.</summary>
    public readonly record struct MonitorSnapshot(RECT Bounds, uint Dpi);

    /// <summary>
    /// Classify how a monitor set changed: nothing, geometry only (same count, different
    /// bounds/DPI), or topology (a monitor was added or removed).
    /// </summary>
    public static DisplayChange Compare(IReadOnlyList<MonitorSnapshot> before, IReadOnlyList<MonitorSnapshot> after)
    {
        if (before.Count != after.Count) return DisplayChange.TopologyChanged;
        bool changed = false;
        for (int i = 0; i < before.Count; i++)
        {
            RECT a = before[i].Bounds, b = after[i].Bounds;
            if (a.Left != b.Left || a.Top != b.Top || a.Right != b.Right || a.Bottom != b.Bottom
                || before[i].Dpi != after[i].Dpi)
                changed = true;
        }
        return changed ? DisplayChange.GeometryChanged : DisplayChange.Unchanged;
    }
}
