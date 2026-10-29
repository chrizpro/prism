$oc = Join-Path $PSScriptRoot "oc.ps1"
function J($o) { $o | ConvertTo-Json -Compress -Depth 10 }
function Call($tool, $payload) { $r = & $oc $tool (J $payload) -Raw; $r }
$global:pass = 0; $global:fail = 0
function Check($name, $ok, $detail = "") { if ($ok) { $global:pass++; "PASS  $name $detail" } else { $global:fail++; "FAIL  $name $detail" } }
function Near($a, $b, $tol = 0.01) { [math]::Abs($a - $b) -le $tol }
function Run($name, $slots = @{}, $params = @{}, $points = $null, $commit = $true) {
  $req = @{ name = $name; slots = $slots; params = $params; commit = $commit }; if ($points) { $req.points = $points }
  try { Call run_command $req } catch { [pscustomobject]@{ error = $_.Exception.Message } }
}
function Reset() { Call file @{action='new'} | Out-Null }
function Last($r) { if ($r.created -and $r.created.Count -gt 0) { $r.created[$r.created.Count-1] } else { $null } }
function Info($n) { Call object_info @{object=$n} }
function Curve($pts) { $r = Run curve_through @{} @{} $pts; Last $r }

# ---------------------------------------------------------------- pipe
Reset
$line = Curve @(@(0,0,0),@(100,0,0))
$r = Run pipe @{ 'Path curve' = $line.name } @{ 'Radius at the start' = 5; 'Radius at the end' = 5 }
$p = Last $r
Check "Pipe: straight 100 mm rod of radius 5 has volume pi*25*100" ($p -and (Near $p.volume (3.14159265*25*100) 5)) "volume=$($p.volume) kind=$($p.kind) err=$($r.error)"
$r = Run pipe @{ 'Path curve' = $line.name } @{ 'Radius at the start' = 5; 'Radius at the end' = 2.5 }
$p = Last $r; $expect = 3.14159265*100/3*(25+12.5+6.25)
Check "Pipe: tapered 5 -> 2.5 is a cone frustum" ($p -and ((Near $p.volume $expect 30))) "volume=$($p.volume) expected=$([math]::Round($expect,1)) err=$($r.error)"
$r = Run pipe @{ 'Path curve' = $line.name } @{ 'Radius at the start' = 5; 'Radius at the end' = 5; Result = 'Surface' }
$p = Last $r
Check "Pipe: surface result has lateral area 2*pi*r*L" ($p -and $p.kind -eq 'Surface' -and (Near $p.area (2*3.14159265*5*100) 5)) "kind=$($p.kind) area=$($p.area) err=$($r.error)"

# ---------------------------------------------------------------- sweep 2 rails
Reset
$r1 = Curve @(@(0,0,0),@(100,0,0)); $r2 = Curve @(@(0,20,0),@(100,20,0)); $pf = Curve @(@(0,0,0),@(0,20,0))
$r = Run sweep_2_rails @{ Profile = $pf.name; 'First rail' = $r1.name; 'Second rail' = $r2.name } @{ Result = 'Surface' }
$s = Last $r
Check "Sweep 2 Rails: a 20 mm profile between two parallel 100 mm rails covers 2000 mm2" ($s -and (Near $s.area 2000 20)) "area=$($s.area) size=$($s.size -join ',') err=$($r.error)"

# ---------------------------------------------------------------- edge surface
Reset
$names = @()
$names += (Curve @(@(0,0,0),@(50,0,0))).name
$names += (Curve @(@(50,0,0),@(50,40,10))).name
$names += (Curve @(@(50,40,10),@(0,40,0))).name
$names += (Curve @(@(0,40,0),@(0,0,0))).name
$r = Run edge_surface @{ 'Boundary curves' = $names } @{}
$s = Last $r
Check "Edge Surface across four curves makes one valid surface spanning 50 x 40" ($s -and $s.valid -and (Near ($s.max[0]-$s.min[0]) 50 0.2) -and (Near ($s.max[1]-$s.min[1]) 40 0.2)) "faces=$($s.faces) size=$($s.size -join ',') area=$($s.area) err=$($r.error)"
Reset
$n3 = @((Curve @(@(0,0,0),@(40,0,0))).name, (Curve @(@(40,0,0),@(20,30,5))).name, (Curve @(@(20,30,5),@(0,0,0))).name)
$r = Run edge_surface @{ 'Boundary curves' = $n3 } @{}
$s = Last $r
Check "Edge Surface across three curves (a triangle)" ($s -and $s.valid) "faces=$($s.faces) area=$($s.area) err=$($r.error)"
Reset
$n2 = @((Curve @(@(0,0,0),@(50,0,0))).name, (Curve @(@(0,30,10),@(50,30,10))).name)
$r = Run edge_surface @{ 'Boundary curves' = $n2 } @{}
$s = Last $r
Check "Edge Surface between two curves is a ruled surface of the expected area" ($s -and (Near $s.area (50*[math]::Sqrt(900+100)) 5)) "area=$($s.area) expected=$([math]::Round(50*[math]::Sqrt(1000),1)) err=$($r.error)"

