<#
  Confirms a maximised window is (a) TRULY maximised and (b) inset from the
  monitor edges by Yohaku's configured margin — on ANY monitor size, taskbar
  position, or DPI scale. Run this with Yohaku running.

  How it stays setup-independent:
    * The monitor and its work area are read at runtime (GetMonitorInfo) for the
      window's own monitor — never hardcoded.
    * The work area already reflects the taskbar AND Yohaku's reservation, so the
      visible maximised window should fill it exactly.
    * The per-edge gap between the full monitor and the work area must be >= the
      configured inset; the smallest gap (an edge with no taskbar) equals the
      inset, which is what proves Yohaku is reserving the margin.
    * The process is made Per-Monitor-V2 DPI aware so GetMonitorInfo and the DWM
      frame bounds are both in physical pixels and directly comparable; the inset
      is scaled to physical px via the monitor's real DPI.
#>
param([int]$Inset = 12, [int]$Tolerance = 1)

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
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO {
    public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
}
"@
}

# Per-Monitor-V2 = (IntPtr)-4. Best-effort: harmless if already set or unsupported.
[void][M]::SetProcessDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Windows.Forms

function Pump([int]$ms){ $n=[int]($ms/100); for($i=0;$i -lt $n;$i++){[System.Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 100} }
$DWMWA_EXTENDED_FRAME_BOUNDS = 9
$MONITOR_DEFAULTTONEAREST    = 2

$form = New-Object System.Windows.Forms.Form
$form.Text="Yohaku Verify"; $form.StartPosition='Manual'
$form.Left=200; $form.Top=200; $form.Width=600; $form.Height=400
$form.Show(); $form.Refresh(); $h=$form.Handle
[System.Windows.Forms.Application]::DoEvents(); Pump 400

[M]::ShowWindow($h,3)|Out-Null; Pump 800   # SW_MAXIMIZE

# Visible bounds (what the user sees) and the window's monitor geometry.
$vb = New-Object M+RECT; [M]::DwmGetWindowAttribute($h,$DWMWA_EXTENDED_FRAME_BOUNDS,[ref]$vb,16)|Out-Null
$mon = [M]::MonitorFromWindow($h,$MONITOR_DEFAULTTONEAREST)
$mi  = New-Object M+MONITORINFO; $mi.cbSize=[System.Runtime.InteropServices.Marshal]::SizeOf($mi)
[M]::GetMonitorInfo($mon,[ref]$mi)|Out-Null
$zoomed = [M]::IsZoomed($h)
$dpi = [M]::GetDpiForWindow($h)
$insetPx = [int][math]::Round($Inset * $dpi / 96.0)

# Pull each edge into an explicit scalar int. PowerShell can otherwise surface a
# value-type field as a single-element array, which breaks the arithmetic below.
$bL=[int]$mi.rcMonitor.L; $bT=[int]$mi.rcMonitor.T; $bR=[int]$mi.rcMonitor.R; $bB=[int]$mi.rcMonitor.B
$wL=[int]$mi.rcWork.L;    $wT=[int]$mi.rcWork.T;    $wR=[int]$mi.rcWork.R;    $wB=[int]$mi.rcWork.B
$vL=[int]$vb.L;           $vT=[int]$vb.T;           $vR=[int]$vb.R;           $vB=[int]$vb.B

Write-Host ("Monitor (full)     : ({0},{1})-({2},{3})" -f $bL,$bT,$bR,$bB)
Write-Host ("Work area          : ({0},{1})-({2},{3})" -f $wL,$wT,$wR,$wB)
Write-Host ("Visible window     : ({0},{1})-({2},{3})" -f $vL,$vT,$vR,$vB)
Write-Host ("DPI / inset (phys) : {0} / {1}px (from {2}px logical)" -f $dpi,$insetPx,$Inset)
Write-Host ("IsZoomed           : {0}" -f $zoomed)

# Per-edge gap between the full monitor and the (reserved) work area.
$gaps = @(($wL-$bL), ($wT-$bT), ($bR-$wR), ($bB-$wB))
$minGap = ($gaps | Measure-Object -Minimum).Minimum
Write-Host ("Edge gaps L,T,R,B  : {0}  (min {1})" -f ($gaps -join ','),$minGap)

function Near($a,$b){ [math]::Abs($a-$b) -le $Tolerance }

# 1) Truly maximised. 2) Visible window fills the reserved work area exactly.
$fillsWork = (Near $vL $wL) -and (Near $vT $wT) -and (Near $vR $wR) -and (Near $vB $wB)
# 3) Every edge reserves at least the inset; the smallest gap equals the inset
#    (a non-taskbar edge), proving Yohaku's margin is applied.
$reserves = ($gaps | Where-Object { $_ -lt ($insetPx - $Tolerance) }).Count -eq 0
$insetOk  = Near $minGap $insetPx

if ($zoomed -and $fillsWork -and $reserves -and $insetOk) {
  Write-Host "RESULT: PASS - truly maximised, visible window fills the work area, and every edge is inset by $($insetPx)px." -ForegroundColor Green
} else {
  $why = @()
  if (-not $zoomed)    { $why += "not maximised" }
  if (-not $fillsWork) { $why += "visible window != work area" }
  if (-not $reserves)  { $why += "an edge reserves less than ${insetPx}px" }
  if (-not $insetOk)   { $why += "smallest gap ${minGap}px != expected ${insetPx}px (is Yohaku running with Inset=${Inset}?)" }
  Write-Host ("RESULT: FAIL - {0}" -f ($why -join '; ')) -ForegroundColor Red
}
$form.Close(); $form.Dispose()
