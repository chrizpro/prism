import type { ImageMetadata } from 'astro'
import appCompact from '../assets/shots/app-compact.png'
import sketch from '../assets/shots/sketch.png'
import nurbsDuct from '../assets/shots/nurbs-duct-vp.png'
import lensRender from '../assets/shots/lens-render.png'
import bottle from '../assets/shots/freeform-bottle-vp.png'
import cae from '../assets/shots/cae-stress.png'

export type Feature = {
  slug: string
  name: string
  kicker: string
  title: string
  lede: string
  image?: ImageMetadata
  alt?: string
  accent: string
  tabs: string[]
  docs: string
  highlights: { title: string; text: string }[]
}

export const features: Feature[] = [
  {
    slug: 'modeling',
    name: 'Solid modeling',
    kicker: 'Parametric solids',
    title: 'Precise parts with a history you can change.',
    lede: 'Sketch a profile, extrude or revolve it, then add holes, fillets, shells and drafts. Every step lands on the design history, so you can reopen any feature, change a number and watch the later steps rebuild.',
    image: appCompact,
    alt: 'Prism Design workspace with a filleted plate, four holes and the design history along the bottom.',
    accent: '#4088ED',
    tabs: ['Home', 'Solid', 'Direct Modeling', 'Sheet Metal', 'Analyze'],
    docs: '/guide/solid',
    highlights: [
      { title: 'Editable features', text: 'Double-click a step in the history to reopen it with the same picks and numbers. Later steps rebuild when you confirm.' },
      { title: 'Holes, shells and drafts', text: 'Hole, Shell, Draft, Rib, Emboss and Split Body sit beside Fillet and Chamfer in one Modify section.' },
      { title: 'Direct editing', text: 'Push, replace, match and split faces on any solid, including imported STEP geometry without a feature history.' },
      { title: 'Sheet metal', text: 'Build flanges with real bend radii, then unfold a flat pattern and save it as DXF.' },
      { title: 'Patterns', text: 'Linear, circular, along a curve, fill a region or drive copies from a table.' },
      { title: 'Engineering drawings', text: 'Make Drawing creates hidden-line views, sections, details, callouts and a title block as SVG, PDF or DXF.' },
    ],
  },
  {
    slug: 'sketching',
    name: 'Sketching',
    kicker: 'Constraint sketcher',
    title: 'Sketches that know what you meant.',
    lede: 'Draw on any plane or face with lines, arcs, slots, polygons, splines and text. Add constraints and dimensions, and Prism shows the remaining degrees of freedom as you work.',
    image: sketch,
    alt: 'The Sketch ribbon with constraints, a rectangle with three circles, and the sketch status panel showing degrees of freedom.',
    accent: '#39C8ED',
    tabs: ['Sketch'],
    docs: '/guide/sketching',
    highlights: [
      { title: 'Live degrees of freedom', text: 'Blue geometry can still move; white geometry is fully constrained. Drag it to test what is still free.' },
      { title: 'Type while you draw', text: 'Dimensions appear beside the pointer. Type a value, press Tab for the next one and Enter to place.' },
      { title: '2D and 3D', text: 'Switch a sketch to 3D to draw curves in space for sweeps, pipes and rails.' },
      { title: 'Project and include', text: 'Bring edges from existing bodies into the sketch as references or construction.' },
    ],
  },
  {
    slug: 'surfacing',
    name: 'Surfaces & NURBS',
    kicker: 'Precision surfacing',
    title: 'Flowing surfaces, checked for every reflection.',
    lede: 'Loft, sweep and fill boundaries, then push control points directly. Match neighbouring surfaces for tangent or curvature continuity and check the result with zebra stripes, curvature maps and curvature graphs.',
    image: nurbsDuct,
    alt: 'A lofted NURBS duct transitioning from a round outlet to a rectangular flange.',
    accent: '#7762ED',
    tabs: ['Surface', 'NURBS'],
    docs: '/guide/surfacing',
    highlights: [
      { title: 'Profile-driven surfaces', text: 'Extrude, Revolve, Sweep, Pipe, Loft and Boundary Surface work from sketches, curves or edges.' },
      { title: 'Control point editing', text: 'Edit Points, Sculpt, Rebuild Surface and Warp reshape a surface while keeping it smooth.' },
      { title: 'Continuity', text: 'Match Surface joins neighbours with G0, G1 or G2 continuity; Check Seam reports the gap and angle.' },
      { title: 'Finish to a solid', text: 'Trim, Sew and Thicken surfaces into a closed body ready for manufacturing.' },
    ],
  },
  {
    slug: 'freeform',
    name: 'Freeform',
    kicker: 'Subdivision modeling',
    title: 'Shape products like clay, finish them as CAD.',
    lede: 'Start from a box, cylinder or loft and pull a coarse cage into the shape you want. Prism keeps a smooth subdivision surface under the cage and converts it to exact NURBS when the form is right.',
    image: bottle,
    alt: 'A freeform detergent bottle with a handle, modeled in Prism.',
    accent: '#B5F2FF',
    tabs: ['Freeform'],
    docs: '/guide/freeform',
    highlights: [
      { title: 'Cage editing', text: 'Extrude, inset, bevel, bridge and insert edge loops. Soft selection moves nearby vertices with a falloff.' },
      { title: 'Symmetry', text: 'Mirror and weld across X, Y, Z or radial symmetry; topology edits repeat on the mirrored side.' },
      { title: 'Creases and sharp edges', text: 'Crease edges and vertices for controlled transitions without adding geometry.' },
      { title: 'Check and convert', text: 'Check Quality finds poles, triangles and stretched faces. Convert to NURBS produces an exact solid.' },
    ],
  },
  {
    slug: 'simulation',
    name: 'Simulation',
    kicker: 'Built-in CAE',
    title: 'Know it holds before you make it.',
    lede: 'Fix a face, apply a load and run a study without leaving the model. Prism meshes the part, solves and colours the stress, displacement and factor of safety on the same body you designed.',
    image: cae,
    alt: 'A von Mises stress result on a T-bracket with a colour legend and the simulation panel.',
    accent: '#FF7A59',
    tabs: ['Static', 'Thermal', 'Dynamics', 'Motion', 'Fluids'],
    docs: '/guide/simulation',
    highlights: [
      { title: 'Static structures', text: 'Fixed, roller and directional supports; force, pressure, torque and gravity loads; stress, displacement, safety and fatigue.' },
      { title: 'Thermal and modal', text: 'Steady and transient conduction, and natural frequencies, solved with CalculiX.' },
      { title: 'Motion', text: 'Joint a mechanism, add springs and actuators, drive it and feed the loads back into a structural study.' },
      { title: 'Generative design', text: 'Set supports and loads and let Prism find the lightest stiff shape, delivered as a new mesh body.' },
      { title: 'Fluids', text: 'Pipe networks with reduced-order flow, plus a basic 3D flow study for slow, smooth flow with streamlines and pressure drop.' },
    ],
  },
  {
    slug: 'manufacturing',
    name: 'Manufacturing',
    kicker: 'CAM and printing',
    title: 'From model to machine without a hand-off.',
    lede: 'Set up stock, choose tools and generate CNC toolpaths, then play them back and post G-code. Preparing a print instead? Check walls and overhangs, orient, repair and export for your FDM or resin slicer.',
    accent: '#F5B83D',
    tabs: ['Subtractive', '3D FDM', '3D Resin', 'Tooling'],
    docs: '/guide/manufacturing',
    highlights: [
      { title: 'CNC operations', text: 'Profile, Pocket, Adaptive, Drill, Face, Turning, 3D Rough and 3D Finish with a shared tool library.' },
      { title: 'Review', text: 'Playback, pass view, operation order, stock review and a printable setup sheet.' },
      { title: 'FDM and resin checks', text: 'Overhang, wall thickness and mesh quality checks; hollowing and drain holes for resin.' },
      { title: 'Export', text: 'Post G-code for your machine, or export 3MF and STL for the slicer you already use.' },
    ],
  },
  {
    slug: 'assembly',
    name: 'Assembly & animation',
    kicker: 'Bottom-up assemblies',
    title: 'Put the parts together and watch them move.',
    lede: 'Insert parts from your workspace library, connect them with rigid, revolute, slider, cylindrical, ball and planar joints, and drive the mechanism to check for interference. Animate an exploded view and render it to video.',
    accent: '#4ADE80',
    tabs: ['Assemble', 'Joints', 'Animate'],
    docs: '/guide/assembly',
    highlights: [
      { title: 'Parts library', text: 'Every part in your workspace folder is ready to insert, with part numbers, revisions and costs.' },
      { title: 'Joints and couples', text: 'Six joint types plus couples for gears and screws. Drive Joints sweeps the motion.' },
      { title: 'Parts list', text: 'A bill of materials straight from the assembly, kept in step with the parts.' },
      { title: 'Exploded views', text: 'Animate the assembly apart and render the sequence.' },
    ],
  },
  {
    slug: 'rendering',
    name: 'Rendering & AR',
    kicker: 'Presentation',
    title: 'Pictures that sell the design.',
    lede: 'Apply physically based materials from the library, light the scene with sun, lamps and spots, and render studio images. Then export to AR and look at the part on your desk, at real size, from your phone.',
    image: lensRender,
    alt: 'A studio render of a camera lens in black anodised aluminium, rubber, polished steel and coated glass.',
    accent: '#EC4899',
    tabs: ['Render', 'Materials'],
    docs: '/guide/rendering',
    highlights: [
      { title: 'Material library', text: 'Metals, plastics, woods and finishes from matte to polished, with physical data used by simulation too.' },
      { title: 'Lights', text: 'Sun, lamp and spot lights you can place and edit in the viewport.' },
      { title: 'AR export', text: 'GLB and USDZ at real size or tabletop scale, shared to a phone on the same network.' },
      { title: 'Animation output', text: 'Render animations and exploded views to video.' },
    ],
  },
  {
    slug: 'scan',
    name: 'Scan & mesh',
    kicker: 'Reverse engineering',
    title: 'Turn a scan back into a part.',
    lede: 'Import a mesh or point cloud, clean and repair it, then fit planes, cylinders and freeform surfaces to the regions you pick. Check the deviation between the rebuilt CAD and the scan in colour.',
    accent: '#22D3EE',
    tabs: ['Mesh', 'Reverse Engineer'],
    docs: '/guide/scan',
    highlights: [
      { title: 'Mesh repair', text: 'Repair, reduce, remesh, smooth, offset, split and boolean triangle meshes.' },
      { title: 'Fit and rebuild', text: 'Segment & Fit, Fit Surface to Region and Rebuild from Scan create exact geometry.' },
      { title: 'Section sketches', text: 'Slice the scan on a plane to start a sketch from the real outline.' },
      { title: 'Deviation map', text: 'Colour the rebuilt body by its distance from the scan.' },
    ],
  },
  {
    slug: 'ecad',
    name: 'ECAD integration',
    kicker: 'Electronics enclosures',
    title: 'Boards and enclosures that fit the first time.',
    lede: 'Create a board from a rectangle or an outline, add mounting holes and component envelopes, and check the enclosure for interference. Exchange the result as STEP.',
    accent: '#A3E635',
    tabs: ['ECAD'],
    docs: '/guide/ecad',
    highlights: [
      { title: 'Boards', text: 'Rectangular boards or boards from a sketched outline, with thickness and mounting holes.' },
      { title: 'Component envelopes', text: 'Reserve space for connectors and tall parts with clearance on any side.' },
      { title: 'Fit checks', text: 'Interference between the board, components and the enclosure.' },
      { title: 'STEP exchange', text: 'Import and export boards with your electronics team.' },
    ],
  },
  {
    slug: 'automation',
    name: 'Scripting & AI',
    kicker: 'Automation',
    title: 'Anything you can click, you can script.',
    lede: 'Write Python in the Script workspace beside the model, or from your own editor. The same automation API lets AI assistants drive Prism through the Model Context Protocol, with every change undoable.',
    accent: '#39C8ED',
    tabs: ['Script'],
    docs: '/api/',
    highlights: [
      { title: 'cadkit for Python', text: 'Friendly functions like box(), sketch(), extrude(), fillet() and loft(), plus run() for any command.' },
      { title: 'One undo step', text: 'Wrap a script in cad.history() and undo the whole thing at once.' },
      { title: 'Dialogs', text: 'cad.ask() turns defaults into a form so a script becomes a reusable tool.' },
      { title: 'MCP bridge', text: 'Let an AI assistant inspect, model, simulate and screenshot through a local, token-protected endpoint.' },
    ],
  },
]

export const feature = (slug: string) => features.find(f => f.slug === slug)
