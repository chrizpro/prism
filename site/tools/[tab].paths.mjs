// One page per ribbon (plus one for the shared sections), generated from the app's ribbon
// catalog and command descriptions. Refresh the data with tools/Prism.DocsCatalog and
// scripts/sync-api.py; edit hand-written text in guide-data.mjs.
import { catalog, isShared, sharedGroups, sharedTabsFor, sharedAnchor, slug, tabSlug, toolAnchor, workspacesFor } from './catalog-model.mjs'
import { commandMap, groupTips, tabIntros } from './guide-data.mjs'
import commands from '../data/commands.json' with { type: 'json' }

const norm = s => (s ?? '').toLowerCase().replace(/[^a-z0-9]/g, '')
const byTitle = new Map()
for (const c of commands) if (!byTitle.has(norm(c.title))) byTitle.set(norm(c.title), c)
const byName = new Map(commands.map(c => [c.name, c]))

// Sketch tools draw inside the open sketch; they are not modeling commands, even where a label matches one.
const noCommands = new Set(['Sketch', 'View', 'Materials'])

export function commandFor(tabTitle, label) {
  if (noCommands.has(tabTitle)) return undefined
  for (const key of [`${tabTitle}:${label}`, label]) {
    if (key in commandMap) return commandMap[key] ? byName.get(commandMap[key]) : undefined
  }
  return byTitle.get(norm(label))
}

// Text from the app goes into Markdown; keep it from being read as HTML or Vue.
const text = s => String(s ?? '').replace(/</g, '&lt;').replace(/\{\{/g, '&#123;&#123;')
const pipes = s => s.replace(/\|/g, '\\|')
const cell = s => pipes(text(s)).replace(/\s+/g, ' ').trim()

// The same button on other ribbons. Sketch reuses icons for its own tools, so it only links to itself.
const tabsUsing = (id, from) => catalog.tabs
  .filter(t => (t.title === 'Sketch') === (from === 'Sketch'))
  .filter(t => t.groups.some(g => g.tools.some(x => x.id === id))).map(t => t.title)

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

function scriptExample(cmd) {
  const slots = cmd.slots.filter(s => !s.optional).map(s => `"${s.label}": ${s.multi ? '[...]' : '...'}`)
  const params = cmd.params.filter(p => p.type !== 'action' && p.default !== undefined && p.default !== null && p.default !== '').slice(0, 2)
  const args = [`"${cmd.name}"`]
  if (slots.length) args.push(`slots={${slots.join(', ')}}`)
  for (const p of params) args.push(`${pyKey(p.label)}=${pyValue(p)}`)
  return ['```python', 'import cadkit as cad', '', `cad.run(${args.join(', ')})`, '```'].join('\n')
}

function commandSection(cmd, tool) {
  const out = []
  if (cmd.hint && norm(cmd.hint) !== norm(tool.description)) out.push(`> ${text(cmd.hint)}`, '')
  const steps = [`Choose **${text(tool.label)}**. Its panel opens on the right with numbered steps; selected items fill matching inputs automatically.`]
  for (const s of cmd.slots) {
    const what = s.accepts ? ` (${text(s.accepts)}${s.multi ? ', one or more' : ''})` : ''
    steps.push(`Pick **${text(s.label)}**${what}${s.optional ? ', optional' : ''}.`)
  }
  const settings = cmd.params.filter(p => p.type !== 'action')
  if (settings.length) steps.push('Adjust the settings below and check the preview in the viewport.')
  const actions = cmd.params.filter(p => p.type === 'action')
  if (actions.length) steps.push(`Use the panel buttons as needed: ${actions.map(a => `**${text(a.label)}**`).join(', ')}.`)
  steps.push('Confirm in the panel to apply, or cancel to leave the model unchanged. Applied changes are one undo step.')
  out.push('**How to use**', '', ...steps.map((s, i) => `${i + 1}. ${s}`), '')
  if (settings.length) {
    out.push('| Setting | Default | Values |', '| --- | --- | --- |')
    for (const p of settings) out.push(`| ${cell(p.label)} | ${cell(value(p))} | ${pipes(detail(p))} |`)
    out.push('')
  }
  out.push('::: details Script it', `Command name \`${cmd.name}\`. See [Python scripting](/api/scripting).`, '', scriptExample(cmd), ':::', '')
  return out
}

function toolSection(tab, group, tool, anchor) {
  const out = [`### ${text(tool.label)} {#${anchor}}`, '', text(tool.description), '']
  const meta = []
  if (tool.shortcut) meta.push(`Shortcut <kbd>${text(tool.shortcut)}</kbd>`)
  const others = tabsUsing(tool.id, tab.title).filter(t => t !== tab.title)
  if (others.length && tab.title !== 'shared') meta.push(`Also on ${others.map(t => `[${t}](/tools/${tabSlug(t)})`).join(', ')}`)
  if (meta.length) out.push(meta.join(' · '), '')
  const cmd = commandFor(tab.title, tool.label)
  if (cmd) out.push(...commandSection(cmd, tool))
  return out
}

function groupIntro(tabTitle, group) {
  const tip = groupTips[`${tabTitle}:${group.title}`] ?? groupTips[group.title]
  return tip ? [`::: tip`, tip, ':::', ''] : []
}

function tabPage(tab) {
  const ws = workspacesFor(tab.title)
  const own = tab.groups.filter(g => !isShared(g))
  const shared = tab.groups.filter(isShared)
  const lines = [
    `# ${text(tab.title)}${tab.contextual ? ' <Badge type="info" text="contextual" />' : ''}`, '',
    tabIntros[tab.title] ?? '', '',
  ]
  if (ws.length) lines.push(`**Workspaces:** ${ws.join(', ')} · **Sections:** ${tab.groups.map(g => g.title).join(' → ')}`, '')
  else if (tab.contextual) lines.push(`**Appears** while its editing mode is active · **Sections:** ${tab.groups.map(g => g.title).join(' → ')}`, '')
  if (shared.length) {
    lines.push(`Shared sections (${shared.map(g => `[${g.title}](/tools/shared#${slug(g.title)})`).join(', ')}) work the same on every ribbon and are described on [Shared tools](/tools/shared).`, '')
  }
  for (const group of own) {
    lines.push(`## ${text(group.title)} {#section-${slug(group.title)}}`, '', ...groupIntro(tab.title, group))
    for (const tool of group.tools) lines.push(...toolSection(tab, group, tool, toolAnchor(tab, group, tool)))
  }
  return lines.join('\n')
}

function sharedPage() {
  const lines = ['# Shared tools', '', 'These sections appear with the same tools on many ribbons, in the same order. They are described once here.', '']
  for (const group of sharedGroups) {
    const on = sharedTabsFor(group)
    lines.push(`## ${text(group.title)} {#${slug(group.title)}}`, '', `On ${on.map(t => `[${t}](/tools/${tabSlug(t)})`).join(', ')}.`, '', ...groupIntro('', group))
    for (const tool of group.tools) lines.push(...toolSection({ title: 'shared', groups: [group] }, group, tool, sharedAnchor(group, tool)))
  }
  return lines.join('\n')
}

export default {
  paths() {
    return [
      { params: { tab: 'shared', title: 'Shared tools' }, content: sharedPage() },
      ...catalog.tabs.map(tab => ({ params: { tab: tabSlug(tab.title), title: tab.title }, content: tabPage(tab) })),
    ]
  },
}
