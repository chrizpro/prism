# Mold manual test checklist

See the [feature guide](../../modeling/mold/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Import a small exact part via **Part** from a STEP file, then repeat from another open tab. Confirm the intended reference bodies appear.
- [ ] Use **Create Mold** with a block that fully contains the part. Confirm two halves, a visible cavity, and the expected split direction and explode distance.
- [ ] Try a block that only partially encloses the part. Confirm the tool explains the problem and leaves the model unchanged.
- [ ] Save and reopen the mold. Verify the halves are present and any imported reference part is omitted as described in the guide.
