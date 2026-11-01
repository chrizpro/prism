# Build the native kernel + FE core as static WASM libraries (Emscripten from the .NET wasm-tools workload).
# Prerequisite: vcpkg install opencascade eigen3 for wasm32-emscripten (see native/triplets).
[CmdletBinding()]
param([string]$Vcpkg = 'C:/vcpkg', [string]$BuildDirectory = (Join-Path $PSScriptRoot 'build-wasm'))
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/wasm-env.ps1"
$ninja = Get-ChildItem "$Vcpkg/downloads/tools" -Directory -Filter 'ninja-*' | Select-Object -First 1
if ($ninja) { $env:PATH = "$($ninja.FullName);$env:PATH" }
$prefix = "$Vcpkg/installed/wasm32-emscripten-eh"
$toolchain = "$env:EMSCRIPTEN_ROOT/cmake/Modules/Platform/Emscripten.cmake".Replace([string][char]92, '/')
& cmake -S $PSScriptRoot -B $BuildDirectory -G Ninja "-DCMAKE_TOOLCHAIN_FILE=$toolchain" "-DCMAKE_PREFIX_PATH=$prefix" "-DCMAKE_FIND_ROOT_PATH=$prefix" -DCMAKE_BUILD_TYPE=Release '-DCMAKE_CXX_FLAGS=-fwasm-exceptions'
if ($LASTEXITCODE -ne 0) { throw 'WASM configure failed.' }
& cmake --build $BuildDirectory --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'WASM compile failed.' }
Get-ChildItem $BuildDirectory -Filter *.a | Select-Object Name, Length
