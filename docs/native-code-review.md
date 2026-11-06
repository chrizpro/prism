Reviewed the native geometry, simulation, Netgen, topology optimization, and surface mapping sources, plus the simulation P/Invoke wrapper. The geometry shim uses a consistent exception boundary; simulation ownership and input validation were less consistent. This review makes focused safety changes rather than reorganizing the large geometry file.

Fixed:

- Null shape handles could be dereferenced in the shared geometry accessor; shape type and clone also lacked exception boundaries.
- Simulation node, fixture, force, and boundary triangle indices were used without range checks. Mesh creation now rejects invalid indices and degenerate tetrahedra before publishing a handle.
- Short managed fixture/load/gravity arrays could cause native reads past the pinned arrays. The wrapper now checks related lengths before P/Invoke.
- Starting topology on merged/disposed meshes, or stepping disposed topology contexts, could dereference null handles. Simulation operations and disposal now share instance locks to prevent concurrent freeing during a native call.
- Partially constructed simulation meshes, topology contexts, geometry tessellations, and Netgen meshes could leak on exceptions. These allocations now use automatic ownership until successfully returned.
- The topology filter packed signed coordinates into a numeric key with possible overflow and collisions. It now uses tuple keys and checks coordinate conversion limits.
- Iterative static solves accepted non-convergence, and topology solves did not check solve status. Failures and non-finite solution vectors are now rejected; fully prescribed systems bypass empty factorization.
- A previous Netgen error could mask a later static-solver error. Current solver failures now take precedence, and new meshing attempts clear prior solver errors. Netgen calls are serialized in native code because its initialization and STL buffers are global.

Remaining follow-ups:

- `sim_netgen.cpp` allocates an STL geometry with `Ng_STL_NewGeometry` and cannot release it through the installed public nglib C API, which has no STL geometry destructor. Inspection of the installed Netgen source confirms that mesh deletion uses a no-op geometry deleter. Repeated meshing therefore still retains geometry allocations. A proper fix needs a dependency-side destruction entry point or a tested integration with Netgen's C++ ownership API; deleting an opaque pointer would be unsafe.
- Geometry `Shape` handles still use raw `IntPtr` across P/Invoke. The null checks handle already-disposed shapes, but concurrent explicit disposal during another geometry call remains a lifetime risk. A broader `SafeHandle` migration should cover every geometry entry point together.
- C entry points generally cannot verify the allocated length behind a raw pointer. Direct native callers must still supply correctly sized buffers and live handles. The new length checks cover the public managed simulation wrapper.

Validation: Windows Release native build, managed kernel build, seven direct native regression tests, twelve managed boundary checks (including real Netgen meshing), and the existing surface suite's 318 checks passed. No sanitizer run or cross-platform build was performed. Tests are in `tests/test_native_safety.py` and `tests/Prism.NativeSafetyTests`; they use freshly built Release DLLs. The surface suite also regenerates its existing STEP output in `docs/surfacing-enhancements`.
