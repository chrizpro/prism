# Interface usability checklist

See [operation progress](../../interface/operation-progress.md), [assembly](../../modeling/assembly/README.md) and [rendering](../../rendering/README.md). Record build, task and observed behavior.

- [ ] Create a part from Assembly, build several features, then Finish part & return. Check the part inserts once and Assemble becomes active. Repeat while editing an inserted part.
- [ ] Try finishing an empty part or an invalid modeling command. Verify it stays open with an explanation.
- [ ] Confirm browser labels distinguish Grounded, Jointed and Free. A positioned Mate alone must not imply a persistent joint.
- [ ] Run and stop image/animation renders, Motion, Flow, Thermal/Modal and reconstruction. Check the shared strip, real/indeterminate stages and stopping behavior. Test a failing import/solver too.
- [ ] Run work in one document and switch to another. Return and check the source document's progress and final result.
- [ ] Check Mold → Inspect has Draft analysis, Section analysis and Check geometry; verify no separate Tooling tab is needed.
- [ ] Check frequently needed modeling tabs remain reachable in Assemble, Scan, CAE, CAM, ECAD, Mold and Render. Check smaller windows and both themes.
