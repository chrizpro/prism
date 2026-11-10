# Solid manual test checklist

See the [feature guide](../../modeling/solid/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Extrude a 20 × 10 mm closed rectangle by 5 mm. Measure the solid and confirm its volume is 1,000 mm³.
- [ ] Cut a plain hole through the solid and check it in Section. Undo the hole and confirm the original volume returns.
- [ ] Edit the extrusion from the history, then save and reopen. Confirm the new depth and dependent geometry are present.
