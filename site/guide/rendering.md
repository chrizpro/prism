# Rendering and materials

The **Render** workspace makes realistic pictures of what is on screen: soft shadows, reflections, and studio or sky light. It is built in, with nothing extra to install. Reference: [Render](/tools/render), [Materials](/tools/materials).

<GuideImage src="/images/guides/lens-render.png" alt="A rendered camera lens with a black barrel, red ring and blue glass" caption="A product rendering made in Prism, showing glass, metal, reflections and a ground shadow." :width="1920" :height="1080" />

## Materials

Open **Materials** from the Appearance section. The **Materials** ribbon appears while the studio is open.

1. Select a body.
2. Pick a library material and **Apply Material**, or choose a finish: Matte, Satin, Glossy, Brushed metal or Polished metal.
3. **Compare** shows materials side by side; **Find Assigned** selects bodies that use one.
4. **Done** closes the studio.

Library materials carry physical data too, so the same assignment is used by simulation and mass properties.

### Make and save your own look

Select a body, then choose **Generic appearance** in the Appearance tab. This starts with white, nonmetallic color and **No pattern**, ready for your bitmap. Under **Image textures**, import a color image; optionally add roughness and bump images. Adjust placement, roughness, metallic response and bump strength. Bump gives the shading depth without changing the geometry.

Expand **My appearances**, enter a name and choose **Save as appearance…**. Your named looks show **· Custom** and are saved on this computer for reuse across documents and restarts. Select one and press **Apply** to apply it to selected bodies. The assigned engineering materials stay the same. Removing a saved entry leaves appearances already applied to documents intact.

### Save a custom engineering material

In the Materials library, expand **My custom materials**. Choose **Create custom material…** to start with blank fields for your own isotropic properties, or select a source material and choose **Duplicate selected material…** to copy its values. Enter a name, edit properties in the displayed units and optionally record a source. Save it, then use **Apply Material** to assign it. Copies retain the source strength basis and solver limitations. **Cancel** discards the draft and returns to the library without saving or assigning it.

Custom materials show **· Custom**, with **User-defined — not verified by Prism** in their details. Their properties and provenance are saved with document assignments. The reusable library stays on this computer; deleting an entry does not change existing assignments. Built-in materials retain their individual evidence status, including reference estimates.

<GuideImage image="materials-studio" />

## Lights

**Sun** adds a global sun with a direction, illuminance in lux and colour temperature. **Lamp** and **Spot** add point and spot lights with power in lumens and real distance falloff. Drag a light's dot in the viewport to move it; <kbd>Ctrl</kbd> keeps it level, and a spot's second dot aims it. **Show Lights** toggles their markers.

For exact control, render with **Only my lights**.

## Render

1. Orbit to the view you want.
2. Open **Render Image** and choose the picture size, quality (samples per pixel), light bounces, lighting, background (white, black, green screen, studio gradient or transparent) and an optional ground plane with shadows.
3. Press **Start render**. The picture appears over the viewport and sharpens as it goes; **Stop** whenever it looks good enough. Higher quality means less grain and takes longer.
4. **Save Picture** (to `Documents\Prism\Renders` unless you choose a folder), then **Close Picture**.

<GuideImage image="render-image-settings" />

**Render Animation** renders an animation to video; MP4 output needs ffmpeg.

## AR

[AR and sharing](/guide/ar) exports GLB and USDZ for phones and can serve them on your Wi-Fi.
