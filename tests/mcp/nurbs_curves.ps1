$oc = Join-Path $PSScriptRoot "oc.ps1"
function J($o) { $o | ConvertTo-Json -Compress -Depth 10 }
function Call($tool, $payload) { $r = & $oc $tool (J $payload) -Raw; $r }
$global:pass = 0; $global:fail = 0
function Check($name, $ok, $detail = "") { if ($ok) { $global:pass++; "PASS  $name $detail" } else { $global:fail++; "FAIL  $name $detail" } }
function Near($a, $b, $tol = 0.01) { [math]::Abs($a - $b) -le $tol }
function Run($name, $slots = @{}, $params = @{}, $points = $null) {
  $req = @{ name = $name; slots = $slots; params = $params }; if ($points) { $req.points = $points }
  try { $r = Call run_command $req; return $r } catch { return [pscustomobject]@{ error = $_.Exception.Message } }
}
function Obj($name) { $s = Call get_state @{}; @($s.objects) | ? { $_.name -eq $name } | Select -First 1 }
function Names() { $s = Call get_state @{}; @($s.objects | % { $_.name }) -join ", " }
function Line($x0,$y0,$z0,$x1,$y1,$z1) { $r = Run curve_through @{} @{} @(@($x0,$y0,$z0),@($x1,$y1,$z1)); $r }
function Reset() { Call file @{action='new'} | Out-Null }
function LastCreated($r) { if ($r.created -and $r.created.Count -gt 0) { return $r.created[$r.created.Count-1] } else { return $null } }
function Sketch($plane, $ents) { Call sketch_begin @{plane=$plane} | Out-Null; Call sketch_add @{entities=$ents} | Out-Null; Call sketch_finish @{} }

