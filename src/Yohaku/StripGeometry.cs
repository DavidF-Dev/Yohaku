using static Yohaku.NativeMethods;

namespace Yohaku;

/// <summary>
/// Pure, side-effect-free geometry for appbar strips. Extracted from the
/// Win32-coupled classes so it can be unit-tested in isolation. Operates only on
/// plain <see cref="RECT"/> values — no system calls.
/// </summary>
internal static class StripGeometry
{
    /// <summary>Scale a logical (96-DPI) length by a monitor DPI factor, rounded.</summary>
    public static int Scale(int logical, double dpiScale) =>
        (int)Math.Round(logical * Math.Max(0.0, dpiScale));

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
}
