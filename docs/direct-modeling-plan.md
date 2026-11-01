# Direct modeling: plan

Status: Phases 0 to 3 are implemented, with the Phase 4 drag handle, hover preview and right-click menu. The user guide is [direct-modeling.md](direct-modeling.md). Not built yet: threaded hole conversion in Change type, curved or multi-face fillets in Resize, and tangent-case limits in Move Body.

Goal: you point at geometry, Prism works out what it is (a hole, a slot, a fillet, a pattern of them), and every
sensible action is one click or one key away. No history, works on imported parts.

## What went wrong in the first real use (50-slot plate)

| Observation | Cause (to confirm) | Fix |
|---|---|---|
| Slow | Pattern lookup and the edit rebuild make a kernel call per face, with an O(n²) coaxial search; every parameter change redoes the booleans for all picked features, one at a time | Phase 0 |
| Unclear what to select | The tool wants a round face; a slot is two flats plus two half-round ends, so clicking a slot wall gets "Pick the round face" | Phase 1 |
| Wanted it to just detect the hole | Selection is face-level only; recognition happens after the pick and the extra faces are not even highlighted | Phase 1 |
| Could move a hole but not delete it | Delete Face exists but is not discoverable, and there is no Delete key or right-click action | Phase 2 |

## Typical direct-modeling feature set (as found in commercial direct and synchronous modeling tools)

Mark what Prism has today (Y), partly has (~), or lacks (-).

**Selection intelligence** (face finders and selection filters)
- Select a whole hole / slot / boss / pocket / fillet chain from one click: -
- Same radius, same size, coplanar, coaxial, tangent chain, offset, symmetric: ~ (same size and pattern only, after the pick)
- Pattern selection (line, grid, circle): ~ (Move Hole etc. only)
- Box / lasso select faces, selection filters: ~

**Move and shape faces**
- Push / Pull (flat faces, round faces, holes, bosses): Y
- Move face with tilt: Y
- Replace face (match to plane or face): Y
- Offset face (all faces of a feature by one distance): -
- Rotate face about an axis: ~ (tilt only)
- Extend / trim a face to meet another: -
- Match face (take another face's shape): -

**Edit features**
- Move / resize hole or boss: Y (round only; no slots)
- Delete face with heal (remove hole, fillet, pocket, boss): Y, hard to find
- Change fillet radius or chamfer size on an existing round: - (only "Fillet" to add one)
- Remove all fillets / defeature by size: -
- Convert hole type (plain, counterbore, countersink, threaded): -
- Fill / cap a hole: ~ (Delete Face does it)
- Edit a recognised pattern (count, spacing, delete one instance): -
- Copy / paste or mirror a feature to another place: -

**Face and body operations**
- Split face, merge faces, imprint: -
- Draft, shell, split body, thicken, offset: Y
- Combine (union, subtract, intersect): Y
- Scale body, move body with kept relationships (stay tangent / coaxial): -
- Heal / repair imported geometry (gaps, missing faces, sliver faces): -

**Interaction**
- Mini-toolbar and right-click menu on the selection: -
- Delete key on selected faces or features: -
- In-viewport drag handle with typed value: ~ (Push / Pull arrow only)
- Live preview that stays responsive on large parts: -

## Phases

### Phase 0: make it fast (do first; everything else is unusable otherwise)
1. Profile Push/Pull, Move Hole and Delete Face on the 50-slot plate and note where the time goes (kernel calls, booleans, mesh rebuild).
2. Cache a per-body face catalog (type, radius, axis, centre, size key) keyed on the body's version, so selection, pattern lookup and edit all reuse it. Today it is rebuilt on every call.
3. Replace the O(n²) coaxial search with one pass that groups faces by (radius, axis).
4. Batch the booleans: fuse or cut all hole tools in one operation instead of one boolean per feature.
5. Debounce live preview while typing or dragging; show a busy indicator and keep the previous result on screen.
Done when: a 50-feature edit previews in well under a second on the user's machine.

### Phase 1: smart selection (fixes "what do I click?")
1. A feature recogniser over the face adjacency graph: hole (through, blind, counterbore, countersink), slot, boss, pocket, rib, fillet / chamfer chain.
   Needs a face-adjacency query from the kernel: check that one exists, otherwise add it to `native/kernel.cpp`.
2. Clicking any face of a recognised feature picks the whole feature; hover highlights it first so the user sees what they will get.
3. A selection-intent bar in the direct tools with chips: Feature, Face, Tangent chain, Coplanar, Coaxial, Same size, Pattern. Replaces the "Also edit" dropdown and defaults to Feature.
4. Highlight every face that was added by recognition (the current version does not), with a count in the panel.
5. Plain-language errors that say what to click ("That is a slot wall; click it again to select the whole slot").

### Phase 2: one feature-aware edit command
1. "Edit Feature" replaces Move Hole / Boss: for the selected feature it offers Move, Resize (diameter; slot length and width), Delete, Copy, Change type. The panel only shows what applies to what was picked.
2. Delete key and right-click / mini-toolbar on any selection: Delete (heal), Move, Resize, Offset, Copy.
3. Fillet / chamfer selected: Change radius, Remove.

### Phase 3: fill the gaps (priority order)
1. Offset Face, Rotate Face, Remove Fillets (by size or selection).
2. Edit Pattern: recognised pattern gets count, spacing and "remove instance" handles.
3. Copy / Mirror feature.
4. Split Face, Merge Faces, Fill / Cap.
5. Extend / Trim, Match Face.
6. Repair tools for imported parts: heal, sew, remove slivers.
7. Move body with kept relationships.

### Phase 4: interaction polish
In-viewport triad on the selection, drag to move, type to set an exact value, Tab to cycle fields, consistent hints in the panel.

### Phase 5: tests and docs
Automation tests per recognised feature type (hole kinds, slot, boss, pocket, fillet chain, line / grid / circle pattern with strays), a performance test on the 50-slot plate, and a docs page with before/after screenshots.

## Decisions needed
- Default selection behaviour: whole feature (recommended, the usual behaviour in direct-modeling tools) or single face.
- Whether "Add" and "Edit" stay as two ribbon groups once Edit Feature exists, or Edit collapses to a few big buttons plus the context menu.
- How far to take repair tools (Phase 3.6): basic heal only, or the full set.