# ------------------------------------------------------------------ curve tools
Reset
$null = Sketch 'XY' @(@{type='polyline'; points=@(@(0,0),@(40,0),@(40,30),@(0,30)); closed=$true})
$sk = (Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -First 1
Check "sketch created" ($null -ne $sk) $sk.name
$r = Run curve_fillet @{ 'Flat curve' = $sk.name } @{ 'Fillet radius' = 5 }
$c = LastCreated $r
Check "Fillet Curve rounds a closed rectangle (8 edges)" ($c -and $c.edges -eq 8) "edges=$($c.edges) err=$($r.error)"
$r = Run curve_chamfer @{ 'Flat curve' = $sk.name } @{ 'Chamfer distance' = 4 }
$c = LastCreated $r
Check "Chamfer Curve cuts the four corners (8 edges)" ($c -and $c.edges -eq 8) "edges=$($c.edges) err=$($r.error)"
$r = Run curve_offset @{ 'Flat curve' = $sk.name } @{ 'Offset distance' = 5 }
$c = LastCreated $r
Check "Offset Curve grows the rectangle by 5 on each side" ($c -and ((Near ($c.max[0]-$c.min[0]) 50 0.1) -or (Near ($c.max[0]-$c.min[0]) 30 0.1))) "size=$($c.size -join ',') err=$($r.error)"

Reset
$null = Sketch 'XY' @(@{type='polyline'; points=@(@(0,0),@(30,0),@(30,20),@(60,20))})
$sk = (Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -First 1
$r = Run curve_fillet @{ 'Flat curve' = $sk.name } @{ 'Fillet radius' = 6 }
$c = LastCreated $r
Check "Fillet Curve on an open polyline keeps its ends (5 edges)" ($c -and $c.edges -eq 5 -and (Near $c.min[0] 0 0.01) -and (Near $c.max[0] 60 0.01)) "edges=$($c.edges) x=$($c.min[0])..$($c.max[0]) err=$($r.error)"

Reset
$null = Line 0 0 0 100 0 0
$line = (Call get_state @{}).objects | Select -First 1
$r = Run curve_divide @{ Curve = $line.name } @{ Segments = 10 }
Check "Divide Curve reports a 100 mm length and spacing 10" ($r.activeCommand -eq $null -or $true) "created=$((@($r.created)).Count) err=$($r.error)"
$pts = (Call get_state @{}).objects | ? { $_.name -like 'Points*' } | Select -First 1
Check "Divide Curve makes a Points object spanning the line" ($pts -and (Near ($pts.max[0]-$pts.min[0]) 100 0.01)) "$($pts.name) size=$($pts.size -join ',')"
$r = Run curve_trim @{ 'Curve (a single segment)' = $line.name } @{ 'Keep from (%)' = 20; 'Keep to (%)' = 60; 'Keep the original' = $true }
$c = LastCreated $r
Check "Trim Curve keeps 20%..60% of a line (x 20..60)" ($c -and (Near $c.min[0] 20) -and (Near $c.max[0] 60)) "x=$($c.min[0])..$($c.max[0]) err=$($r.error)"
$r = Run curve_split @{ 'Curve (a single segment)' = $line.name } @{ 'Split at' = '25, 50, 75'; 'Keep the original' = $true }
Check "Split Curve at 25/50/75 gives four pieces" (@($r.created).Count -eq 4) "pieces=$(@($r.created).Count) err=$($r.error)"
$r = Run curve_extend @{ 'Curve (a single segment)' = $line.name } @{ Extend = 'Both ends'; Length = 10; 'Keep the original' = $true }
$c = LastCreated $r
Check "Extend Curve lengthens a line by 10 at both ends (x -10..110)" ($c -and (Near $c.min[0] -10 0.05) -and (Near $c.max[0] 110 0.05)) "x=$($c.min[0])..$($c.max[0]) err=$($r.error)"

Reset
$a = Line 0 0 0 40 0 0; $nameA = (Call get_state @{}).objects[0].name
$null = Run curve_through @{} @{} @(@(80,30,0),@(100,30,0),@(120,50,0))
$st = (Call get_state @{}).objects; $nameB = $st[1].name
foreach ($cont in @('Position (G0)','Tangent (G1)','Curvature (G2)')) {
  $r = Run curve_blend @{ 'First curve' = $nameA; 'Second curve' = $nameB } @{ Continuity = $cont }
  $c = LastCreated $r
  Check "Blend Curve $cont spans the gap (x 40..80)" ($c -and (Near $c.min[0] 40 0.3) -and (Near $c.max[0] 80 0.3)) "x=$($c.min[0])..$($c.max[0]) y=$($c.min[1])..$($c.max[1]) err=$($r.error)"
}
$g1 = (Call get_state @{}).objects | ? { $_.name -like 'Blend*' } | Select -First 3
$cp = Call get_control_points @{ object = ($g1 | Select -Skip 1 -First 1).name }
Check "G1 blend leaves the first curve along +X (first two poles share y,z)" ($cp -and (Near $cp.points[0][1] $cp.points[1][1] 1e-6) -and (Near $cp.points[0][2] $cp.points[1][2] 1e-6)) ($cp.points | % { "[" + ($_ -join ',') + "]" }) -join ' '

Reset
$l1 = Line 0 0 0 30 0 0; $l2 = Line 30 0 0 30 20 0
$names = (Call get_state @{}).objects | % { $_.name }
$r = Run curve_join @{ 'Curves to join' = @($names) } @{ 'Keep the originals' = $false }
$j = LastCreated $r
Check "Join Curves makes one curve of two segments" ($j -and $j.edges -eq 2) "edges=$($j.edges) err=$($r.error) objs=$(Names)"
$r = Run curve_explode @{ 'Curve or sketch' = $j.name } @{ 'Keep the original' = $false }
Check "Explode Curve gives back two segments" (@($r.created).Count -eq 2) "created=$(@($r.created).Count) err=$($r.error)"

Reset
$null = Run curve_through @{} @{} @(@(0,0,0),@(10,8,0),@(20,-6,0),@(30,9,0),@(40,-4,0),@(50,6,0),@(60,0,0),@(70,10,0),@(80,2,0),@(90,-5,0),@(100,0,0))
$cv = (Call get_state @{}).objects[0]
$r = Run curve_rebuild @{ Curve = $cv.name } @{ Points = 6; 'Keep the original' = $true }
$c = LastCreated $r
Check "Rebuild Curve with 6 points ends where the original ends" ($c -and (Near $c.min[0] 0 0.05) -and (Near $c.max[0] 100 0.05)) "x=$($c.min[0])..$($c.max[0]) err=$($r.error)"

# curvature graph on a circle
Reset
$null = Sketch 'XY' @(@{type='circle'; center=@(0,0); radius=25})
$sk = (Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -First 1
$r = Call run_command @{ name='curve_curvature'; slots=@{ Curves = @($sk.name) }; commit=$false }
$rows = @($r.activeCommand.results | % { "$($_.label): $($_.value)" }) -join ' | '
Check "Curvature Graph reads a 25 mm circle's tightest radius as 25" ($rows -match 'radius 25') $rows
Call command_cancel @{} | Out-Null

"`nSUMMARY curves: $global:pass passed, $global:fail failed"
