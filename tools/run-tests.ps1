<#
.SYNOPSIS
  Runs every test project and prints one summary, so one failing project no longer hides the others.
.EXAMPLE
  ./tools/run-tests.ps1                      # everything
  ./tools/run-tests.ps1 -Filter Hole,Ribbon  # projects whose name contains any of these
  ./tools/run-tests.ps1 -Cadkit              # also check cadkit's slot labels (needs Prism running)
Run from the repository root: the tests look for native/bin relative to it.
#>
param([string[]]$Filter = @(), [switch]$Cadkit, [int]$TimeoutSeconds = 600)

$Filter = @($Filter | ForEach-Object { $_ -split ",\s*" } | Where-Object { $_ })   # -File passes "a,b" as one string
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$projects = Get-ChildItem tests -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") } | Sort-Object Name
if ($Filter.Count -gt 0) {
    $projects = $projects | Where-Object { $name = $_.Name; @($Filter | Where-Object { $name -like "*$_*" }).Count -gt 0 }
}
$results = @()
foreach ($p in $projects) {
    Write-Host ("running {0} ..." -f $p.Name) -NoNewline
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $job = Start-Job { param($dir, $name) Set-Location $dir; & dotnet run --project "tests/$name" 2>&1 } -ArgumentList $root, $p.Name
    if (-not (Wait-Job $job -Timeout $TimeoutSeconds)) { Stop-Job $job; $out = @("TIMEOUT after $TimeoutSeconds s") } else { $out = @(Receive-Job $job) }
    Remove-Job $job -Force
    $text = ($out | ForEach-Object { "$_" })
    $ok = $text | Where-Object { $_ -match '^SUCCESS' } | Select-Object -Last 1
    $why = $text | Where-Object { $_ -match 'FAIL|Unhandled exception|error CS|TIMEOUT' } | Select-Object -First 1
    $status = if ($ok -and -not ($text | Where-Object { $_ -match '^FAIL' })) { 'PASS' } else { 'FAIL' }
    Write-Host (" {0} ({1:n0}s)" -f $status, $sw.Elapsed.TotalSeconds)
    $results += [pscustomobject]@{ Project = $p.Name; Status = $status; Detail = if ($status -eq 'PASS') { $ok } else { $why } }
}
if ($Cadkit) {
    $out = & python tests/check_cadkit_slots.py 2>&1
    $results += [pscustomobject]@{ Project = 'cadkit slot labels'; Status = $(if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'FAIL' }); Detail = ($out | Select-Object -Last 1) }
}
Write-Host ""
$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object Status -eq 'FAIL')
Write-Host ("{0} of {1} passed" -f ($results.Count - $failed.Count), $results.Count)
exit $failed.Count
