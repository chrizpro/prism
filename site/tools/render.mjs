// Markdown helpers shared by the generated tool, ribbon and API pages.
import { slug } from './catalog-model.mjs'

// Text from the app goes into Markdown; keep it from being read as HTML or Vue.
export const text = s => String(s ?? '').replace(/</g, '&lt;').replace(/\{\{/g, '&#123;&#123;')
export const pipes = s => s.replace(/\|/g, '\\|')
export const cell = s => pipes(text(s)).replace(/\s+/g, ' ').trim()

const norm = s => (s ?? '').toLowerCase().replace(/[^a-z0-9]/g, '')

// The app names selection types after its object classes; say what a user would pick.
const kinds = { AnyCurve: 'curve or sketch', CurveObj: 'curve', SketchObj: 'sketch', SurfaceObj: 'surface', MeshObj: 'mesh', Form: 'freeform', AnyObject: 'any object' }
export const accepts = s => String(s ?? '').split(/,\s*/).filter(Boolean).map(k => kinds[k] ?? k.toLowerCase()).join(', ')

function value(p) {
  if (p.type === 'boolean') return p.default ? 'On' : 'Off'
  if (p.default === undefined || p.default === null || p.default === '') return ''
  return `${p.default}${p.unit ? ` ${p.unit}` : ''}`
}

function detail(p) {
  if (p.type === 'choice' && p.options?.length) return p.options.map(o => `\`${text(o)}\``).join(', ')
  const range = [p.min, p.max].every(v => v !== undefined && v !== null) ? `${p.min} to ${p.max}${p.unit ? ` ${p.unit}` : ''}` : ''
  return [p.type === 'integer' ? 'Whole number' : p.type === 'text' ? 'Text' : '', range].filter(Boolean).join(', ')
}

const pyKey = label => slug(label.replace(/\(.*?\)/g, '')).replace(/-/g, '_')

function pyValue(p) {
  if (p.type === 'boolean') return p.default ? 'True' : 'False'
  if (p.type === 'choice' || p.type === 'text') return JSON.stringify(p.default ?? '')
  return String(p.default)
}

export function scriptExample(cmd) {
  const slots = cmd.slots.filter(s => !s.optional).map(s => `"${s.label}": ${s.multi ? '[...]' : '...'}`)
  const params = cmd.params.filter(p => p.type !== 'action' && p.default !== undefined && p.default !== null && p.default !== '').slice(0, 2)
  const args = [`"${cmd.name}"`]
  if (slots.length) args.push(`slots={${slots.join(', ')}}`)
  for (const p of params) args.push(`${pyKey(p.label)}=${pyValue(p)}`)
  return ['```python', 'import cadkit as cad', '', `cad.run(${args.join(', ')})`, '```'].join('\n')
}

// "How to use", "Settings" and "Script it" sections for a tool backed by a command.
export function commandSections(cmd, tool) {
  const out = []
  if (cmd.hint && norm(cmd.hint) !== norm(tool.description)) out.push('::: info In the app', text(cmd.hint), ':::', '')
  const steps = [`Choose **${text(tool.label)}**. Its panel opens on the right with numbered steps; anything you selected first fills matching inputs.`]
  for (const s of cmd.slots) {
    const what = s.accepts ? ` (${text(accepts(s.accepts))}${s.multi ? ', one or more' : ''})` : ''
    steps.push(`Pick **${text(s.label)}**${what}${s.optional ? ', optional' : ''}.`)
  }
  const settings = cmd.params.filter(p => p.type !== 'action')
  if (settings.length) steps.push('Adjust the [settings](#settings) and check the preview in the viewport.')
  const actions = cmd.params.filter(p => p.type === 'action')
  if (actions.length) steps.push(`Use the panel buttons as needed: ${actions.map(a => `**${text(a.label)}**`).join(', ')}.`)
  steps.push('Confirm in the panel to apply, or cancel to leave the model unchanged. Applied changes are one undo step.')
  out.push('## How to use', '', ...steps.map((s, i) => `${i + 1}. ${s}`), '')
  if (settings.length) {
    out.push('## Settings', '', '| Setting | Default | Values |', '| --- | --- | --- |')
    for (const p of settings) out.push(`| ${cell(p.label)} | ${cell(value(p))} | ${pipes(detail(p))} |`)
    out.push('')
  }
  out.push('## Script it', '', `Command name \`${cmd.name}\`. See [Python scripting](/api/scripting) and the [command reference](/api/commands#${slug(cmd.name)}).`, '', scriptExample(cmd), '')
  return out
}
