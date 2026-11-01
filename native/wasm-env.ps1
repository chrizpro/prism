# Point the shell at the Emscripten SDK that ships with the .NET wasm-tools workload.
# Dot-source before building the kernel for the browser: . native/wasm-env.ps1
param([string]$Pack = 'C:\Program Files\dotnet\packs\Microsoft.NET.Runtime.Emscripten.3.1.56', [string]$Ver = '10.0.3')
$sdk = "$Pack.Sdk.win-x64\$Ver\tools"
$env:EMSDK_PATH = "$sdk\"
$env:EMSDK_PYTHON = "$Pack.Python.win-x64\$Ver\tools\python.exe"
$env:DOTNET_EMSCRIPTEN_LLVM_ROOT = "$sdk\bin"
$env:DOTNET_EMSCRIPTEN_BINARYEN_ROOT = $sdk
$env:DOTNET_EMSCRIPTEN_NODE_JS = "$Pack.Node.win-x64\$Ver\tools\bin\node.exe"
$env:EM_CACHE = "$Pack.Cache.win-x64\$Ver\tools\emscripten\cache"
$env:EMSCRIPTEN_ROOT = "$sdk\emscripten"
$env:PATH = "$sdk\emscripten;$(Split-Path $env:EMSDK_PYTHON);$sdk\bin;$env:PATH"
