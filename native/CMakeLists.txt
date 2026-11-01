cmake_minimum_required(VERSION 3.20)
project(prism_kernel CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
if(APPLE)
    set(CMAKE_INSTALL_RPATH "@loader_path")
elseif(UNIX)
    set(CMAKE_INSTALL_RPATH "$ORIGIN")
endif()
set(CMAKE_BUILD_WITH_INSTALL_RPATH ON)

find_package(OpenCASCADE CONFIG REQUIRED)

if(EMSCRIPTEN)
    set(PRISM_LIB_TYPE STATIC)   # linked into dotnet.native.wasm via NativeFileReference
else()
    set(PRISM_LIB_TYPE SHARED)
endif()
add_library(prism_kernel ${PRISM_LIB_TYPE} kernel.cpp)
if(EMSCRIPTEN)
    # Name must match DllImport("prism_kernel") for the dotnet pinvoke table, so no 'lib' prefix.
    set_target_properties(prism_kernel PROPERTIES PREFIX "")
endif()
target_include_directories(prism_kernel PRIVATE ${OpenCASCADE_INCLUDE_DIR})
target_link_libraries(prism_kernel PRIVATE
    TKernel TKMath TKG2d TKG3d TKGeomBase TKBRep TKGeomAlgo TKTopAlgo
    TKPrim TKBO TKBool TKOffset TKFillet TKMesh TKShHealing TKFeat
    TKXSBase TKDESTEP TKDEIGES TKDESTL TKHLR)

# ---- simulation core: Eigen finite elements + Netgen tetrahedral meshing
find_package(Eigen3 CONFIG REQUIRED)
if(EMSCRIPTEN)
    # Netgen has no Emscripten port yet: build the FE core and topology optimizer only.
    add_library(prism_sim STATIC sim.cpp sim_netgen_stub.cpp topo.cpp)
    target_include_directories(prism_sim PRIVATE ${CMAKE_PREFIX_PATH}/include)
    target_link_libraries(prism_sim PRIVATE Eigen3::Eigen)
    set_target_properties(prism_sim PROPERTIES PREFIX "")
else()
    find_library(NGLIB_LIB nglib REQUIRED)
    find_library(NGCORE_LIB ngcore REQUIRED)
    add_library(prism_sim SHARED sim.cpp sim_netgen.cpp topo.cpp)
    target_include_directories(prism_sim PRIVATE ${CMAKE_PREFIX_PATH}/include)
    target_link_libraries(prism_sim PRIVATE Eigen3::Eigen ${NGLIB_LIB} ${NGCORE_LIB})
endif()
install(TARGETS prism_kernel prism_sim RUNTIME DESTINATION . LIBRARY DESTINATION .)
