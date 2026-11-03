# Right-side studios

The sidebar now has a compact selector rail for Properties, Appearance, Diagnostics, References and Parameters. Studio panels use a 340px content area plus the rail; the ordinary inspector retains its previous overall width. Modeling commands and sketch editing take priority over studios. Opening a studio does not abandon an active operation. The script editor yields to an explicitly selected studio and returns when it closes.

## Diagnostics

Run checks explicitly to evaluate existing B-rep validity, sketch constraint status and captured feature input availability. Sketch checks operate on clones. Results are marked stale after document changes. A structured failure record connects a stopped feature rebuild to the original failed feature; its original saved inputs can be opened for inspection. Locate selects and frames an affected object; sketch issues open the real sketch editor. Checks and Activity tabs keep validation results distinct from historical session messages. Activity follows the active document only.

These checks do not certify surface continuity, manufacturing readiness, or general self-intersection freedom. They are synchronous, explicitly requested checks; large documents may take time to inspect.

## References

The panel reuses actual origin planes/axes/point, sketches, curves, construction planes and image-plane objects. It provides name search, visible-only filtering, visibility switches, selection/framing, reference origins/normals, and captured feature usage on the current branch. Editable sketches and construction features open their existing editors. Imported references without a recipe cannot be reconstructed into an editable feature. Origin references are fixed. Create construction plane opens the existing plane command.

## Parameters

Document values support names, pinning, arithmetic, functions from the existing expression parser, and references such as `rib = wall * 0.75`. Length literals accept `mm`, `cm`, and `inch`; `deg` is accepted for angle literals. Names are case-insensitive. Cycles, missing names, non-finite results and duplicate/reserved names are rejected. Units are scalar conversions, not a full dimensional-analysis system.

Edit document formulas in the draft, evaluate them, then apply. Removing a parameter used by a current-branch feature is blocked. Feature tab lists current-branch recipes and their dimensional numeric inputs. Enter a literal or a formula using saved document values, then update the feature. The full feature editor remains available for selections, integer counts, switches and other settings. A bound formula is shown in the standard numeric editor; typing a different literal detaches it.

Applying document changes validates all bound values against feature limits before touching the history. It rebuilds from the first affected feature, re-evaluating downstream bindings. Existing branches remain available. If native geometry rebuilding fails later, the session stops at the last successful result, reports the failed feature and preserves the complete original branch.

### Parametric sketches

Sketch dimensions can carry a **name** and a **formula**, so the Parameters panel drives sketches as well as features. Set them in the sketch (double-click a dimension, or the dimension's row in the Sketch panel) or in the panel's **Sketches** tab:

- Type a number to fix a dimension, an expression such as `wall * 2` to drive it, or `width = wall * 2` to name and drive it in one go.
- Names never collide between sketches. Inside a formula, `width` means this sketch's own dimension called `width`, and if there is none, the document parameter `width`. `sketch1.width` is the `width` of the sketch called "Sketch 1" (its name in lower case without spaces), so Sketch 1 and Sketch 2 can each have their own `width`. `doc.width` always means the document parameter, even when the sketch has a dimension with the same name.
- A sketch can use dimensions of sketches made before it. Loops (`a` uses `b` uses `a`) are rejected, and a name is unique within its sketch. `doc`, `t`, `pi`, `e`, `tau` and the unit names cannot be dimension names.
- Lengths are in mm, angles in degrees. Sizes must stay positive; a formula that gives zero or less is refused.
- Applying a change re-solves every affected sketch, then rebuilds the features built on them. Everything is checked first, so a bad formula, an out-of-range value or a sketch whose constraints cannot hold leaves the document untouched. Preview in the panel shows each dimension's resulting value before you apply.
- The panel lists, under a parameter's **Used by**, the sketch dimensions that use it.
- Renaming a sketch changes how other sketches must refer to it (`sketch1.` becomes the new name); formulas that still use the old name are reported when applied. Resizing a selection with the sketch gizmo replaces the formulas of the scaled dimensions with plain values.

Named parameter definitions are immutable history snapshots and are stored in `.prism` files along with final geometry. The existing file format still does **not** save construction history or feature recipes. Consequently feature bindings remain session-only and cannot be reconstructed after reopening, though the named definitions and final shapes survive. Old files without parameter definitions remain compatible.

## Verification and preview

- `dotnet run --project tests/Prism.StudioTests`: 50 checks covering real geometry rebuilds, formula validation, downstream failures, persistence, references, diagnostics, document isolation, theme layouts and the native selector rail.
- History regression: 41 checks; Appearance regression: 26 checks; material/file compatibility regression: 100 checks.
- `dotnet build Prism.Desktop/Prism.Desktop.csproj --no-restore -o artifacts/studios-preview`

The desktop preview is `artifacts/studios-preview/Prism.Desktop.exe`. The running application was left untouched. Dark/light panel captures and separate Feature-tab captures are saved in this directory.
