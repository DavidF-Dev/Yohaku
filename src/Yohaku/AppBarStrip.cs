using System.Runtime.InteropServices;
using System.Windows.Forms;
using static Yohaku.NativeMethods;

namespace Yohaku;

/// <summary>
/// A single reserved margin strip along one edge of one monitor, backed by a
/// hidden top-level window registered as an application desktop toolbar (appbar).
/// The reserved rectangle shrinks the monitor work area, which is what makes
/// maximised windows inset; the strip itself draws nothing (the gap shows the
/// desktop wallpaper).
/// </summary>
internal sealed class AppBarStrip : NativeWindow
{
    // Callback message the appbar system uses to notify us (ABN_* in wParam).
    public const int WM_APPBAR = 0x8000 + 1; // WM_APP + 1

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly uint _edge;          // ABE_*
    private readonly RECT _monitor;       // full monitor bounds (physical px)
    private int _thickness;               // reserved thickness for this edge (physical px)
    private readonly Action _onPosChanged;

    private bool _registered;

    public RECT ReservedRect { get; private set; }
    public int Thickness => _thickness;

    /// <summary>
    /// Re-resolve this strip to a new thickness in place, (de)registering as the
    /// thickness crosses zero, then reposition. The window handle is retained either
    /// way so the strip can re-reserve later without being recreated.
    /// </summary>
    public void ApplyThickness(int thickness)
    {
        _thickness = Math.Max(0, thickness);

        if (_thickness == 0)
        {
            if (_registered)
            {
                var abd = NewData();
                SHAppBarMessage(ABM_REMOVE, ref abd);
                _registered = false;
                ReservedRect = default;
            }
            return;
        }

        if (!_registered)
        {
            var abd = NewData();
            SHAppBarMessage(ABM_NEW, ref abd);
            _registered = true;
        }
        SetPosition();
    }

    public AppBarStrip(uint edge, RECT monitorBounds, int thickness, Action onPosChanged)
    {
        _edge = edge;
        _monitor = monitorBounds;
        _thickness = Math.Max(0, thickness);
        _onPosChanged = onPosChanged;

        // Hidden, non-activating tool window; its position/size are irrelevant since the reservation is driven by the appbar rc, not the window rect.
        CreateHandle(new CreateParams
        {
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = WS_POPUP,
            ExStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        });
    }

    public void Register()
    {
        if (_registered || _thickness <= 0) return;

        var abd = NewData();
        SHAppBarMessage(ABM_NEW, ref abd);
        _registered = true;

        SetPosition();
    }

    /// <summary>Query the system for an approved position, pin our thickness, and reserve it.</summary>
    public void SetPosition()
    {
        if (!_registered) return;

        var abd = NewData();
        abd.rc = DesiredRect();

        // The system adjusts the rect to avoid overlapping the taskbar or other appbars (e.g. moves a bottom strip up above the taskbar).
        SHAppBarMessage(ABM_QUERYPOS, ref abd);

        // QUERYPOS only fixes the approved outer edge, so re-pin our exact thickness against it.
        abd.rc = StripGeometry.PinThickness(_edge, abd.rc, _thickness);

        // Idempotent: if the reservation is unchanged, don't SETPOS, so the work area (and maximised windows) stays undisturbed.
        var rc = abd.rc;
        if (rc.Left == ReservedRect.Left && rc.Top == ReservedRect.Top &&
            rc.Right == ReservedRect.Right && rc.Bottom == ReservedRect.Bottom)
            return;

        SHAppBarMessage(ABM_SETPOS, ref abd);
        ReservedRect = abd.rc;
    }

    public void Remove()
    {
        if (_registered)
        {
            var abd = NewData();
            SHAppBarMessage(ABM_REMOVE, ref abd);
            _registered = false;
        }
        if (Handle != IntPtr.Zero) DestroyHandle();
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = Handle,
        uCallbackMessage = WM_APPBAR,
        uEdge = _edge,
    };

    private RECT DesiredRect() => StripGeometry.DesiredRect(_edge, _monitor, _thickness);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_APPBAR)
        {
            int notification = m.WParam.ToInt32();
            if (notification is ABN_POSCHANGED or ABN_FULLSCREENAPP)
                _onPosChanged();
            return;
        }
        base.WndProc(ref m);
    }
}
