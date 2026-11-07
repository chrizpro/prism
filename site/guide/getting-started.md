# Your first Prism workspace

Prism brings sketches, solids, surfaces, and freeform modeling into one desktop application. Start in the **Design** workspace and use the **Home** ribbon for everyday actions.

<GuideImage src="/images/guides/app-compact.png" alt="Prism Design workspace with a rounded plate and five holes" caption="The ribbon is at the top, the model browser is on the left, and the design history runs along the bottom." :width="1500" :height="920" />

## Choose a folder

On the start screen, choose a workspace folder. This is where new parts are saved and where Prism lists your models. You can choose your own location or use `Documents/Prism`. Existing workspace paths are preserved when upgrading.

## Make a first part

1. Choose **New part**.
2. On **Home**, open **Create** and choose a **Box**, or choose **Create Sketch** under **References** to draw a profile on a plane.
3. Finish the sketch, then open **Create → Features → Extrude** to give the profile depth.
4. Inspect the preview and confirm the command in the tool panel.
5. Press **Ctrl+S** to save a `.prism` document.

Use **Ctrl+Z** to undo. Press **Home** to fit the model in the viewport, and **N** to look straight at a selected face or plane. **Ctrl+K** opens command search, including tools inside ribbon menus.

<GuideImage image="solid-extrude-preview" />

<GuideImage image="solid-extrude-result" />

## Pick a workspace

**Design** contains the main modeling ribbons. **Scan** concentrates on meshes and reverse engineering. **Assemble**, **Render**, **CAM**, **CAE**, **ECAD**, and **Script** expose their own task-specific tools. Home and View remain available in each workspace.

The [tool catalog](/reference/ribbons) is generated from Prism's ribbon definitions. It includes contextual Sketch and Materials tools as well as regular ribbons.
