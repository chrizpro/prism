# ECAD integration

The **ECAD** workspace handles the mechanical side of electronics: boards, component envelopes, fit checks and STEP exchange. It has no electrical design tools. Reference: [ECAD ribbon](/tools/ecad).

## Make a board

- **Rectangular board** creates a board centered on the world origin from its width, length, thickness and bottom elevation, optionally with four mounting holes at an inset from the edges.
- **Board from outline** uses a closed sketch, so the board can follow any shape.
- **Mounting Hole** cuts a through-hole at the location you click on a flat board face. For a horizontal board, selecting the whole body lets you enter world X and Y.

Holes are mechanical; copper, plating and nets are not modeled.

<GuideImage image="ecad-board-envelopes" />

## Reserve space

**Component Envelope** places an approximate package or keep-out with a reference designator, body size, position, rotation, board side and clearance allowance. Use it for connectors, tall capacitors and anything the enclosure must clear. For accurate connectors, import their STEP models. Choose **Place on Board**, pick the component solid, then click the board face and set the rotation.

## Check the fit

Model the enclosure in Design. Use **Interference** for overlap, **Check Clearance** to compare a gap with a required distance, and **Section View** to inspect the inside.

## Exchange

**Import Board STEP** opens the mechanical geometry. **Export STEP** shows the exact bodies that will go in the file before you save.
