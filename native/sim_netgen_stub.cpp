// Browser (Emscripten) build: Netgen has no WASM port yet, so meshing reports "unavailable".
#include "prism_exports.h"
static const char* kMsg = "Tetrahedral meshing is not available in the browser build yet. Use the desktop app for simulation meshing.";
SIM_API void* sim_netgen_mesh(const float*, int, double, double, int) { return nullptr; }
SIM_API const char* sim_netgen_error() { return kMsg; }
