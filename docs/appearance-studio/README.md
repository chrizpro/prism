# Appearance studio

Canonical specification for visual appearance, engineering material defaults and imported render backgrounds. [Delivery issue #45](https://github.com/chrizpro/prism/issues/45); inspection coverage remains in [#37](https://github.com/chrizpro/prism/issues/37).

## Status and owner decisions

**Approved for implementation on 2026-11-05:** the owner instructed delivery after reviewing the appearance/material and gradient/weathering recommendations, including documentation, issues and tests. Implementation is present; verification below distinguishes executed checks from remaining gaps.

Material answers **what the part is made from**. Appearance answers **how its surface looks**. Render controls **lighting, camera and the resulting image**. Appearances remain unrestricted visual choices; curated finishes provide convenient starting points.

## Selection, defaults and overrides

- **APP-001:** appearance preserves material identity, pinned engineering properties, geometry and analysis inputs. Dirt/rust and bump are visual effects: they do not calculate corrosion, remove metal, add coating thickness, change mass or create machinable relief.
- Open **Materials** on the ribbon. Live edits apply to selected solid, surface and freeform bodies. Fields show the first supported selection's values; edits affect all supported selected bodies. No selection disables editing. Face-specific assignment and mesh objects are outside this workflow.
- Material assignment supplies its default look unless an explicit visual override exists. Overrides survive reassignment. Recognizable legacy overrides on assigned materials also survive. Old unassigned custom colors lack reliable metadata; editing their appearance once establishes an explicit override.
- **Reset finish** restores the current material/default appearance. Choosing a finish preset replaces visual layers with that preset. Both retain engineering data. The six preview swatches are illustrative drawings, not viewport renders.
- **APP-002:** multi-body changes and slider/wheel pointer gestures group live updates into one Undo step. Numeric/keyboard edits support Undo/Redo. Native `.prism` save/reopen retains layers and embedded images; files without additive fields retain their original finishes.
- While the studio is visible, selection edges remain and full-face selection tint is suppressed so actual colors can be judged.

## Compact color editor

**APP-003:** the full picker opens only from **Base color**. Finish presets and advanced sections start collapsed. Reset stays available while scrolling. Layout supports the existing 312px sidebar and desktop working panel.

The picker offers an HSV wheel, brightness, palette, eight recent colors per session and up to 24 saved colors in local preferences. Arrow keys adjust wheel hue/saturation. Exact entry accepts `#RRGGBB`, `RRGGBB`, `#RGB`, `rgb(255,128,0)`, `rgb(100%,50%,0%)`, `hsl(30,100%,50%)` and `hsv(30,100%,100%)`. HSL/HSV saturation and lightness/value use 0–100 with optional percent signs. Enter or focus loss applies the code; invalid input leaves the model unchanged. Colors are opaque sRGB; alpha entry is unsupported.

**Pick from screen** is available on Windows: freeze monitors in memory, magnify a pixel, click to apply, Escape to cancel. The click does not reach applications underneath. Captures are not saved. Sampling returns the displayed pixel, including shading/display effects, rather than recovering an object's original material color. Other platforms retain wheel/code editing and report screen sampling as unavailable. Mixed-DPI multi-monitor and HDR/color-managed display accuracy need further coverage.

## Surface layers

Roughness runs from polished (0) to matte (1), with a renderer floor of 0.06. Existing gloss remains compatible (`roughness = 1 - gloss`). Surface finishes retain From material, Plain, Wood grain, Brushed metal, Cast finish and Carbon weave, with millimeter size and world-Z grain rotation.

| Expandable section | Behavior |
| --- | --- |
| Color gradient | Solid, linear, radial, spherical and freeform. Base color is the first stop. Freeform blends four corner colors on the plane perpendicular to X/Y/Z. Size, offset and rotation control placement. |
| Weathering | Dirt or rust patches controlled by coverage, patch size and seed. Changes color, metallic response and roughness together. Optional bottom concentration has an independent fade height. |
| Image textures | Embedded color, roughness and grayscale bump/height images, repeated through blended box (triplanar) projection with millimeter size and rotation. Color maps multiply base/gradient color; use white for original image colors. White roughness is matte; black is polished. |
| Reflection and detail | Metallic response, bump strength, clear-coat amount and separate coat roughness. Without a bump image, bump uses procedural grain. CPU coat uses dielectric GGX reflection, base attenuation and matching mixture sampling; viewport reflection remains an inexpensive approximation. |

Layer order is base color → gradient → color image → weathering; roughness maps/weathering control roughness and bump perturbs normals. Clear coat sits above the result. Maps can be individually removed. Disabling a gradient/weathering retains its settings.

**APP-004:** layers require no UV editor and appear in viewport and CPU rendering. Imports are capped at 16 MB, 16384 pixels per dimension and 64 megapixels, then decoded to at most 2048 pixels on the longest side. PNG/JPEG/BMP/WebP support depends on the host decoder. Images are decoded once per material/render and cached with mipmaps on the GPU. Transparent pixels are composited on white. Roughness/height use scalar data without color gamma conversion.

### Placement limits

Coordinates are millimeters relative to each body's bounding-box minimum. Translation preserves placement. Rotation and geometry edits can change alignment: this is **not a persistent body-local texture frame**. Rotation turns about world Z before axis projection. Triplanar blending can soften transitions; precise logos/wraparound artwork need a future decal/UV workflow. Freeform means four smooth corner colors, not arbitrary gradient mesh topology or viewport handles.

## Imported render backgrounds

**APP-005:** Render offers **Imported image**, **Import background image…** and **Remove background image**. Images are embedded. Advanced placement appears for image backgrounds: Fill crops, Fit preserves proportions with white borders, Stretch fills without preserving aspect ratio. Offsets are fractions of output width/height.

Backgrounds are composited after exposure/tone mapping, independently of lighting/reflections. The optional ground catches shadows over them. Render Animation uses the stored image/placement when Imported image is selected. Missing images report a render error. Backplate import does not provide HDR environment lighting.

## Implementation and tests

- [Model](../../Prism/Core/SurfaceStyle.cs), [color entry](../../Prism/Core/ColorCode.cs), [view model](../../Prism/ViewModels/MainViewModel.Appearance.cs), [panel](../../Prism/Views/MaterialsPanel.axaml), [Windows sampler](../../Prism.Desktop/ScreenColorPicker.cs).
- [CPU layers](../../Prism/Rendering/SurfaceLayers.cs), [viewport shader](../../Prism/Rendering/Shaders.Surface.cs), [image decoding](../../Prism/Rendering/SurfaceImage.cs), [path tracer](../../Prism/Rendering/PathTracer.cs), [render command](../../Prism/Commands/RenderCommand.cs).
- Maintained [Unit tests](../../Prism.Tests/Unit/Appearance/SurfaceAppearanceTests.cs), [Headless tests](../../Prism.Tests/Headless/Solid/AppearanceStudioTests.cs), [desktop integration](../../Prism.Tests/Integration/DesktopAppearanceTests.cs), and [material assignment characterization](../../Prism.Tests/Headless/Solid/AppearanceTests.cs). Legacy `tests/` programs remain intact.
- Automation `material_apply` accepts exact color codes, roughness (exclusive with gloss), replacement `surface` data using `SurfaceStyle` JSON fields and `resetAppearance`. Surface enums use numeric values; maps are base64. Material-only assignment preserves overrides.

Run Unit and Headless projects with `--filter Feature=Appearance`. Desktop checks use the [isolated desktop/MCP runner](../../Prism.Tests/Integration/README.md) with the same filter.

## Verification evidence

2026-11-05, Windows workstation, .NET SDK 10.0.102, installed native libraries (not rebuilt):

| Command | Actual result |
| --- | --- |
| `dotnet test Prism.Tests/Unit/Prism.UnitTests.csproj` | 67 passed, no failures/skips; includes 18 appearance cases and representative coat energy/reciprocity integrations. |
| `dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj` | 222 passed, no failures/skips; includes six new appearance scenarios and existing material assignment characterization. |
| `dotnet run --project tests/Prism.MaterialTests` | 100 checks passed. |
| `dotnet run --project tests/Prism.RenderTests` | 70 checks passed. |
| `dotnet run --project tests/Prism.AppearanceTests` | 26 checks passed; light/dark panel captures at 312px. |
| `dotnet test Prism.Tests/Integration/Prism.IntegrationTests.csproj --filter Feature=Appearance` | APP-I001 passed, no failures/skips; a 16-sample 960 × 540 render was exported. |
| Desktop build and MCP build | Passed; pre-existing compiler/NuGet warnings remain. |

Final runs used `artifacts/tests/appearance-validation`, a detached validation checkout of base `8dcec2c` plus only this delivery's scoped files. This isolated concurrent, incomplete Drawing workspace edits in the shared checkout; those edits were preserved. Full shared-checkout validation is not claimed. Native `prism_kernel.dll` SHA256: `07D37FF1F5F7E9F7E008801A919703CF6F49364BC20329207B97576A74CB7B19`. Tested desktop `Prism.dll` SHA256: `0CB72202A9A653F845A8DE6A78F231B8F197FC105192AE6B39E68E0630BBECDA`.

The desktop `APP-I001` scenario verifies actual shader display, native save/reopen, embedded color/roughness/bump maps and rendered image-background composition. Reviewed evidence: [freeform gradient in viewport](APP-I001-freeform.png), [rust with metal exposed](APP-I001-weathering.png), [exported render with embedded maps and image background](APP-I001-render.png), [dark panel](appearance-dark.png), [light panel](appearance-light.png). The exported image is a 16-sample draft, not a denoised production render. Interactive review separately exercised live wheel changes, a screen-sampled pixel and expanded-panel layout. The permanent source revision is recorded on [issue #45](https://github.com/chrizpro/prism/issues/45). Historical results are not live test status.

## Remaining scope

The approved rendering follow-up adds HDR environment lighting, final denoising, explicit texture projection/offsets, glass/refraction and directional reflections. See the [Product rendering guide](../rendering/README.md) and [issue #60](https://github.com/chrizpro/prism/issues/60) for current controls, implementation limits and verification. The evidence above records the original #45 delivery.

Full UV editing, arbitrary gradient meshes, per-face appearance, tangent-space normal maps, automatic cavity/edge wear, displacement geometry and subsurface scattering remain future scope. A plausible appearance does not certify an engineering material or manufacturing process.
