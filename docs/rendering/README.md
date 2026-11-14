# Product rendering

Canonical guide for the CPU renderer, HDR environment lighting, final denoising, transparent surfaces and directional reflections. [Delivery issue #60](https://github.com/chrizpro/prism/issues/60). Surface styling and engineering-material separation remain in the [Appearance studio guide](../appearance-studio/README.md).

## Status and scope

Owner approved materials-first organization and consistent progress on 2026-11-11 ([#126](https://github.com/chrizpro/prism/issues/126)). Render follows **Materials → Lighting → Camera → Render → Output**. Materials are directly accessible first; Save Picture and Close Picture are in Output. Rendering uses the shared [operation progress strip](../interface/operation-progress.md), including real sample/frame fractions and cooperative Stop. Render workspace also offers Solid, Direct Modeling and Analyze for geometry corrections and inspection.

**Owner approved proceeding on 2026-11-05** after reviewing priorities: HDR lighting, denoising and better texture placement, then transmission/refraction and directional metal reflections. The approved scope is implemented and verified below. Further rendering features remain outside this delivery.

## Approved behavior

- **RND-001:** an embedded Radiance HDR environment supplies linear-light illumination, reflections and optional camera background. Rotation and intensity are independent of exposure and ordinary image backplates. Missing/invalid environments report an actionable error. Existing studio/sky/lights-only presets remain available.
- **RND-002:** optional final denoising uses geometry/material guides and sampling variance to reduce noise. It preserves alpha and composited backgrounds and does not alter the raw path-tracing accumulation. It can be disabled for comparison. This is a built-in spatial filter, not an external AI denoiser or a guarantee of recovered detail.
- **RND-003:** appearance maps offer triplanar, planar and cylindrical placement, axis, millimeter offsets and repeat/clamp controls. Defaults preserve existing documents. This improves placement without a UV editor; persistent body-local attachment under every modeling operation is not claimed.
- **RND-004:** transparent appearance supports Fresnel reflection, IOR/refraction, total internal reflection, rough interfaces and distance-dependent absorption. These are visual parameters and preserve engineering properties. Correct solid refraction requires closed, consistently oriented surfaces; thin/open sheets and intersecting media have limits that must be documented.
- **RND-005:** anisotropic GGX reflection has strength and direction controls. Zero strength retains isotropic behavior. Brushed pattern markings and directional reflections are independent controls.
- Picture settings and material parameters survive native document save/reopen. Appearance edits retain grouped Undo and reset behavior. Advanced UI remains collapsible. Old documents retain opaque material behavior by default.

## Delivery limits

Radiance `.hdr`/`.rgbe` environment import is in scope; EXR and external denoiser dependencies are not. No spectral dispersion, participating-media scattering, caustic-focused integrator, full UV editor, or claim of production-renderer parity. The viewport is an interactive approximation; CPU rendering is the reference for refraction and final lighting.

## Using the controls

### Lighting and image quality

In **Render**, choose **HDR environment** under Lighting, expand Advanced, then use **Import HDR environment…**. HDR rotation turns the panorama around world Z; intensity scales its linear radiance. Exposure changes the resulting picture rather than the light intensity. Choose Environment as the background to see the panorama, or retain a white/transparent/image backdrop while using the HDR for lighting and reflections. Existing lamps remain active. Remove HDR environment returns to Studio.

Imports support Radiance RGBE Y/X scanlines, modern RLE and flat/legacy runs, including reversed X/Y orientations. Limits: 32 MB, 8192 × 4096 dimensions, 16 megapixels; linear radiance is reduced to at most 2048 × 1024 for sampling. Radiance must be finite and at most 1,000,000 per channel. XYZE/EXR and arbitrary axis-order headers are unsupported. HDR color is interpreted as linear RGB. Source exposure/color-correction metadata is not applied separately. Ordinary LDR backplates remain display-space images and do not light the scene.

**Denoise final image** is optional and off by default. Progressive passes remain raw; the completed image receives three geometry/variance-guided spatial passes before tone mapping and background compositing. Original accumulation and alpha are unchanged. Fractional silhouette coverage and pixels without reliable primary guides remain unfiltered. Geometry and color-difference weights protect material boundaries and reflection/refraction details, but a low-sample image can still lose fine detail or retain noise. Increase samples for difficult reflections, glass and caustic paths. This filter is not temporally stable animation denoising.

### Backgrounds and AI generation

The Background choice offers None, White, Transparent, HDR environment and Imported image. **Import background image…** loads a PNG, JPG, BMP or WEBP file (16 MB maximum, embedded base64 in the document) with a Fill/Fit/Stretch placement.

**Generate with AI…** sits next to the import action and opens the assistant with a staged background prompt. The workflow needs file access (the prompt says so when it is off); with it on, the assistant — Codex is the default provider — writes a PNG into `%LOCALAPPDATA%\Prism\generated` via `run_script` and applies it with `render_setup action=set_background`, which goes through the same `SurfaceImage.Import` validation as a hand-picked image and switches Background to Imported image. No SVG output is used. For external agents, the `render_setup` tool covers the same ground: `set` changes background/environment/exposure/HDR/denoise/camera, `set_background` assigns an image file, `clear_background` returns to the environment. `render_control` continues to poll a running render and save the PNG.

### Texture placement

In Appearance → Image textures choose **Blended box**, **Planar**, or **Cylindrical** and the projection axis. Set texture size in millimeters, rotation about world Z, XYZ offsets, and height/width aspect. **Center projection on selection** places the projection origin at each selected body's bounding-box center. Repeat image tiles the map; disabling it clamps to its border pixels. A cylinder wraps once around its chosen axis; texture size/aspect set its axial repeat height. Color, roughness and bump share placement so their features align.

Placement remains relative to the body's bounding-box minimum. Translation preserves placement, but rotation and shape edits can require adjustment. No UV editor, automatic seam placement or topology-aware attachment is implied. Clamping repeats the edge pixel rather than adding transparent borders. Color-image alpha remains composited on white; the decal layer keeps its alpha instead (see the Appearance studio guide).

### Glass and transparent surfaces

Appearance → **Glass and transparent surfaces** offers Clear glass and Frosted glass starting points, transmission, IOR, and **Tint at reference depth** with a millimeter reference depth. White tint absorbs no light; colored tint becomes stronger on longer paths through the solid. Surface roughness controls frosting. Metallic suppresses transmission; the glass presets set Metallic to zero. These presets are grouped/undoable and retain the engineering material snapshot. Newly assigning the library's soda-lime glass supplies a transparent default; older documents without surface-transmission fields retain their saved opaque appearance until reassigned/reset or edited.

The tracer tracks closed dielectric boundaries with Fresnel reflection, total internal reflection, Snell refraction and Beer absorption. Smooth interfaces use discrete specular transport; rough interfaces use GGX reflection/transmission with matching PDFs. A medium stack supports properly nested closed solids up to 16 levels. Intersecting/non-nested media and inconsistent face orientation are unsupported. Transmission through an open sheet does not infer missing thickness. The viewport shows a studio approximation, not scene refraction. Camera backplates are not refractive scene geometry; transparent PNG backgrounds do not make glass alpha physically compositable over arbitrary photographs.

Direct-light shadow rays still treat intervening geometry as blockers; specular light through glass is reached by path tracing. Sharp caustics and complex glass illumination can therefore be noisy or dark at modest samples. This is not a caustic-focused renderer.

### Directional metal reflection

Appearance → Reflection and detail adds anisotropy (0–0.95) and brush direction. This changes the GGX reflection distribution, independently of the Brushed surface pattern. Direction is measured in the surface tangent frame built from world axes, not a UV/tangent map. The viewport approximates the directional highlight; final rendering evaluates anisotropic masking, scattering and the corresponding sampling PDF. Anisotropy zero retains isotropic behavior.

## Implementation and maintained tests

- [HDR decoding/sampling](../../Prism/Rendering/HdrEnvironment.cs), [denoiser](../../Prism/Rendering/RenderDenoiser.cs), [surface scattering](../../Prism/Rendering/SurfaceScattering.cs), [path tracer](../../Prism/Rendering/PathTracer.cs).
- [Surface data](../../Prism/Core/SurfaceStyle.cs), [map projection](../../Prism/Rendering/SurfaceLayers.cs), [viewport shader](../../Prism/Rendering/Shaders.Surface.cs), [render panel](../../Prism/Commands/RenderCommand.cs), [appearance controls](../../Prism/Views/MaterialsPanel.axaml).
- [Unit math/decoder/filter checks](../../Prism.Tests/Unit/Rendering/RenderingQualityTests.cs), [Headless physical-render/persistence checks](../../Prism.Tests/Headless/Rendering/RenderingQualityTests.cs), [desktop HDR/material gallery](../../Prism.Tests/Integration/DesktopRenderingTests.cs). All link back here. Existing appearance and legacy executable tests remain intact.
- [`render_setup` automation coverage](../../Prism.Tests/Headless/Assistant/AssistantToolTests.cs) (`AST-H006`, `AST-H007`) — state, `set`, image-path validation. A successful PNG decode runs through `SurfaceImage.Import`, which needs the desktop bitmap pipeline; it is exercised by the desktop render flow.

Run each maintained project with `--filter Feature=Rendering`. The desktop scenario follows the [Integration runner](../../Prism.Tests/Integration/README.md) and saves both raw and denoised PNGs in its artifact folder. [Appearance tests](../../Prism.Tests/Headless/Solid/AppearanceStudioTests.cs) continue to cover engineering separation and grouped changes.

## References

Scattering and sampling are based on standard microfacet/light-transport equations, with original Prism implementation: [PBRT infinite area lights](https://pbr-book.org/4ed/Light_Sources/Infinite_Area_Lights), [dielectric BSDF](https://pbr-book.org/4ed/Reflection_Models/Dielectric_BSDF), [microfacet theory](https://pbr-book.org/4ed/Reflection_Models/Roughness_Using_Microfacet_Theory). These references do not establish implementation verification.

## Tests and evidence

Verified on the Windows workstation on 2026-11-05:

| Check | Result |
| --- | --- |
| Complete Unit project | 76 passed; includes nine RND unit cases. |
| Complete Headless project | 228 passed; includes six RND headless cases. |
| Desktop integration, Rendering and Appearance | RND-I001 and APP-I001 passed; actual native viewport and exported renders reviewed. |
| Legacy Material / Render / Appearance programs | 100 / 70 / 26 checks passed. |
| Desktop and MCP builds | Passed with existing compiler/NuGet warnings, including NU1903 for Tmds.DBus.Protocol 0.21.2. |

Final runs used `artifacts/tests/render-validation`, a detached checkout of base `fe9cc17` plus this delivery's scoped files. Concurrent Drawing, reporting and native source edits in the shared checkout were preserved and excluded. The installed native binary was used, not rebuilt: `prism_kernel.dll` SHA256 `07D37FF1F5F7E9F7E008801A919703CF6F49364BC20329207B97576A74CB7B19`. Tested desktop `Prism.dll` SHA256: `C01DAA9735599A072716E2476FE2DC28F5DF113CB7DFDCA28375A4CB91BB467E`. No cloud/native CI validation is claimed. The permanent source revision is recorded on [issue #60](https://github.com/chrizpro/prism/issues/60).

RND-I001 renders directional steel, tinted glass and coated red paint under an original generated studio HDR at 960 × 540, 32 samples and eight bounces. Compare [raw render](RND-I001-raw.png) and [final denoised render](RND-I001-denoised.png); these are draft sample counts, not a quality benchmark. [Viewport evidence](RND-I001-viewport.png) shows the interactive approximation. Sidebar checks retain the compact panel with advanced sections collapsed in [dark](panel-dark.png) and [light](panel-light.png) themes. Denoising reduces noise but does not remove all glass/caustic noise or guarantee preserved fine detail.

Combined-source verification on 2026-11-05 passed 86 Unit and 592 Headless tests with rebuilt native libraries. See the [release integration report](../engineering/release-readiness-2026-11-05.md) for desktop results and source revisions.
