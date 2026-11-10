# Joints manual test checklist

See the [feature guide](../../modeling/assembly/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Create a revolute joint through a shaft axis and drive it. Confirm the shaft rotates without translating.
- [ ] Before committing, check that the viewport labels identify the two picks and that the hinge axis passes through the pin. Pick the grounded part second and confirm the preview still identifies it as staying in place.
- [ ] Create a slider joint in a second sample and check that it moves along only the intended axis.
- [ ] Toggle the joint browser parent off and on. Confirm the joint graphics follow the toggle and verify that the joint still drives afterward.
