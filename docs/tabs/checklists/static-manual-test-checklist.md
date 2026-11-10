# Static manual test checklist

See the [feature guide](../../cae/static-analysis/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Set up a simple cantilever with a fixed end, a known material, and a load at the free end. Check the load and support indicators before meshing.
- [ ] Run the study and inspect Displacement. Confirm the fixed end stays still and the free end moves in the load direction.
- [ ] Probe the same spot on two mesh sizes and compare the results. Save and reopen the study to check the setup persists.
