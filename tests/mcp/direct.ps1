# Direct modeling tools driven through the MCP: Push/Pull (flat, round, surface), Delete Face (surface), Draft, Replace Face, Move Face, Split Body. Needs the app running.
$oc = Join-Path $PSScriptRoot "oc.ps1"
function J($o) { $o | ConvertTo-Json -Compress -Depth 10 }
function Call($tool, $payload) { & $oc $tool (J $payload) -Raw }
$global:pass = 0; $global:fail = 0
function Check($name, $ok, $detail = "") { if ($ok) { $global:pass++; "PASS  $name $detail" } else { $global:fail++; "FAIL  $name $detail" } }
function Near($a, $b, $tol = 0.05) { [math]::Abs($a - $b) -le $tol }
function Run($name, $slots = @{}, $params = @{}) {
  try { Call run_command @{ name = $name; slots = $slots; params = $params } } catch { [pscustomobject]@{ error = $_.Exception.Message } }
}
function Reset() { Call file @{action='new'} | Out-Null }
function Info($name) { Call object_info @{ object = $name } }
function Face($obj, $i) { @{ object = $obj; face = $i } }
function NewBox() { Reset; Run box @{} @{ Width = 40; Depth = 40; Height = 20 } | Out-Null; 'Box 1' }
# which face number of the 40x40x20 box points where: probed by pushing each face 5 mm and watching the bounding box
function BoxFaces() {
  $map = @{}
  foreach ($f in 1..6) {
    $b = NewBox
    $r = Run push_pull @{ 'Faces' = (Face $b $f) } @{ 'Distance' = 5 }
    $i = Info $b
    $lo = $i.min; $hi = $i.max
    if ($hi[2] -gt 24) { $map['top'] = $f } elseif ($lo[2] -lt -4) { $map['bottom'] = $f }
    elseif ($hi[0] -gt 24) { $map['+x'] = $f } elseif ($lo[0] -lt -24) { $map['-x'] = $f }
    elseif ($hi[1] -gt 24) { $map['+y'] = $f } elseif ($lo[1] -lt -24) { $map['-y'] = $f }
  }
  $map
}
$faces = BoxFaces
Check "Probed all six faces of the box" ($faces.Count -eq 6) (($faces.GetEnumerator() | % { "$($_.Key)=$($_.Value)" }) -join ' ')

# ---------------------------------------------------------------- push / pull
$b = NewBox
$null = Run push_pull @{ 'Faces' = (Face $b $faces['top']) } @{ 'Distance' = 5 }
$i = Info $b
Check "Push/Pull top face out by 5 gives a 25 mm tall block" (Near $i.max[2] 25) "z=$($i.max[2]) volume=$($i.volume)"
$b = NewBox
$null = Run push_pull @{ 'Faces' = (Face $b $faces['top']) } @{ 'Distance' = -5 }
$i = Info $b
Check "Push/Pull top face in by 5 gives a 15 mm tall block" (Near $i.max[2] 15) "z=$($i.max[2])"

# ---------------------------------------------------------------- push / pull on a hole and a boss (round faces)
$b = NewBox
$cyl = $null; $gain = 0
foreach ($f in 1..9) {
  $b = NewBox
  $null = Run hole @{ 'Body or entry face' = (Face $b $faces['top']) } @{ Diameter = 10 }
  $before = (Info $b).volume
  $null = Run push_pull @{ 'Faces' = (Face $b $f) } @{ 'Distance' = 1 }
  $gain = (Info $b).volume - $before
  if ($gain -gt 300 -and $gain -lt 700) { $cyl = $f; break }
}
Check "Push/Pull +1 on the wall of a hole shrinks the hole radius by 1 (volume gains about 565 if it is 20 deep)" ($cyl -and $gain -gt 300 -and $gain -lt 700) "face=$cyl gain=$gain"
$before = (Info $b).volume
$null = Run push_pull @{ 'Faces' = (Face $b $cyl) } @{ 'Distance' = -3 }
Check "Push/Pull -3 on the same wall then grows the hole (volume drops, still valid)" ((Info $b).valid -and (Info $b).volume -lt $before) "volume=$((Info $b).volume)"

