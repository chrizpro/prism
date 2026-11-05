// Generated API reference pages. Data comes from scripts/sync-api.py (run it with Prism open).
import cadkit from '../data/cadkit.json' with { type: 'json' }
import tools from '../data/automation-tools.json' with { type: 'json' }
import commands from '../data/commands.json' with { type: 'json' }
import { catalog, commandFor, slug, toolUrl } from '../tools/catalog-model.mjs'
import { accepts, cell, text } from '../tools/render.mjs'


// Short summaries for cadkit functions whose source has no docstring.
const notes = {
  get: 'Look up one object by name and return it as an `Obj`.',
  state: 'The full document state: objects, selection, active command, sketch, camera and status messages.',
  undo: 'Undo the last `count` steps.',
  redo: 'Redo the last `count` undone steps.',
  delete: 'Delete the given objects (names or `Obj`).',
  select: 'Replace the selection with the given objects, faces or edges.',
  save: 'Save the document to `path` (`.prism`).',
  box: 'Create a box and return it. Extra keyword arguments set other Box settings, such as `position_x`.',
  cylinder: 'Create a cylinder and return it.',
  sphere: 'Create a sphere and return it.',
  cone: 'Create a cone and return it.',
  torus: 'Create a torus and return it.',
  revolve: 'Revolve a sketch or face about an axis (`"World Z"` by default).',
  sweep: 'Sweep a profile along a path.',
  loft: 'Loft through ordered sections, optionally along guide rails.',
  chamfer: 'Chamfer edges by `distance`. Edges are given like `fillet()`.',
  shell: 'Hollow a solid, removing `faces`, with the given wall `thickness`.',
  union: 'Join `tools` into `body`.',
  subtract: 'Cut `tools` out of `body`.',
  intersect: 'Keep only where `body` and `tools` overlap.',
  move: 'Move objects by `x`, `y`, `z` millimetres.',
  rotate: 'Rotate objects by `angle` degrees about a world axis.',
  scale: 'Scale objects by `factor`.',
  mirror: 'Mirror objects across a world plane (`"XY"`, `"XZ"` or `"YZ"`).',
  flat_pattern: 'Unfold a sheet metal body into its flat pattern.',
  move_face: 'Move faces by `x`, `y`, `z` and optionally tilt them; the body follows.',
  thicken: 'Give a surface a thickness to make a solid.',
  cnc_pocket: 'Clear the inside of closed outlines to `depth`, leaving islands.',
  cnc_drill: 'Drill the picked holes to `depth`.',
}

const sections = [
  ['Connection', ['CadError', 'connect', 'call', 'tools']],
  ['Document', ['Obj', 'objects', 'get', 'state', 'undo', 'redo', 'delete', 'select', 'save', 'view', 'screenshot', 'history', 'show']],
  ['Dialogs', ['ask', 'dialog']],
  ['Any command', ['run', 'commands', 'describe', 'help']],
  ['Find and check geometry', ['faces', 'edges', 'check']],
  ['Sketches', ['sketch']],
  ['Primitives', ['box', 'cylinder', 'sphere', 'cone', 'torus']],
  ['Features', ['extrude', 'revolve', 'sweep', 'loft', 'fillet', 'chamfer', 'shell', 'holes', 'rib', 'thicken', 'split_body']],
  ['Combine and transform', ['union', 'subtract', 'intersect', 'move', 'rotate', 'scale', 'mirror', 'linear_pattern', 'circular_pattern']],
  ['Direct edits', ['push_pull', 'move_face', 'delete_face']],
  ['Sheet metal, layouts and drawings', ['flange', 'flat_pattern', 'flatten', 'refold', 'wrap', 'drawing', 'image', 'edit_image']],
  ['Freeform', ['form_box', 'form_cylinder', 'form_loft', 'form_pipe', 'form_info', 'form_op', 'form_to_nurbs']],
  ['Manufacturing', ['cam_setup', 'cnc_profile', 'cnc_pocket', 'cnc_adaptive', 'cnc_drill', 'cnc_face', 'cnc_rough', 'cnc_finish', 'cnc_post', 'cnc_design_check', 'cnc_stock_review', 'fdm_check', 'resin_check', 'print_export', 'resin_export']],
]

function docBlock(doc) {
  // Docstrings use reST ``code``; show it as Markdown code.
  return text(doc).replace(/``(.+?)``/g, '`$1`')
}

