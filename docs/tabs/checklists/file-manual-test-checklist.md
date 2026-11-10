# File manual test checklist

See the [feature guide](../../interface/document-safety/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] On a disposable document, create a box, save, edit, and use Save As. Reopen both files and confirm each has its own geometry.
- [ ] Close a dirty document and choose Cancel. Confirm the document stays open. Repeat with a copy and check that Discard leaves the saved file unchanged.
- [ ] Export a known-size solid as STEP, import it into a new document, and measure its dimensions.
