# Build the geometry/simulation shims and stage their dynamic dependencies.
[CmdletBinding()]
param(
    [string]$Vcpkg = 'C:/vcpkg',
    [string]$Triplet,
    [string]$Config = 'Release',
    [string]$BuildDirectory,
    [string]$NativeDirectory
)
$ErrorActionPreference = 'Stop'
if (!$Triplet) {
    $Triplet = if ($IsWindows) { 'x64-windows-rel' } elseif ($IsMacOS) { 'arm64-osx-dynamic' } else { 'x64-linux-dynamic' }
}
if (!$BuildDirectory) { $BuildDirectory = Join-Path $PSScriptRoot 'build' }
if (!$NativeDirectory) { $NativeDirectory = Join-Path $PSScriptRoot 'bin' }
$prefix = Join-Path $Vcpkg "installed/$Triplet"
if (!(Test-Path -LiteralPath $prefix)) { throw "Install opencascade, eigen3 and netgen for $Triplet in $Vcpkg first." }
$configure = @('-S',$PSScriptRoot,'-B',$BuildDirectory,"-DCMAKE_TOOLCHAIN_FILE=$Vcpkg/scripts/buildsystems/vcpkg.cmake",
    "-DVCPKG_TARGET_TRIPLET=$Triplet","-DVCPKG_OVERLAY_TRIPLETS=$PSScriptRoot/triplets;$Vcpkg/triplets-custom",
    "-DCMAKE_PREFIX_PATH=$prefix","-DCMAKE_BUILD_TYPE=$Config")
if ($IsWindows) { $configure += @('-G','Visual Studio 17 2022','-A','x64') }
& cmake @configure
if ($LASTEXITCODE -ne 0) { throw 'Native configure failed.' }
& cmake --build $BuildDirectory --config $Config --parallel 2
if ($LASTEXITCODE -ne 0) { throw 'Native compile failed.' }
& cmake --install $BuildDirectory --config $Config --prefix $NativeDirectory
if ($LASTEXITCODE -ne 0) { throw 'Native staging failed.' }
New-Item -ItemType Directory -Path $NativeDirectory -Force | Out-Null
$patterns = if ($IsWindows) { @('bin/*.dll') } elseif ($IsMacOS) { @('lib/*.dylib') } else { @('lib/*.so*') }
foreach ($pattern in $patterns) { Copy-Item -Path (Join-Path $prefix $pattern) -Destination $NativeDirectory -Force }
foreach ($copyright in Get-ChildItem -LiteralPath (Join-Path $prefix 'share') -Recurse -File -Filter copyright) {
    $name = Split-Path (Split-Path $copyright.FullName -Parent) -Leaf
    $license = Join-Path $NativeDirectory "licenses/$name"
    New-Item -ItemType Directory -Path $license -Force | Out-Null
    Copy-Item -LiteralPath $copyright.FullName -Destination $license -Force
}
Write-Output "Native libraries and license notices staged in $NativeDirectory"
