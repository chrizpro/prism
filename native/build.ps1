# Builds the Prism kernel shim against Open CASCADE (installed through vcpkg) and stages the DLLs in native/bin.
# Prerequisite (once):  vcpkg install opencascade:x64-windows-rel --overlay-triplets=<vcpkg>\triplets-custom
param(
    [string]$Vcpkg = "C:\vcpkg",
    [string]$Triplet = "x64-windows-rel",
    [string]$Config = "Release"
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$build = Join-Path $here "build"
$bin = Join-Path $here "bin"

cmake -S $here -B $build -G "Visual Studio 17 2022" -A x64 `
    "-DCMAKE_TOOLCHAIN_FILE=$Vcpkg/scripts/buildsystems/vcpkg.cmake" `
    "-DVCPKG_TARGET_TRIPLET=$Triplet" `
    "-DVCPKG_OVERLAY_TRIPLETS=$Vcpkg/triplets-custom"
cmake --build $build --config $Config --parallel

New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item (Join-Path $build "$Config\prism_kernel.dll") $bin -Force
Copy-Item (Join-Path $build "$Config\prism_sim.dll") $bin -Force
# Open CASCADE + its dependencies (freetype, zlib, ...) live in the vcpkg bin folder.
Copy-Item (Join-Path $Vcpkg "installed\$Triplet\bin\*.dll") $bin -Force
Write-Host "Kernel staged in $bin"
