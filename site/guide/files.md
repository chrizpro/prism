# Files and compatibility

## Native documents

Use **Save As** to save editable work with the extension that matches the document. Prism suggests `.prism_prt` for a part, `.prism_asm` for an assembly and `.prism_dwg` for a drawing. Existing `.prism` and `.ourcad` documents still open. To make a general editable `.prism` copy while keeping your current document open, choose **Home → Export As → Prism document (.prism)**.

<GuideImage image="modeling-workspace-overview" />

## Exchange formats

Use **File → Exchange** to import geometry or meshes. To write a file, use **Home → Export As** and choose STEP, IGES, BREP, Sketch DXF, GLB, 3MF, STL, OBJ or PLY. Unavailable formats are grayed out. Hover over one to see what it requires.

Exchange formats carry different kinds of data. A mesh does not preserve an editable solid feature history, and imported CAD geometry does not recreate the original application's modeling history.

## Settings

Preferences and local support files live in `%LOCALAPPDATA%/Prism` on Windows. First launch imports existing support files from the previous product folder without removing the originals or overwriting existing Prism files. Saved workspace paths and recent files remain intact.
