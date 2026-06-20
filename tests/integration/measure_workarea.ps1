# Prints each monitor's full bounds and work area (work area reflects appbar reservations).
Add-Type -AssemblyName System.Windows.Forms
$i = 0
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
  $b = $s.Bounds; $w = $s.WorkingArea
  "Mon{0} {1,-8} Bounds=({2},{3})-({4},{5})  Work=({6},{7})-({8},{9})" -f `
    $i, $(if($s.Primary){"PRIMARY"}else{""}), `
    $b.Left,$b.Top,$b.Right,$b.Bottom, `
    $w.Left,$w.Top,$w.Right,$w.Bottom
  $i++
}