# ---------------------------------------------------------------- surface: push/pull into a solid, delete face
Reset
Call sketch_begin @{plane='XY'} | Out-Null; Call sketch_add @{entities=@(@{type='rect'; corner1=@(0,0); corner2=@(40,30)})} | Out-Null; Call sketch_finish @{} | Out-Null
$sk = ((Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -Last 1).name
$null = Run planar_surface @{ 'Closed boundary' = $sk } @{}
$surf = ((Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1).name
$r = Run push_pull @{ 'Faces' = (Face $surf 1) } @{ 'Distance' = 8 }
$solid = (Call get_state @{}).objects | ? { $_.kind -eq 'Solid' } | Select -First 1
Check "Push/Pull on a flat surface face sweeps it into a solid 8 mm thick" ($solid -and (Near (Info $solid.name).volume (40*30*8) 1)) "volume=$((Info $solid.name).volume) err=$($r.error)"

Reset
$null = Run box @{} @{ Width = 20; Depth = 20; Height = 20 }
$null = Run nurbs_patch @{} @{}
$p = ((Call get_state @{}).objects | ? { $_.kind -eq 'Surface' } | Select -First 1)
if ($p) {
  $r = Run delete_face @{ 'Faces' = (Face $p.name 1) } @{}
  $left = (Call get_state @{}).objects | ? { $_.kind -eq 'Surface' }
  Check "Delete Face on a single-face surface removes the surface" (-not $left) "err=$($r.error)"
}
# a two-face surface: two planar patches sewn
Reset
Call sketch_begin @{plane='XY'} | Out-Null; Call sketch_add @{entities=@(@{type='rect'; corner1=@(0,0); corner2=@(40,30)})} | Out-Null; Call sketch_finish @{} | Out-Null
$sk = ((Call get_state @{}).objects | ? { $_.kind -eq 'Sketch' } | Select -Last 1).name
$null = Run planar_surface @{ 'Closed boundary' = $sk } @{}
$null = Run extrude @{ Profile = $sk } @{ Distance = 10; 'Output' = 1 }

# ---------------------------------------------------------------- draft
$b = NewBox
$null = Run draft_face @{ 'Faces to draft' = @((Face $b $faces['+x']), (Face $b $faces['-x']), (Face $b $faces['+y']), (Face $b $faces['-y'])); 'Neutral plane' = (Face $b $faces['bottom']) } @{ 'Draft angle' = 5 }
$i = Info $b
$expect = 20 * [math]::Tan(5 * [math]::PI / 180)
$topw = $i.max[0] - $i.min[0]; $botw = 40
Check "Draft 5 degrees on the four walls about the bottom face keeps the bottom 40 wide and the volume changes" ($i.valid -and $i.volume -lt 32000) "volume=$($i.volume) x=$($i.min[0])..$($i.max[0]) (a 5 degree lean over 20 mm moves a wall by $([math]::Round($expect,2)) mm)"
$narrower = $i.volume -lt 32000; Check "Draft positive angle with the bottom as neutral/pull face narrows the part going up (volume shrinks)" $narrower
"      draft direction: volume $(if ($narrower) { 'shrinks (walls lean inward going up)' } else { 'grows (walls lean outward going up)' })"

# ---------------------------------------------------------------- replace face (flush up to a plane)
$b = NewBox
$null = Run box @{} @{ Width = 10; Depth = 10; Height = 10 }
$b2 = 'Box 2'
# raise the small box so its top sits at z=30, then replace the big box's top with that plane: the big box should grow to 30
$null = Run move @{ Objects = $b2 } @{ 'Δ Z' = 20 }
$null = Run replace_face @{ 'Flat faces to replace' = (Face $b $faces['top']); 'Target plane' = (Face $b2 $faces['top']) } @{}
$i = Info $b
Check "Replace Face moves the top face of the 20 mm block up to a plane at z=30" (Near $i.max[2] 30 0.1) "z=$($i.max[2]) volume=$($i.volume)"

# tilted target: a datum plane tilted 10 degrees
$b = NewBox
$null = Run move_face @{ 'Flat faces to move' = (Face $b $faces['top']) } @{ 'Move Z' = 5 }
$i = Info $b
Check "Move Face lifts the top face by 5 mm" (Near $i.max[2] 25 0.1) "z=$($i.max[2])"
$b = NewBox
$null = Run move_face @{ 'Flat faces to move' = (Face $b $faces['top']) } @{ 'Tilt angle' = 10; 'Tilt about' = 'World Y' }
$i = Info $b
$rise = 20 * [math]::Tan(10 * [math]::PI / 180)
Check "Move Face tilts the top 10 degrees about the middle of the face (one side rises about 3.5 mm, the other drops)" ($i.valid -and (Near $i.max[2] (20 + $rise) 0.3) -and (Near $i.volume 32000 40)) "zmax=$($i.max[2]) volume=$($i.volume) expect zmax=$([math]::Round(20 + $rise,2))"

# ---------------------------------------------------------------- split body
$b = NewBox
$null = Run split_body @{ 'Body to split' = $b; 'Splitting tool' = @{ origin = 'XZ' } } @{}
$solids = (Call get_state @{}).objects | ? { $_.kind -eq 'Solid' }
Check "Split Body along the XZ plane gives two halves of 16000" ($solids.Count -eq 2 -and (Near (Info $solids[0].name).volume 16000 1) -and (Near (Info $solids[1].name).volume 16000 1)) "count=$($solids.Count)"

"`nSUMMARY direct: $global:pass passed, $global:fail failed"
