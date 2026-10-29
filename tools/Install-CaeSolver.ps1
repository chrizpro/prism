$ErrorActionPreference = 'Stop'
# External solver, installed per user. CalculiX is GPL-2.0-or-later and runs as a separate process.
$solverRoot = Join-Path $env:LOCALAPPDATA 'Prism\Solvers\CalculiX'
$archive = Join-Path $solverRoot 'calculix_2.23_4win.zip'
New-Item -ItemType Directory -Path $solverRoot -Force | Out-Null
Invoke-WebRequest -Uri 'https://www.dhondt.de/calculix_2.23_4win.zip' -OutFile $archive
$expectedHash = '87FC4E1721DF37370EC67D0A244BB14F9BF804B0A95DF49E2AE7369D30EC72E8'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The CalculiX archive changed. Verify the official download before updating the pinned hash.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $solverRoot -Force
Write-Output ('Installed CalculiX 2.23: ' + (Join-Path $solverRoot 'calculix_2.23_4win\ccx_static.exe'))