function pythonPage() {
  const by = new Map(cadkit.map(e => [e.name, e]))
  const listed = new Set(sections.flatMap(([, names]) => names))
  const rest = cadkit.filter(e => !listed.has(e.name)).map(e => e.name)
  const lines = ['# Python reference', '',
    '`cadkit` is the Python library that ships with Prism. It needs only the standard library. Parameters are written in snake_case and matched to on-screen labels by partial text, so `cad.box(width=40)` sets **Width** and `cad.flange(bend_radius=3)` sets **Bend radius (inside)**. Objects can be passed by name or as the `Obj` a call returned.', '',
    '```python', 'import cadkit as cad', '```', '',
    'Every function that makes something goes through [`run()`](#run), so anything missing here can still be reached with `cad.run("command_name", ...)`. See the [command reference](/api/commands) for every command name.', '']
  for (const [title, names] of [...sections, ...(rest.length ? [['Other', rest]] : [])]) {
    lines.push(`## ${title}`, '')
    for (const name of names) {
      const e = by.get(name)
      if (!e) continue
      lines.push(`### ${e.name} {#${slug(e.name)}}`, '')
      if (e.kind === 'class') {
        lines.push('```python', `class ${e.name}`, '```', '', docBlock(e.doc), '')
        if (e.members?.length) {
          lines.push('| Member | Description |', '| --- | --- |')
          for (const m of e.members) lines.push(`| \`${m.property ? m.name : m.signature}\` | ${cell(m.doc.replace(/``(.+?)``/g, '`$1`')) || (m.property ? 'Property.' : '')} |`)
          lines.push('')
        }
        continue
      }
      const sig = e.decorators?.includes('contextmanager') ? `with cad.${e.signature}:` : `cad.${e.signature}`
      lines.push('```python', sig, '```', '', e.doc ? docBlock(e.doc) : notes[e.name] ?? '', '')
    }
  }
  return lines.join('\n')
}

function schemaRows(schema) {
  const props = schema?.properties ?? {}
  const required = new Set(schema?.required ?? [])
  return Object.entries(props).map(([name, p]) => {
    const type = p.type === 'array' ? 'array' : p.type ?? 'any'
    return `| \`${name}\`${required.has(name) ? ' <Badge type="warning" text="required" />' : ''} | ${type} | ${cell(p.description ?? '')} |`
  })
}

const toolGroups = [
  ['Inspect the document', ['get_state', 'object_info', 'get_log', 'screenshot', 'wait_idle', 'solid_find', 'solid_check']],
  ['Commands and the ribbon', ['list_commands', 'describe_command', 'run_command', 'command_set', 'command_commit', 'command_cancel', 'list_ribbon', 'ribbon_click', 'marking_menu', 'edit_feature']],
  ['Objects and selection', ['select', 'object_update', 'delete_objects', 'origin']],
  ['Sketching', ['sketch_begin', 'sketch_plane', 'sketch_add', 'sketch_state', 'sketch_constrain', 'sketch_dimension', 'sketch_set_dimension', 'sketch_project', 'sketch_mirror', 'sketch_trim', 'sketch_pattern', 'sketch_delete', 'sketch_input', 'sketch_finish']],
  ['Freeform and NURBS', ['form_info', 'form_find', 'form_analyze', 'form_op', 'form_select', 'form_to_nurbs', 'get_control_points', 'set_control_points']],
  ['Viewport and input', ['view', 'pointer']],
  ['Documents, files and history', ['file', 'document_new', 'document_list', 'document_switch', 'document_close', 'history', 'history_group', 'history_view', 'history_tree', 'history_goto', 'batch', 'workspace']],
  ['Materials, rendering and drawings', ['material_list', 'material_apply', 'materials_panel', 'render_control', 'drawing_export']],
  ['Meshes', ['mesh_import', 'mesh_export', 'mesh_region']],
  ['Simulation and workflows', ['study_new', 'study_run', 'study_state', 'cae_state', 'workflow_state']],
  ['Scripts and the assistant', ['script_dialog', 'assistant']],
]

