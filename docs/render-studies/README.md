# Material and render exercise

Tested October 27, 2026 using the real Prism MCP bridge and the app's built-in CPU path tracer. These PNGs are actual app renders.

## Saved scenes and results

- `azure-material-render.prism`: freeform detergent bottle with HDPE body and polypropylene cap, custom colours and gloss. `azure-hdpe-studio.png` is the 1280 x 720 studio render at 1,024 samples and 5 bounces, with ground and shadows.
- `N01-material-render.prism`: NURBS mouse with aluminum 6061-T6 housing and dark ABS wheel/base. `mouse-aluminum-studio.png`: 1280 x 720, 512 samples, 5 bounces, studio lighting, white background, no ground; completed in 27.84 seconds on this machine. `mouse-aluminum-transparent.png`: same scene, 256 samples, transparent background; completed in 14 seconds.
- `N02-material-render.prism`: earlier NURBS bottle study with HDPE body and polypropylene cap. Initial draft/128-sample PNGs preserve the pre-fix white-background behavior. Its exaggerated silhouette is already present in the source loft; the renderer faithfully reproduces it. This is a separate loft-fairness issue, not a render distortion, and reinforces the need for continuity/fairness diagnostics from the NURBS assessment.
- Original model documents remain at their previous paths. Material variants were saved separately.

## Fixes

1. White camera backgrounds previously passed through exposure/tone mapping, exporting gray (approximately RGB 230) instead of white. White is now composited after tone mapping. Actual exported background pixels verified as RGBA 255,255,255,255.
2. Transparent antialiasing multiplied pixel coverage twice, darkening partially covered silhouette pixels. Covered radiance is now resolved before tone mapping and premultiplied once. PNG saving still converts premultiplied BGRA to straight RGBA.
3. MCP lacked render status and lifecycle controls. Added `render_control` with `status`, `stop`, `save`, and `close`; `get_state` now also includes progress/completion/error information. Start through the existing Render command. Detailed `object_info` now exposes material identity, metal and gloss for solids/surfaces/forms.
4. Saving before any completed samples now reports failure, rather than writing the initial empty buffer. Saving creates missing parent folders. Background render failures are retained on the job and cancellation cannot mark a canceled callback completed.
5. Removed the unrelated modeling Preview checkbox from Render and Render Animation command panels.

## Verification

`dotnet run --project tests/Prism.RenderTests`: 17 checks passed. Covers material/colour/finish persistence, render completion, material colour in rendered pixels, exact white, transparent coverage and consistent compositing, saving to a new directory, stop/close, and failed save with no render. Release desktop build passed.

Live checks exercised material assignment, save/reopen, progressive completion, PNG export, document switching while rendering, MCP stop/close and rejected save without a render. The HD PNGs have the expected dimensions. The transparent mouse PNG contains alpha values from 0 to 255 and 1,413 partially covered pixels. See `png-validation.json`, `*-status.json`, and `mcp-render-session.jsonl` for evidence.

The renderer remains a simple CPU path tracer: diffuse surfaces and shadows retain sampling noise at modest sample counts, and there is no denoiser. Library materials currently supply colour, metal, gloss, and engineering properties; transparent PNG backgrounds do not imply that glass/refraction/transmission materials are implemented.
