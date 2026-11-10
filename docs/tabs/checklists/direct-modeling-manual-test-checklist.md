# Direct Modeling manual test checklist

See the [feature guide](../../modeling/direct-modeling/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Import a STEP block without feature history. Push or offset a planar face by a known amount and measure the new size.
- [ ] Move a hole or remove a fillet on an imported solid. Use Section or Check Geometry to inspect the edited area.
- [ ] Undo the edit, finish Direct Modeling, and verify that the previous tab and selection behavior return.
