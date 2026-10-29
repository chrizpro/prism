# Branching design history

The bottom history strip is now a Git-style graph. The original continuation stays on its lane; edits and new alternatives create additional lanes without discarding old snapshots. Lane positions remain stable when navigating. Horizontal and vertical scrolling support histories larger than the panel.

Use **Collapse / Expand** to switch between a 46-pixel compact trail and the full branch graph. The compact trail shows the active path, highlights the current step, dims redo steps, and marks checkpoints and forks. Scroll the mouse wheel over it to browse longer histories; click or right-click steps for the same navigation and actions as the full graph. The active step is revealed after navigation, resizing, or expansion. Expanded branch labels stay fixed during horizontal scrolling, and adding a new fork preserves the lanes of older alternatives.

Drag the thin top edge to resize the expanded graph. Expansion and height are remembered in user settings. **Checkpoint** names the current snapshot; **Branch here** asks for an alternative name and creates a branch snapshot. Clicking a node revisits that state. Hover highlights affected objects. Right-click a node to edit its feature or sketch, name a checkpoint, or restore an object. Double-click also opens an editable feature or sketch. The Browser's context menu exposes **Edit feature** for bodies with a captured recipe. **All steps** opens the existing history list. Automation can inspect or change the panel with `history_view` (`expanded`, `height`).

Feature edits restore captured selections and parameters in one batch. Updating an earlier operation replays the displayed continuation, remapping references to newly created bodies. Revisiting a historical feature and editing it rebuilds its preferred continuation. Cancel restores the exact pre-edit state.

Editing a sketch rolls back to its definition before dependent features. Finish Sketch stores the changed definition and rebuilds later features in order. The existing branch is kept. A rollback followed by a sketch edit only rebuilds as far as that rollback point, rather than resurrecting the dimmed future. Independent later sketches, property changes, and deletions replay without overwriting regenerated geometry.

If an operation cannot rebuild, the session reports the failed step, stops at the last successful result, and keeps the complete old branch available. Face and edge selections still use the kernel's topology indices; edits that change topology may require reselecting those references. Commands driven by interactive tools without replayable parameters remain non-editable.

## Current file-format limitation

The existing `.prism` format stores final geometry and sketch definitions, **not the construction history or command recipes**. This change provides editing and automatic rebuilds within the current document session; it does not introduce history serialization. Reopened legacy files cannot reconstruct feature dependencies from solids alone. Their sketches remain editable, but those edits cannot regenerate bodies whose recipes were never saved.

## Verification

Run from the repository root:

```
dotnet run --project tests/Prism.HistoryTests
dotnet run --project tests/Prism.RibbonTests
dotnet build Prism.Desktop/Prism.Desktop.csproj --no-restore -o artifacts/history-preview
```

The history suite runs real native geometry checks for sketch-to-extrusion-to-fillet rebuilds, depth editing, branch preservation, cancel, rollback, invalid downstream geometry, independent bodies, property changes, named alternatives, and a deep graph. Headless UI checks cover native node activation and collapse/expand at the minimum window size. It also renders `history-dark.png` and `history-light.png` for visual review.

The preview output directory allows building while another Prism instance locks the normal debug output. The existing running app is not stopped or replaced.