function automationPage() {
  const by = new Map(tools.map(t => [t.name, t]))
  const listed = new Set(toolGroups.flatMap(([, n]) => n))
  const rest = tools.filter(t => !listed.has(t.name)).map(t => t.name)
  const lines = ['# Automation tools', '',
    `Prism exposes ${tools.length} automation tools. Call them from Python with \`cad.call("name", **args)\`, over [HTTP](/api/http) with \`POST /call\`, or from an AI assistant through the [MCP bridge](/api/mcp) (\`call_api\`). Sizes are millimetres with Z up. Face and edge indices are 1-based, like the labels in the app; Freeform cage indices are 0-based.`, '']
  for (const [title, names] of [...toolGroups, ...(rest.length ? [['Other', rest]] : [])]) {
    const present = names.filter(n => by.has(n))
    if (!present.length) continue
    lines.push(`## ${title}`, '')
    for (const name of present) {
      const t = by.get(name)
      lines.push(`### ${t.name} {#${slug(t.name)}}`, '', text(t.description), '')
      const rows = schemaRows(t.inputSchema)
      if (rows.length) lines.push('| Argument | Type | Description |', '| --- | --- | --- |', ...rows, '')
      else lines.push('No arguments.', '')
    }
  }
  return lines.join('\n')
}

// Where a command's ribbon button is documented, if it has one.
const buttonFor = (() => {
  const map = new Map()
  for (const tab of catalog.tabs) for (const g of tab.groups) for (const tool of g.tools) {
    const c = commandFor(tab.title, tool.label, g.title)
    if (c && !map.has(c.name)) map.set(c.name, { label: `${tab.title} › ${g.title} › ${tool.label}`, url: toolUrl(tab.title, g.title, tool.label) })
  }
  return map
})()

function commandsPage() {
  const lines = ['# Command reference', '',
    `All ${commands.length} modeling commands accepted by \`run_command\` and \`cad.run()\`. Each lists its selection slots (what to pick) and parameters. Labels match by partial, case-insensitive text, and commands fill empty slots from the current selection. Some commands add further inputs once started; run them with \`commit: false\` to see those.`, '',
    '```python', 'import cadkit as cad', 'print(cad.describe("fillet"))          # slots and parameters as JSON', 'cad.run("fillet", slots={"Edges": [{"object": "Box 1", "edge": 3}]}, radius=2)', '```', '']
  const groups = new Map()
  for (const c of commands) {
    const prefix = c.name.includes('_') ? c.name.split('_')[0] : c.name
    const key = { cnc: 'CNC', cam: 'CAM', cae: 'CAE', sim: 'Simulation', mesh: 'Mesh', form: 'Freeform', curve: 'Curves', joint: 'Joints', pattern: 'Patterns', pcb: 'ECAD', motion: 'Motion', sketch: 'Sketch' }[prefix] ?? 'Modeling and general'
    if (!groups.has(key)) groups.set(key, [])
    groups.get(key).push(c)
  }
  const order = ['Modeling and general', 'Curves', 'Patterns', 'Freeform', 'Mesh', 'Simulation', 'CAE', 'Motion', 'Joints', 'CAM', 'CNC', 'ECAD', 'Sketch']
  for (const key of order.filter(k => groups.has(k))) {
    lines.push(`## ${key}`, '')
    for (const c of groups.get(key)) {
      lines.push(`### ${c.name} {#${slug(c.name)}}`, '', `**${text(c.title)}**${c.hint ? ` · ${text(c.hint)}` : ''}`, '')
      const button = buttonFor.get(c.name)
      if (button) lines.push(`In the app: [${text(button.label)}](${button.url})`, '')
      if (c.slots.length) lines.push(`Slots: ${c.slots.map(s => `\`${text(s.label)}\`${s.accepts ? ` (${text(accepts(s.accepts))}${s.multi ? ', several' : ''}${s.optional ? ', optional' : ''})` : ''}`).join(' · ')}`, '')
      const params = c.params.filter(p => p.type !== 'action')
      if (params.length) {
        lines.push('| Parameter | Type | Default |', '| --- | --- | --- |')
        for (const p of params) {
          const type = p.type === 'choice' && p.options?.length ? p.options.map(o => `\`${cell(o)}\``).join(', ') : `${p.type}${p.unit ? ` (${p.unit})` : ''}`
          lines.push(`| ${cell(p.label)} | ${type} | ${cell(p.default ?? '')} |`)
        }
        lines.push('')
      }
      const actions = c.params.filter(p => p.type === 'action')
      if (actions.length) lines.push(`Actions: ${actions.map(a => `\`${text(a.label)}\``).join(' · ')}`, '')
    }
  }
  return lines.join('\n')
}

export default {
  paths() {
    return [
      { params: { ref: 'python' }, content: pythonPage() },
      { params: { ref: 'automation' }, content: automationPage() },
      { params: { ref: 'commands' }, content: commandsPage() },
    ]
  },
}
