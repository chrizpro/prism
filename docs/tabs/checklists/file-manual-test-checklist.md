# File manual test checklist

See the [feature guide](../../interface/document-safety/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Open a disposable `.prism` part, edit it, and use **Save As**. Confirm the save dialog suggests `.prism_prt`. Repeat with an assembly and a drawing and verify the suggested suffixes.
- [ ] In **Home → Export As**, confirm that a solid without a sketch enables STEP and STL and grays out Sketch DXF. Hover over Sketch DXF to see what it requires.
- [ ] Export a `.prism` copy of an edited part. Confirm the original stays open with its unsaved edits. Reopen the copy and confirm the edits and history are present.
- [ ] Close a dirty document and choose Cancel. Confirm the document stays open. Repeat with a copy and check that Discard leaves the saved file unchanged.
- [ ] Export a known-size solid as STEP, import it into a new document, and measure its dimensions.
