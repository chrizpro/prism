# ECAD integration

The **ECAD** workspace handles the mechanical side of electronics: boards, component envelopes, fit checks and STEP exchange. It has no electrical design tools. Reference: [ECAD ribbon](/tools/ecad).

## Make a board

- **Rectangular board** creates a board centered on the world origin from its width, length, thickness and bottom elevation, optionally with four mounting holes at an inset from the edges.
- **Board from outline** uses a closed sketch, so the board can follow any shape.
- **Mounting Hole** cuts a through-hole along world Z at an X, Y position.

Holes are mechanical; copper, plating and nets are not modeled. Build boards lying in XY and rotate the finished assembly afterwards.

## Reserve space

**Component Envelope** places an approximate package or keep-out with a reference designator, body size, position, rotation, board side and clearance allowance. Use it for connectors, tall capacitors and anything the enclosure must clear. For accurate connectors, import their STEP models.

## Check the fit

Model the enclosure in the Design workspace, then run **Interference** to find where the board, envelopes and enclosure overlap.

## Exchange

**Import STEP** and **Export STEP** move boards and enclosures between Prism and your electronics tools.
