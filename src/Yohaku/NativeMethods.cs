using System.Runtime.InteropServices;

namespace Yohaku;

/// <summary>
/// P/Invoke surface for AppBar reservation (SHAppBarMessage), monitor
/// enumeration, and DPI.
/// </summary>
internal static class NativeMethods
{
    // ---- Geometry ------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
    }

    // ---- AppBar (SHAppBarMessage) -------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    // dwMessage values for SHAppBarMessage
    public const uint ABM_NEW = 0x00000000;
    public const uint ABM_REMOVE = 0x00000001;
    public const uint ABM_QUERYPOS = 0x00000002;
    public const uint ABM_SETPOS = 0x00000003;
    public const uint ABM_GETSTATE = 0x00000004;
    public const uint ABM_GETTASKBARPOS = 0x00000005;

    // ABM_GETSTATE result flag: the taskbar is set to auto-hide.
    public const uint ABS_AUTOHIDE = 0x00000001;

    // uEdge values
    public const uint ABE_LEFT = 0;
    public const uint ABE_TOP = 1;
    public const uint ABE_RIGHT = 2;
    public const uint ABE_BOTTOM = 3;

    // Notification codes delivered via the appbar's uCallbackMessage (wParam)
    public const int ABN_STATECHANGE = 0x0000;
    public const int ABN_POSCHANGED = 0x0001;
    public const int ABN_FULLSCREENAPP = 0x0002;
    public const int ABN_WINDOWARRANGE = 0x0003;

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // ---- Monitor enumeration / info -----------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;   // full monitor bounds (physical px)
        public RECT rcWork;      // work area, excludes taskbar + appbars (physical px)
        public uint dwFlags;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ---- DWM corner preference (reserved for later rounded-corner work) -

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    public enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
