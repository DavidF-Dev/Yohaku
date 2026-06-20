<#
  Confirms a maximised window is (a) TRULY maximised, (b) fills the work area, and
  (c) inset from each monitor edge by Yohaku's configured margin - including the
  taskbar-side override. Setup-independent (any monitor size, taskbar position, or
  DPI scale). Run with Yohaku running.

  Pass -TaskbarInset to check the override on the taskbar's edge; omit it to expect
  the same inset on every edge (the pre-override behaviour).

  Per edge, the monitor-to-work-area gap should equal our reserved inset plus, on
  the taskbar's edge, the taskbar's own thickness. The process is made
  Per-Monitor-V2 DPI aware so every measurement is in directly comparable physical
  pixels, and our inset is scaled to physical px via the monitor's real DPI.
#>
param([int]$Inset = 12, [int]$TaskbarInset = -1, [int]$Tolerance = 1)
if ($TaskbarInset -lt 0) { $TaskbarInset = $Inset }

if (-not ('M' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class M {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
  [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int sz);
  [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO {
    public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
  [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA {
    public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
}
"@
}

# Per-Monitor-V2 = (IntPtr)-4. Best-effort: harmless if already set or unsupported.
[void][M]::SetProcessDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Windows.Forms

function Pump([int]$ms){ $n=[int]($ms/100); for($i=0;$i -lt $n;$i++){[System.Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 100} }
function Near($a,$b){ [math]::Abs($a-$b) -le $Tolerance }
$DWMWA_EXTENDED_FRAME_BOUNDS = 9
$MONITOR_DEFAULTTONEAREST    = 2
$ABM_GETSTATE = 4; $ABM_GETTASKBARPOS = 5; $ABS_AUTOHIDE = 1
$EdgeName = @('Left','Top','Right','Bottom')   # index = ABE_*

$form = New-Object System.Windows.Forms.Form
$form.Text="Yohaku Verify"; $form.StartPosition='Manual'
$form.Left=200; $form.Top=200; $form.Width=600; $form.Height=400
$form.Show(); $form.Refresh(); $h=$form.Handle
[System.Windows.Forms.Application]::DoEvents(); Pump 400

[M]::ShowWindow($h,3)|Out-Null; Pump 800   # SW_MAXIMIZE

$vb = New-Object M+RECT; [M]::DwmGetWindowAttribute($h,$DWMWA_EXTENDED_FRAME_BOUNDS,[ref]$vb,16)|Out-Null
$mon = [M]::MonitorFromWindow($h,$MONITOR_DEFAULTTONEAREST)
$mi  = New-Object M+MONITORINFO; $mi.cbSize=[System.Runtime.InteropServices.Marshal]::SizeOf($mi)
[M]::GetMonitorInfo($mon,[ref]$mi)|Out-Null
$zoomed = [M]::IsZoomed($h)
$dpi = [M]::GetDpiForWindow($h)

# Taskbar edge (ABE_*), its own thickness on that edge, and auto-hide state.
$abd = New-Object M+APPBARDATA; $abd.cbSize=[System.Runtime.InteropServices.Marshal]::SizeOf($abd)
if ([M]::SHAppBarMessage($ABM_GETTASKBARPOS,[ref]$abd).ToUInt64() -eq 0) {
  $tbEdge = -1; $tbThick = 0
} else {
  $tbEdge = [int]$abd.uEdge
  $tbThick = if ($tbEdge -eq 0 -or $tbEdge -eq 2) { [int]$abd.rc.R - [int]$abd.rc.L } else { [int]$abd.rc.B - [int]$abd.rc.T }
}
$autoHide = ([M]::SHAppBarMessage($ABM_GETSTATE,[ref]$abd).ToUInt64() -band $ABS_AUTOHIDE) -ne 0

# Pull each edge into an explicit scalar int. PowerShell can otherwise surface a value-type field as a single-element array, which breaks the arithmetic below.
$bL=[int]$mi.rcMonitor.L; $bT=[int]$mi.rcMonitor.T; $bR=[int]$mi.rcMonitor.R; $bB=[int]$mi.rcMonitor.B
$wL=[int]$mi.rcWork.L;    $wT=[int]$mi.rcWork.T;    $wR=[int]$mi.rcWork.R;    $wB=[int]$mi.rcWork.B
$vL=[int]$vb.L;           $vT=[int]$vb.T;           $vR=[int]$vb.R;           $vB=[int]$vb.B

# Gaps indexed by ABE_*: 0=Left, 1=Top, 2=Right, 3=Bottom.
$gaps = @(($wL-$bL), ($wT-$bT), ($bR-$wR), ($bB-$wB))
$tbName = if ($tbEdge -ge 0) { $EdgeName[$tbEdge] } else { 'none' }

Write-Host ("Monitor (full)   : ({0},{1})-({2},{3})" -f $bL,$bT,$bR,$bB)
Write-Host ("Work area        : ({0},{1})-({2},{3})" -f $wL,$wT,$wR,$wB)
Write-Host ("Visible window   : ({0},{1})-({2},{3})" -f $vL,$vT,$vR,$vB)
Write-Host ("DPI              : {0}" -f $dpi)
Write-Host ("Taskbar          : edge={0} thickness={1}px autoHide={2}" -f $tbName,$tbThick,$autoHide)
Write-Host ("Inset / taskbar  : {0} / {1}px logical" -f $Inset,$TaskbarInset)
Write-Host ("Edge gaps L,T,R,B: {0}" -f ($gaps -join ','))
Write-Host ("IsZoomed         : {0}" -f $zoomed)

$why = @()
for ($e = 0; $e -lt 4; $e++) {
  # Expected gap = our reserved inset (override on the taskbar edge) + the
  # taskbar's own thickness where it sits.
  $logical = if ($e -eq $tbEdge) { $TaskbarInset } else { $Inset }
  $px = [int][math]::Round($logical * $dpi / 96.0)
  $tb = if ($e -eq $tbEdge) { $tbThick } else { 0 }
  $exp = $px + $tb
  $g = [int]$gaps[$e]

  if ($e -eq $tbEdge -and $autoHide) {
    Write-Host ("  {0,-6}: gap {1}px - skipped (taskbar auto-hidden; override does not apply)" -f $EdgeName[$e],$g) -ForegroundColor Yellow
    continue
  }
  $status = if (Near $g $exp) { 'ok' } else { $why += ("{0} gap {1}px != expected {2}px (inset {3} + taskbar {4})" -f $EdgeName[$e],$g,$exp,$px,$tb); 'BAD' }
  Write-Host ("  {0,-6}: gap {1}px  expected {2}px  [{3}]" -f $EdgeName[$e],$g,$exp,$status)
}

$fillsWork = (Near $vL $wL) -and (Near $vT $wT) -and (Near $vR $wR) -and (Near $vB $wB)
if (-not $zoomed)    { $why += "not maximised" }
if (-not $fillsWork) { $why += "visible window != work area" }

if ($why.Count -eq 0) {
  Write-Host "RESULT: PASS - truly maximised, fills the work area, every edge inset as configured." -ForegroundColor Green
} else {
  Write-Host ("RESULT: FAIL - {0} (is Yohaku running with Inset=${Inset}, TaskbarInset=${TaskbarInset}?)" -f ($why -join '; ')) -ForegroundColor Red
}
$form.Close(); $form.Dispose()
