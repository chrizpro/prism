set(VCPKG_ENV_PASSTHROUGH_UNTRACKED EMSCRIPTEN_ROOT EMSDK PATH EMSDK_PATH EMSDK_PYTHON EM_CACHE DOTNET_EMSCRIPTEN_LLVM_ROOT DOTNET_EMSCRIPTEN_BINARYEN_ROOT DOTNET_EMSCRIPTEN_NODE_JS)

if(NOT DEFINED ENV{EMSCRIPTEN_ROOT})
   find_path(EMSCRIPTEN_ROOT "emcc")
else()
   set(EMSCRIPTEN_ROOT "$ENV{EMSCRIPTEN_ROOT}")
endif()

if(NOT EMSCRIPTEN_ROOT)
   if(NOT DEFINED ENV{EMSDK})
      message(FATAL_ERROR "The emcc compiler not found in PATH")
   endif()
   set(EMSCRIPTEN_ROOT "$ENV{EMSDK}/upstream/emscripten")
endif()

if(NOT EXISTS "${EMSCRIPTEN_ROOT}/cmake/Modules/Platform/Emscripten.cmake")
   message(FATAL_ERROR "Emscripten.cmake toolchain file not found")
endif()

set(VCPKG_TARGET_ARCHITECTURE wasm32)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_CMAKE_SYSTEM_NAME Emscripten)
# Chainload vcpkg's wrapper toolchain rather than Emscripten.cmake directly:
# the wrapper includes Emscripten.cmake and then applies VCPKG_C(XX)_FLAGS
# and VCPKG_LINKER_FLAGS, which would otherwise be silently dropped.
set(VCPKG_CHAINLOAD_TOOLCHAIN_FILE "${VCPKG_ROOT_DIR}/scripts/toolchains/emscripten.cmake")

# Match the .NET browser runtime: native WebAssembly exception handling, no signal-to-exception conversion (no signals in a browser), release only.
set(VCPKG_BUILD_TYPE release)
set(VCPKG_C_FLAGS "-fwasm-exceptions -UOCC_CONVERT_SIGNALS")
set(VCPKG_CXX_FLAGS "-fwasm-exceptions -UOCC_CONVERT_SIGNALS")
