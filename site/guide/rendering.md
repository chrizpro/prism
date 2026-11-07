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
