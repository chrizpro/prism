cmake_minimum_required(VERSION 3.20)
project(prism_kernel CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(OpenCASCADE CONFIG REQUIRED)

add_library(prism_kernel SHARED kernel.cpp)
target_include_directories(prism_kernel PRIVATE ${OpenCASCADE_INCLUDE_DIR})
target_link_libraries(prism_kernel PRIVATE
    TKernel TKMath TKG2d TKG3d TKGeomBase TKBRep TKGeomAlgo TKTopAlgo
    TKPrim TKBO TKBool TKOffset TKFillet TKMesh TKShHealing TKFeat
    TKXSBase TKDESTEP TKDEIGES TKDESTL TKHLR)

# ---- simulation core: Eigen finite elements + Netgen tetrahedral meshing
find_package(Eigen3 CONFIG REQUIRED)
find_library(NGLIB_LIB nglib REQUIRED)
find_library(NGCORE_LIB ngcore REQUIRED)
add_library(prism_sim SHARED sim.cpp sim_netgen.cpp topo.cpp)
target_include_directories(prism_sim PRIVATE ${CMAKE_PREFIX_PATH}/include)
target_link_libraries(prism_sim PRIVATE Eigen3::Eigen ${NGLIB_LIB} ${NGCORE_LIB})
