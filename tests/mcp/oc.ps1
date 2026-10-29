param([string]$Tool, [string]$Json = "{}", [switch]$Raw)
$d = Get-Content "$env:LOCALAPPDATA\Prism\automation.json" | ConvertFrom-Json
$body = '{"tool":"' + $Tool + '","args":' + $Json + '}'
$r = Invoke-RestMethod -Uri "http://127.0.0.1:$($d.port)/call" -Method Post -Body $body -ContentType "application/json" -Headers @{ "X-Prism-Token" = $d.token } -TimeoutSec 120
if (-not $r.ok) { Write-Output ("ERROR: " + $r.error); exit 1 }
if ($Raw) { $r.result } else { $r.result | ConvertTo-Json -Depth 12 -Compress }
