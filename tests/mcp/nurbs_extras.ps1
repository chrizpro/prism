# Network Surface, Deform and the Edit Points gumball, driven through the MCP. Needs the app running.
$oc = Join-Path $PSScriptRoot "oc.ps1"
function J($o) { $o | ConvertTo-Json -Compress -Depth 10 }
function Call($tool, $payload) { & $oc $tool (J $payload) -Raw }
$global:pass = 0; $global:fail = 0
function Check($name, $ok, $detail = "") { if ($ok) { $global:pass++; "PASS  $name $detail" } else { $global:fail++; "FAIL  $name $detail" } }
function Near($a, $b, $tol = 0.01) { [math]::Abs($a - $b) -le $tol }
function Run($name, $slots = @{}, $params = @{}, $points = $null, $commit = $true) {
  $req = @{ name = $name; slots = $slots; params = $params; commit = $commit }; if ($points) { $req.points = $points }
  try { Call run_command $req } catch { [pscustomobject]@{ error = $_.Exception.Message } }
}
function Reset() { Call file @{action='new'} | Out-Null }
function Last($r) { if ($r.created -and $r.created.Count -gt 0) { $r.created[$r.created.Count-1] } else { $null } }
function Curve($pts) { Last (Run curve_through @{} @{} $pts) }
function RectSurface($w, $h) {
  Call sketch_begin @{plane='XY'} | Out-Null
  Call sketch_add @{entities=@(@{type='rect'; corner1=@(0,0); corner2=@($w,$h)})} | Out-Null
  Call sketch_finish @{} | Out-Null
  $sk = ((Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -Last 1).name
  Last (Run planar_surface @{ 'Closed boundary' = $sk } @{})
}

# ------------------------------------------------------------------ network surface
Reset
$names = @((Curve @(@(0,0,0),@(60,0,0))).name, (Curve @(@(60,0,0),@(60,40,0))).name, (Curve @(@(60,40,0),@(0,40,0))).name, (Curve @(@(0,40,0),@(0,0,0))).name)
$flat = Last (Run network_surface @{ 'Outline curves' = $names } @{})
Check "Network Surface with only an outline is flat" ($flat -and (Near $flat.min[2] 0 0.2) -and (Near $flat.max[2] 0 0.2)) "z=$($flat.min[2])..$($flat.max[2]) err="
$across = (Curve @(@(10,20,8),@(30,20,14),@(50,20,8))).name
$r = Run network_surface @{ 'Outline curves' = $names; 'Curves across' = @($across) } @{ 'Points taken' = 40 }
$s = Last $r
Check "Network Surface pulled through an arched inner curve rises to about 14 mm" ($s -and $s.valid -and $s.max[2] -gt 11 -and $s.max[2] -lt 16 -and (Near ($s.max[0]-$s.min[0]) 60 0.3) -and (Near ($s.max[1]-$s.min[1]) 40 0.3)) "z=$($s.min[2])..$($s.max[2]) size=$($s.size -join ',') err=$($r.error)"

# ------------------------------------------------------------------ deform
Reset
$plate = RectSurface 100 20
$r = Run deform @{ 'Curves or surfaces' = $plate.name } @{ Deform = 'Bend'; 'Along axis' = 'X'; 'Bend toward' = '+Z'; Angle = 90 }
$d = (Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1
$expect = 100 / (3.14159265/2)
Check "Deform Bend: a 100 mm strip bent 90 degrees becomes a quarter circle of radius 63.7" ($d -and (Near ($d.max[0]-$d.min[0]) $expect 4) -and (Near ($d.max[2]-$d.min[2]) $expect 4)) "size=$((($d.max[0]-$d.min[0]), ($d.max[1]-$d.min[1]), ($d.max[2]-$d.min[2])) -join ',') expected ~$([math]::Round($expect,1)) err=$($r.error)"

Reset
$plate = RectSurface 100 20
$r = Run deform @{ 'Curves or surfaces' = $plate.name } @{ Deform = 'Twist'; 'Along axis' = 'X'; Angle = 90 }
$d = (Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1
Check "Deform Twist: a strip twisted 90 degrees about its length gains about 20 mm of height" ($d -and (Near ($d.max[2]-$d.min[2]) 20 2.5) -and (Near ($d.max[0]-$d.min[0]) 100 2)) "size=$((($d.max[0]-$d.min[0]), ($d.max[1]-$d.min[1]), ($d.max[2]-$d.min[2])) -join ',') err=$($r.error)"

Reset
$plate = RectSurface 100 20
$r = Run deform @{ 'Curves or surfaces' = $plate.name } @{ Deform = 'Taper'; 'Along axis' = 'X'; 'End scale' = 0.5 }
$info = Call object_info @{ object = ((Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1).name }
Check "Deform Taper: a 100 x 20 strip tapered to half width has the area of a trapezoid (1500)" ($info -and (Near $info.area 1500 60)) "area=$($info.area) err=$($r.error)"

Reset
$plate = RectSurface 100 20
$r = Run deform @{ 'Curves or surfaces' = $plate.name } @{ Deform = 'Stretch'; 'Along axis' = 'X'; 'End scale' = 2 }
$d = (Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1
Check "Deform Stretch: scaling the length by 2 gives a 200 mm strip" ($d -and (Near ($d.max[0]-$d.min[0]) 200 1)) "size=$((($d.max[0]-$d.min[0]), ($d.max[1]-$d.min[1])) -join ',') err=$($r.error)"

Reset
$line = Curve @(@(0,0,0),@(100,0,0))
$r = Run deform @{ 'Curves or surfaces' = $line.name } @{ Deform = 'Bend'; 'Along axis' = 'X'; 'Bend toward' = '+Y'; Angle = 60; 'Starts at' = 50 }
$d = (Call get_state @{}).objects | ? { $_.kind -eq 'Curve' } | Select -First 1
Check "Deform Bend on a curve with a region: the first half stays straight, the second half bends toward +Y" ($d -and $d.max[1] -gt 15 -and $d.min[1] -gt -0.5 -and $d.min[0] -lt 0.5) "y=$($d.min[1])..$($d.max[1]) x=$($d.min[0])..$($d.max[0]) err=$($r.error)"

# ------------------------------------------------------------------ gumball on Edit Points
Reset
$null = Run nurbs_patch @{} @{}
$patch = (Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1
$cp = Call get_control_points @{ object = $patch.name }
$p0 = $cp.points[0]
Call view @{ target = @($p0[0], $p0[1], $p0[2]); distance = 260 } | Out-Null
$r = Run edit_points @{ 'Curve or surface' = $patch.name } @{} $null $false
Call pointer @{ events = @(@{type='move'; x=$p0[0]; y=$p0[1]; z=$p0[2]}, @{type='down'; x=$p0[0]; y=$p0[1]; z=$p0[2]}, @{type='up'; x=$p0[0]; y=$p0[1]; z=$p0[2]}) } | Out-Null
function PointX() { $s = Call get_state @{}; ($s.activeCommand.params | ? { $_.label -eq 'Point X' }).value }
$x0 = PointX
Check "Edit Points: clicking a control point selects it (Point X is shown)" ($null -ne $x0 -and (Near $x0 $p0[0] 0.01)) "x=$x0"
$moved = $false
foreach ($k in 10, 14, 18, 22) {
  Call pointer @{ events = @(@{type='move'; x=($p0[0]+$k); y=$p0[1]; z=$p0[2]}, @{type='down'; x=($p0[0]+$k); y=$p0[1]; z=$p0[2]}, @{type='move'; x=($p0[0]+$k+6); y=$p0[1]; z=$p0[2]}, @{type='move'; x=($p0[0]+$k+12); y=$p0[1]; z=$p0[2]}, @{type='up'; x=($p0[0]+$k+12); y=$p0[1]; z=$p0[2]}) } | Out-Null
  $x1 = PointX
  if ([math]::Abs($x1 - $x0) -gt 5) { $moved = $true; break }
}
Check "Edit Points gumball: dragging the X arrow moves the selected point along X by about 12 mm" ($moved -and (Near ($x1-$x0) 12 3)) "dx=$([math]::Round($x1-$x0,2))"
# start again so the points are back where get_control_points says they are, then select two of them.
# A click that misses the gizmo clears the selection, so every attempt re-selects first.
function SelectTwo() {
  Call command_cancel @{} | Out-Null
  $null = Run edit_points @{ 'Curve or surface' = $patch.name } @{} $null $false
  Call pointer @{ events = @(@{type='move'; x=$p0[0]; y=$p0[1]; z=$p0[2]}, @{type='down'; x=$p0[0]; y=$p0[1]; z=$p0[2]}, @{type='up'; x=$p0[0]; y=$p0[1]; z=$p0[2]}) } | Out-Null
  Call pointer @{ events = @(@{type='down'; x=$p1[0]; y=$p1[1]; z=$p1[2]; shift=$true}, @{type='up'; x=$p1[0]; y=$p1[1]; z=$p1[2]; shift=$true}) } | Out-Null
}
$p1 = $cp.points[1]
$cx = ($p0[0] + $p1[0]) / 2; $cy = ($p0[1] + $p1[1]) / 2; $cz = ($p0[2] + $p1[2]) / 2
$rotStatus = ""
foreach ($r2 in 18, 19.5, 21, 22.5, 24, 25.5, 27, 28.5, 30, 31.5) {
  SelectTwo
  Call command_set @{ params = @{ 'Gizmo' = 'Rotate' } } | Out-Null
  # start away from the axes: from the top view the other two rings are edge-on lines along X and Y
  $a0 = 0.8; $a1 = 1.1
  $reply = Call pointer @{ events = @(@{type='move'; x=($cx+$r2*[math]::Cos($a0)); y=($cy+$r2*[math]::Sin($a0)); z=$cz}, @{type='down'; x=($cx+$r2*[math]::Cos($a0)); y=($cy+$r2*[math]::Sin($a0)); z=$cz}, @{type='move'; x=($cx+$r2*[math]::Cos($a1)); y=($cy+$r2*[math]::Sin($a1)); z=$cz}, @{type='up'; x=($cx+$r2*[math]::Cos($a1)); y=($cy+$r2*[math]::Sin($a1)); z=$cz}) }
  if ($reply.status -like 'Rotate*') { $rotStatus = $reply.status; break }
}
Check "Edit Points gumball: dragging the Z ring rotates the two selected points (status reports the angle)" ($rotStatus -match 'Rotate .*about Z') $rotStatus
$scaleStatus = ""
foreach ($k in 10, 14, 18, 22, 26) {
  SelectTwo
  Call command_set @{ params = @{ 'Gizmo' = 'Scale' } } | Out-Null
  $reply = Call pointer @{ events = @(@{type='move'; x=($cx+$k); y=$cy; z=$cz}, @{type='down'; x=($cx+$k); y=$cy; z=$cz}, @{type='move'; x=($cx+$k*1.5); y=$cy; z=$cz}, @{type='up'; x=($cx+$k*1.5); y=$cy; z=$cz}) }
  if ($reply.status -like 'Scale*') { $scaleStatus = $reply.status; break }
}
Check "Edit Points gumball: dragging the X scale handle scales the selection (status reports the factor)" ($scaleStatus -match 'Scale .* along X') $scaleStatusCall command_cancel @{} | Out-Null

"`nSUMMARY extras: $global:pass passed, $global:fail failed"
