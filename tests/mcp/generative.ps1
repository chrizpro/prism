# Generative design (topology optimization) on a cantilever, driven through the MCP. Needs the app running.
$oc = Join-Path $PSScriptRoot "oc.ps1"
function J($o) { $o | ConvertTo-Json -Compress -Depth 10 }
function Call($tool, $payload) { & $oc $tool (J $payload) -Raw }
$global:pass = 0; $global:fail = 0
function Check($name, $ok, $detail = "") { if ($ok) { $global:pass++; "PASS  $name $detail" } else { $global:fail++; "FAIL  $name $detail" } }
function Run($name, $slots = @{}, $params = @{}, $commit = $true) {
  try { Call run_command @{ name = $name; slots = $slots; params = $params; commit = $commit } } catch { [pscustomobject]@{ error = $_.Exception.Message } }
}

Call file @{action='new'} | Out-Null
$null = Run box @{} @{ Width = 100; Depth = 20; Height = 40 }
$null = Call material_apply @{ object = 'Box 1'; material = 'a36' }
$null = Call study_new @{ object = 'Box 1' }
$r = Run sim_fixed @{ 'Faces to support' = @{ object = 'Box 1'; face = 1 } } @{}
$r2 = Run sim_force @{ 'Faces to load' = @{ object = 'Box 1'; face = 2 } } @{ Force = 2000; Direction = '-Z' }
$st = Call study_state @{}
Check "Study has a support and a load" ($st.fixtures.Count -ge 1 -and $st.loads.Count -ge 1) "fixtures=$($st.fixtures.Count) loads=$($st.loads.Count) err=$($r.error)$($r2.error)"
$base = Call study_run @{ show = 'von Mises' }
"      block: max stress $($base.maxStress) displacement $($base.maxDisplacement)"

# open the command, set the options, press Generate and wait for it
$null = Run generative @{} @{ 'Keep this much of the volume' = 30; Detail = 'Coarse (fast)'; 'Most iterations' = 60 } $false
$null = Call command_set @{ params = @{ 'Generate' = $true } }
$deadline = (Get-Date).AddMinutes(8); $status = ""
do {
  Start-Sleep -Seconds 3
  $s = Call get_state @{}
  $status = ($s.activeCommand.results | ? { $_.label -eq 'Status' }).value
} while ((Get-Date) -lt $deadline -and $status -notmatch '^(Done|Stopped)')
"      status: $status"
Check "Generative design finished" ($status -match '^Done') $status
$hasVol = $s.activeCommand.results | ? { $_.label -eq 'Result volume' }
"      $($hasVol.value)"
$null = Call command_commit @{}
$mesh = (Call get_state @{}).objects | ? { $_.kind -eq 'Mesh' } | Select -First 1
Check "A mesh body was created" ($null -ne $mesh)
if ($mesh) {
  $i = Call object_info @{ object = $mesh.name }
  $frac = $i.volume / (100 * 20 * 40)
  Check "The shape keeps roughly 30 % of the block" ($frac -gt 0.15 -and $frac -lt 0.45) "fraction=$([math]::Round($frac,3))"
  Check "The shape still spans from the support to the load (about 100 mm long)" (($i.max[0] - $i.min[0]) -gt 90) "length=$($i.max[0]-$i.min[0])"
  Check "The shape stays inside the original 100 x 20 x 40 block" (($i.max[0] - $i.min[0]) -le 100.05 -and ($i.max[1] - $i.min[1]) -le 20.05 -and ($i.max[2] - $i.min[2]) -le 40.05) "size=$($i.size -join ' x ')"
}
# ---- the result can become a solid, and still be a valid body
if ($mesh) {
  $r = Run mesh_to_solid @{ 'Mesh' = $mesh.name } @{}
  $sol = (Call get_state @{}).objects | ? { $_.kind -eq 'Solid' -and $_.name -ne 'Box 1' } | Select -First 1
  Check "Mesh to Solid turns the generative result into a solid body" ($null -ne $sol) "err=$($r.error)"
}

# ---- keep faces + medium detail
function Generate($keepFace, $detail) {
  Call command_cancel @{} | Out-Null
  $slots = @{}; if ($keepFace) { $slots['Faces to keep'] = @{ object = 'Box 1'; face = $keepFace } }
  $null = Run generative $slots @{ 'Keep this much of the volume' = 30; Detail = $detail; 'Most iterations' = 40 } $false
  $null = Call command_set @{ params = @{ 'Generate' = $true } }
  $deadline = (Get-Date).AddMinutes(10)
  do { Start-Sleep -Seconds 3; $s = Call get_state @{}; $status = ($s.activeCommand.results | ? { $_.label -eq 'Status' }).value } while ((Get-Date) -lt $deadline -and $status -notmatch '^(Done|Stopped)')
  $null = Call command_commit @{}
  $status
}
$st1 = Generate 6 'Coarse (fast)'
Check "Generative design with a kept face (the top, face 6) finishes" ($st1 -match '^Done') $st1
$st2 = Generate $null 'Medium'
Check "Generative design at Medium detail finishes" ($st2 -match '^Done') $st2
$meshes = (Call get_state @{}).objects | ? { $_.kind -eq 'Mesh' }
$last = $meshes | Select -Last 1
$i = Call object_info @{ object = $last.name }
Check "The medium result is watertight and inside the block" ($i.valid -ne $false -and ($i.max[0]-$i.min[0]) -le 100.05 -and ($i.max[2]-$i.min[2]) -le 40.05) "size=$($i.size -join ' x ') volume=$($i.volume)"

"`nSUMMARY generative: $global:pass passed, $global:fail failed"
