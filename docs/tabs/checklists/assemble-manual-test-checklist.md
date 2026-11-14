# Assemble manual test checklist

- [ ] Create a part, build geometry, then Finish part & return. Confirm the component is inserted once and Assemble is active. Check Grounded / Jointed / Free badges and modeling tabs.

See the [feature guide](../../modeling/assembly/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Insert two components and move one by a known distance. Measure their separation and confirm the browser names match the parts.
- [ ] In the **Joints** tab, open **Rigid**, choose **As Positioned**, and click two components in the viewport. Confirm the preview identifies both parts and that neither shifts when you create the joint. Save and reopen to verify the connection and position.
- [ ] Run Interference on an overlapping pair, then move them apart and rerun.
- [ ] Create an exploded view and return to the assembled view. Save and reopen to check both the saved layout and component positions.
