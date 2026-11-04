<#
.SYNOPSIS
Run the maintained, solution-managed tests without building mobile applications or legacy executables.
.EXAMPLE
./tools/test.ps1
.EXAMPLE
./tools/test.ps1 -Layer Headless -Feature Revolve
.EXAMPLE
./tools/test.ps1 -List
#>
param(
    [ValidateSet('All', 'Unit', 'Headless')][string]$Layer = 'All',
    [ValidateSet('All', 'Extrude', 'Revolve', 'Sweep', 'Loft', 'Solid', 'Sketch', 'DirectModeling', 'StaticAnalysis')][string]$Feature = 'All',
    [switch]$List
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$resultDirectory = Join-Path $root 'artifacts/tests'
$layers = if ($Layer -eq 'All') { @('Unit', 'Headless') } else { @($Layer) }
$failed = $false
foreach ($name in $layers) {
    $projectName = if ($name -eq 'Unit') { 'Prism.UnitTests' } else { 'Prism.HeadlessTests' }
    $projectPath = Join-Path $root "Prism.Tests/$name/$projectName.csproj"
    $testArguments = @('test', $projectPath, '-c', 'Release', '--logger', 'console;verbosity=normal')
    if ($Feature -ne 'All') { $testArguments += @('--filter', "Feature=$Feature") }
    if ($List) { $testArguments += '--list-tests' }
    else {
        $testArguments += @('--logger', "trx;LogFileName=$name.trx", '--results-directory', $resultDirectory)
    }
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
if ($failed) { exit 1 }
exit 0
