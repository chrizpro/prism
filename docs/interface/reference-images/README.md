# Reference images

Reference pictures are embedded plane objects for tracing and measurement; they are not exported. Import supports PNG, JPG, BMP and GIF, a standard or picked plane, width, opacity, normal offset and horizontal mirroring. Edit Image adjusts width, opacity, position, rotation and mirroring. Set Scale picks two points and a real distance in mm, cm, m, inches or feet; it scales the picture uniformly about the first point. It does not resize existing sketch geometry. See also [automation](../../MCP.md).

## Current fixes and verification

[Issue #68](https://github.com/chrizpro/prism/issues/68) tracks the reported missing import preview and scale failure. Import preview now supplies the picture texture to the viewport. Calibration selects a picture under the pointer even where an origin or construction plane overlaps it. The reported scale failure may have additional causes; desktop interaction and rendered preview remain to be verified.

[Headless image tests](../../../Prism.Tests/Headless/Images/ImageCalibrationTests.cs) exercise the two-point calculation, selection over an origin plane and import preview data. On 2026-11-05, `dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj -c Release --filter 'Feature=Images' --no-restore` passed three tests on Windows at base revision `b1d0a23846c821683c41d789b7e9fe52f76addf7` plus uncommitted local changes. Viewport rendering is not covered by those tests.

## Interactive placement and editing

The owner approved this scope on 2026-11-05 ([issue #69](https://github.com/chrizpro/prism/issues/69)). Following owner feedback, Import and Edit use the shared gizmo display and picking: choose **Move**, **Rotate** or **Scale** in the command's Gizmo selector, or press **T**, **R** or **S** with viewport focus. Arrows move in the picture plane; the ring rotates in that plane; the centre square scales uniformly about the centre. Shared gizmo display and snapping settings apply. The corresponding panel values update during dragging; cancel discards the preview and apply commits the change. Set Scale remains available for a known distance. The offset panel value controls normal-axis placement during import.

Normal-axis dragging and showing handles outside Import/Edit remain open decisions. The headless image tests include a pointer-driven move, rotate and resize and cancellation; actual viewport presentation still needs desktop verification.

## Proposed — awaiting owner review

The owner requested axis-specific scaling. Proposed: scale handles along the picture's U and V axes independently stretch its width and height about its centre; the centre handle scales both dimensions together. Store both dimensions with the image, preserving the proportions of existing saved documents, and retain uniform two-point calibration. Review this distortion behavior before implementation and tests; the current image data derives height from width and pixel aspect ratio.

The 2026-11-05 Windows combined Images/DocumentTabs/DocumentSafety headless run passed 16 tests, including four image tests, at base `b1d0a23846c821683c41d789b7e9fe52f76addf7` plus uncommitted shared-workspace changes. After the shared gizmo replacement, the same filtered command again passed 16 tests; the desktop Release build passed with an existing package warning. The tests exercise mode switching, pointer-driven move, rotate and uniform scale, edit commit and undo as well as cancellation; they do not render the viewport.

After the reported hotkey failure, the window event routing was corrected. On 2026-11-05 the same filtered run passed 18 tests including IMG-H005, which sends key events through the window, and the desktop Release build passed. This used the same base and uncommitted shared-workspace changes; no rendered desktop screenshot was captured.
