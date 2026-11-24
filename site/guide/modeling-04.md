# Modeling changes in 0.4

This visual guide covers the changes on **v0.4-work**, based on source revision `1ae0e1c`. These are development-branch features, not a claim that a 0.4 installer has been released. The pictures below are explanatory diagrams; existing screenshots elsewhere in the guides may show the earlier ribbon.

## Choose how to build a boundary surface

<GuideImage image="diagram-v04-boundary" />

Open **Boundary Surface** on Surface or NURBS. Leave **Mode** on **Automatic** to infer the construction, or choose the job explicitly:

| Mode | Pick |
| --- | --- |
| Fill boundary | A loop to fill |
| Edge: span two to four curves | Two to four free curves or sketch edges |
| Blend: bridge two surface edges | An edge on each of two surfaces |
| Network: outline and curves across | An outline and interior guide curves |

**Mode** chooses the construction. **Fill with** separately chooses automatic, flat or smooth filling. An explicit mode refuses incompatible inputs instead of silently choosing another job. Continue with the [surfacing walkthrough](/guide/surfacing#boundary-surface).

## Round edges and size bevels

<GuideImage image="diagram-v04-fillet" />

In **Fillet**, use **Radius** for the common value. To vary individual edges, type `3, 5-8, 2` in **Radius per edge (optional)**. Entries follow pick order: 3 mm on the first edge, 5 to 8 mm along the second, 2 mm on the third. Edges beyond the list use the common Radius. Setback corners are not available.

On Surface or NURBS, **Fillet / Chamfer** also accepts one edge from each of two separate, touching surfaces. They become one dressed surface. The branch uses a 0.05 mm joining tolerance; it refuses non-touching sheets and a mixed solid/surface pair. This is not a general bridge across a gap.

<GuideImage tool="fillet-chamfer" />

<GuideImage image="diagram-v04-chamfer" />

In solid **Chamfer**, choose **Style**: Equal distance, Two distances, or Distance and angle. **Swap the two sides** changes which side receives each value. See [Solid modeling](/guide/solid#modify).

## Stitch sheets, or remove a seam

<GuideImage image="diagram-v04-sew" />

In **Sew**, **Stitch** keeps each face. **Merge into one patch** also tries to merge neighboring faces on the same underlying surface. Two coplanar halves can become one face; a sharp crease remains. If merging cannot be done, the faces stay stitched. It does not refit arbitrary tangent patches into a new smooth surface.

<GuideImage tool="sew" />

<GuideImage image="diagram-v04-keep" />

Sew's **Keep the originals** is off by default. Turn it on to retain the input sheets alongside the result. **Thicken** similarly offers **Keep the original surface**, off by default. Hide the retained sources if overlapping geometry makes the result hard to inspect.

**Intersection Curve** has **Keep the original curves** for its two-flat-curve mode. It replaces those curves by default; planes, faces and bodies remain references and are never removed. Older saved intersection steps keep their curves. Check the option rather than assuming that every tool has the same default.

## Split one face, or every face the cut crosses

**Split Face** has an **Extent** choice. **Selected face** divides only the face you picked. **Through all** lets you pick any one face of the body and divides every face that the plane, movable plane or sketch crosses, all the way through. The body stays one solid of the same shape, so you can then push, pull or color each part. A sketch is carried straight through the body along its own plane. An edge cannot cut through all, because it has no direction; pick a plane or a sketch instead.

## Mark a solid face with curves

<GuideImage image="diagram-v04-imprint" />

Start **Split Face**, choose **Mode → Imprint**, and pick the face and curves or sketch. A closed circle can mark out a disc face without cutting material away. A line ending inside the face adds an edge without enclosing a new face. Check the preview before confirming.

<GuideImage tool="split-face-direct-modeling" />

Imprint is implemented for **solids** in this branch. For surface sheets, use [Split Surface](/guide/surfacing#split-a-surface-into-faces) or Draw on Surface's split result. Wrap is not available.

## Make a helix without writing a formula

<GuideImage image="diagram-v04-helix" />

1. Open **Curves → Curve from Data** and choose **Points from → Helix**.
2. Choose **Axis** X, Y or Z. Set **Helix radius**, **Pitch (rise per turn)**, **Turns** and **Handedness**.
3. Leave **End radius (0 = same)** at 0 for a constant radius, or enter another radius for a taper. Position with Move X, Y and Z.
4. Confirm the curve. To make a spring-like body, create a closed section and use the helix as the path in **Sweep**.

For example, pitch 10 mm and 5 turns give 50 mm of rise along the axis. This makes a path; it does not automatically create thread geometry or a thread standard.

<GuideImage tool="curve-from-data" />

## Change a sweep's section size

<GuideImage image="diagram-v04-sweep" />

For a simple taper, set **Scale at the start** and the end scale. For a varying section, enter `0:1, 0.5:1.6, 1:0.7` in **Scale along the path (optional)**. Each pair means **position : scale**; positions run from 0 to 1, and scales must be positive. The smoothly joined table replaces the start and end scales.

<GuideImage tool="sweep" />

The second rail still steers orientation only. Stretching the section to guide rails and a twist law are not implemented. These controls describe exact Solid/Surface/NURBS Sweep, not Freeform's cage sweep.

## Draft and analysis shortcuts

<GuideImage image="diagram-v04-draft" />

**Draft → Parting line → At the neutral plane** makes walls above and below the plane lean in opposite directions. A positive angle narrows both ends away from the plane. Faces are assigned by their centers: **split a wall at the plane first** if it crosses the plane. Per-face Shell thickness is not available.

<GuideImage image="diagram-v04-analyze" />

On **Surface** and **NURBS**, the **Analyze** dropdown brings together **Zebra Stripes**, **Curvature Map**, **Draft Analysis** and **Check Seam**. Use the existing [inspection images](/guide/surfacing#continuity) to understand the results. Curvature Graph is not in this dropdown. Match Surface still requires compatible natural patch boundaries; arbitrary trimmed-edge matching is unfinished.

## Where did the curve buttons go?

Curve, Blend Curve, Join, Explode, Trim Curve, Extend Curve and Offset Curve are hidden from the default GUI, search and customization in this branch. Their legacy commands remain for scripts and saved files. Sketch's own tools are unchanged. Use sketches for interactive curve drawing and **Curve from Data** for equations, tables and helices. Extract Edges, Rebuild and Points Along Curve remain available; older docs may call the last two extraction tools Duplicate Edge and Divide.

## Source and verification

The [canonical modeling change record](https://github.com/chrizpro/prism/blob/v0.4-work/docs/modeling/surface-and-nurbs/README.md#0-4-modeling-gaps-approved-by-the-owner-2026-11-14) separates owner-approved scope, delivered subsets and open decisions. Maintained tests include [Boundary modes](https://github.com/chrizpro/prism/blob/v0.4-work/Prism.Tests/Headless/Surfacing/BoundaryModeTests.cs), [surface fillets](https://github.com/chrizpro/prism/blob/v0.4-work/Prism.Tests/Headless/Surfacing/FilletAcrossSurfacesTests.cs), [dress-up](https://github.com/chrizpro/prism/blob/v0.4-work/Prism.Tests/Headless/Solid/DressUpDepthTests.cs), [Imprint](https://github.com/chrizpro/prism/blob/v0.4-work/Prism.Tests/Headless/Solid/ImprintTests.cs), [helix](https://github.com/chrizpro/prism/blob/v0.4-work/Prism.Tests/Headless/Curves/HelixTests.cs) and [sweep scaling](https://github.com/chrizpro/prism/blob/v0.4-work/Prism.Tests/Headless/Sweep/SweepScaleLawTests.cs).

The source guides record earlier Windows headless runs. This documentation task also executed the five examples captured above in an isolated Windows desktop instance. These are documentation captures, not maintained acceptance-test runs. The concept diagrams explain construction; only pictures labeled Prism result capture are actual viewport screenshots. [Capture method and provenance](https://github.com/chrizpro/prism/blob/codex/v0.4-illustrated-docs/docs/engineering/tool-reference-images.md).