# ---------------------------------------------------------------- project / pull
Reset
$null = Run box @{} @{ Width = 40; Depth = 40; Height = 20 }
Call sketch_begin @{plane='XY'; offset=50} | Out-Null
Call sketch_add @{entities=@(@{type='circle'; center=@(0,0); radius=10})} | Out-Null
Call sketch_finish @{} | Out-Null
$sk = ((Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -First 1).name
$r = Run curve_project @{ 'Curves to project' = @($sk); 'Surface to project onto' = 'Box 1' } @{ Method = 'Along a direction'; Direction = 'Down (-Z)' }
$c = Last $r
Check "Project Curve: a circle at z=50 lands on the box top at z=20 (and the bottom at z=0)" ($c -and (Near $c.max[2] 20 0.01) -and (Near ($c.max[0]-$c.min[0]) 20 0.2)) "z=$($c.min[2])..$($c.max[2]) width=$($c.size[0]) err=$($r.error)"
Reset
$null = Run sphere @{} @{ Radius = 30 }
Call sketch_begin @{plane='XY'; offset=40} | Out-Null
Call sketch_add @{entities=@(@{type='circle'; center=@(0,0); radius=15})} | Out-Null
Call sketch_finish @{} | Out-Null
$sk = ((Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -First 1).name
$r = Run curve_project @{ 'Curves to project' = @($sk); 'Surface to project onto' = 'Sphere 1' } @{ Method = 'Pull normal to the surface' }
$c = Last $r
Check "Pull Curve: the circle is pulled radially onto the 30 mm sphere" ($c -and (Near $c.max[2] 28.1 0.4) -and (Near $c.max[0] 10.5 0.4)) "z=$($c.min[2])..$($c.max[2]) x=$($c.min[0])..$($c.max[0]) err=$($r.error)"

# ---------------------------------------------------------------- isocurves, intersect, contour, duplicate edge
Reset
$null = Run nurbs_patch @{} @{}
$patch = (Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1
$r = Run curve_isocurve @{ 'Surface or face' = $patch.name } @{ Direction = 'U lines (constant U)'; 'How many' = 3 }
Check "Isocurves: three curves are pulled out of a NURBS patch" (@($r.created).Count -eq 3) "created=$(@($r.created).Count) err=$($r.error)"
$r = Run curve_isocurve @{ 'Surface or face' = $patch.name } @{ Direction = 'V lines (constant V)'; 'How many' = 1; 'Position' = 50 }
$c = Last $r
Check "Isocurve at 50% exists and lies inside the patch bounds" ($c -and $c.min[0] -ge $patch.min[0] - 0.01 -and $c.max[0] -le $patch.max[0] + 0.01) "x=$($c.min[0])..$($c.max[0]) err=$($r.error)"

Reset
$null = Run box @{} @{ Width = 40; Depth = 40; Height = 40 }
$null = Run cylinder @{} @{ Radius = 12; Height = 80; 'Position Z' = -20 }
$r = Run curve_intersect_surfaces @{ 'First surface or body' = 'Box 1'; 'Second surface or body' = 'Cylinder 1' } @{}
$c = Last $r
Check "Intersect Surfaces: a cylinder through a box leaves a closed intersection curve" ($c -and $c.edges -ge 2) "edges=$($c.edges) size=$($c.size -join ',') err=$($r.error)"

Reset
$null = Run sphere @{} @{ Radius = 20 }
$r = Run curve_contour @{ 'Body or surface' = 'Sphere 1' } @{ 'Slice along' = 'Z'; Spacing = 5 }
Check "Contour: a 20 mm sphere cut every 5 mm gives 7 slices" (@($r.created).Count -eq 7) "created=$(@($r.created).Count) err=$($r.error)"
$mid = @($r.created) | ? { [math]::Abs($_.center[2]) -lt 0.01 } | Select -First 1
Check "Contour: the middle slice is a circle of radius 20" ($mid -and (Near $mid.size[0] 40 0.2)) "mid size=$($mid.size -join ',')"

Reset
$null = Run box @{} @{ Width = 40; Depth = 40; Height = 20 }
$r = Run curve_duplicate_edge @{ Edges = @(@{object='Box 1'; edge=1}, @{object='Box 1'; edge=2}) } @{}
$c = Last $r
Check "Duplicate Edge copies two box edges as curves" ($c -and $c.edges -ge 2) "edges=$($c.edges) err=$($r.error)"

# ---------------------------------------------------------------- rebuild surface
Reset
$null = Run nurbs_patch @{} @{}
$patch = (Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1
$r = Run rebuild_surface @{ 'Surface or face' = $patch.name } @{ 'Points in U' = 6; 'Points in V' = 6; 'Keep the original' = $true }
$s = Last $r
Check "Rebuild Surface returns one patch of about the same size" ($s -and (Near $s.size[0] $patch.size[0] 1) -and (Near $s.size[1] $patch.size[1] 1)) "size=$($s.size -join ',') original=$($patch.size -join ',') err=$($r.error)"

# ---------------------------------------------------------------- variable fillet
Reset
$null = Run box @{} @{ Width = 40; Depth = 40; Height = 40 }
$v0 = (Info 'Box 1').volume
$edge = @{object='Box 1'; edge=1}
$r = Run fillet @{ Edges = @($edge) } @{ Radius = 2 }
$vConst2 = (Info 'Box 1').volume
Reset
$null = Run box @{} @{ Width = 40; Depth = 40; Height = 40 }
$r = Run fillet @{ Edges = @($edge) } @{ Radius = 8 }
$vConst8 = (Info 'Box 1').volume
Reset
$null = Run box @{} @{ Width = 40; Depth = 40; Height = 40 }
$r = Run fillet @{ Edges = @($edge) } @{ Radius = 2; 'End radius' = 8 }
$vVar = (Info 'Box 1').volume
Check "Variable Fillet (2 -> 8) removes more material than a constant 2 and less than a constant 8" ($vVar -lt $vConst2 -and $vVar -gt $vConst8) "box=$v0 r2=$vConst2 var=$vVar r8=$vConst8 err=$($r.error)"


# ---------------------------------------------------------------- blend surface
Reset
Call sketch_begin @{plane='XY'} | Out-Null; Call sketch_add @{entities=@(@{type='rect'; corner1=@(0,0); corner2=@(20,30)})} | Out-Null; Call sketch_finish @{} | Out-Null
Call sketch_begin @{plane='YZ'; offset=50} | Out-Null; Call sketch_add @{entities=@(@{type='rect'; corner1=@(0,20); corner2=@(30,40)})} | Out-Null; Call sketch_finish @{} | Out-Null
Run planar_surface @{ 'Closed boundary' = 'Sketch 1' } | Out-Null
Run planar_surface @{ 'Closed boundary' = 'Sketch 2' } | Out-Null
$areas = @{}
foreach ($cont in @('Position (G0)','Tangent (G1)','Curvature (G2)')) {
  $r = Run blend_surface @{ 'First edge' = @{object='Surface 1'; edge=2}; 'Second edge' = @{object='Surface 2'; edge=1} } @{ Continuity = $cont }
  $s = Last $r; $areas[$cont] = $s.area
  Check "Blend Surface $cont joins the two surfaces (x 20..50, z 0..20, valid)" ($s -and $s.valid -and (Near $s.min[0] 20 0.05) -and (Near $s.max[0] 50 0.1) -and (Near $s.min[2] 0 0.05) -and (Near $s.max[2] 20 0.1)) "size=$($s.size -join ',') area=$($s.area) err=$($r.error)"
}
Check "Blend Surface: tangent/curvature blends are longer than the straight (G0) one" ($areas['Tangent (G1)'] -gt $areas['Position (G0)'] -and $areas['Curvature (G2)'] -gt $areas['Position (G0)']) "G0=$($areas['Position (G0)']) G1=$($areas['Tangent (G1)']) G2=$($areas['Curvature (G2)'])"
"`nSUMMARY surfaces: $global:pass passed, $global:fail failed"
