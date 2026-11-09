# Files and compatibility

## Native documents

Save editable work as `.prism`. Existing `.ourcad` documents remain readable and appear in file dialogs, workspace listings, and the parts library. You can open one and save a new `.prism` copy.

<GuideImage image="modeling-workspace-overview" />

## Exchange formats

Use **File → Exchange → Import** or **Export**. Supported geometry and mesh paths include STEP, IGES, BREP, DXF, STL, OBJ, PLY, and 3MF, with capabilities depending on the format. GLB export is also available.

Exchange formats carry different kinds of data. A mesh does not preserve an editable solid feature history, and imported CAD geometry does not recreate the original application's modeling history.

## Settings

Preferences and local support files live in `%LOCALAPPDATA%/Prism` on Windows. First launch imports existing support files from the previous product folder without removing the originals or overwriting existing Prism files. Saved workspace paths and recent files remain intact.
