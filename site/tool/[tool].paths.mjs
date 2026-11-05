// One page per tool, generated from the app's ribbon catalog and command descriptions.
// See tools/catalog-model.mjs for how buttons on several ribbons become one tool.
import { catalog, slug, tabSlug, toolPages } from '../tools/catalog-model.mjs'
import { groupTips, tabGuides } from '../tools/guide-data.mjs'
import { commandSections, text } from '../tools/render.mjs'

const tabOrder = new Map(catalog.tabs.map((t, i) => [t.title, i]))

function where(page) {
  // Shared sections sit on many ribbons; name the section once and list the ribbons.
  const byGroup = new Map()
  for (const p of page.places) {
    const key = `${p.group}|${p.shared}`
    if (!byGroup.has(key)) byGroup.set(key, { group: p.group, shared: p.shared, tabs: [] })
    byGroup.get(key).tabs.push(p.tab)
  }
  return [...byGroup.values()].map(({ group, shared, tabs }) => {
    const sorted = tabs.sort((a, b) => tabOrder.get(a) - tabOrder.get(b))
    // Ribbon pages give their own sections headings; shared sections are described on the shared page.
    if (shared) return `${sorted.map(t => `[${t}](/tools/${tabSlug(t)}#shared)`).join(', ')} › [${text(group)}](/tools/shared#${slug(group)})`
    return `${sorted.map(t => `[${t}](/tools/${tabSlug(t)}#${slug(group)})`).join(', ')} › **${text(group)}**`
  })
}

function toolPage(page) {
  const { tool, command } = page
  const first = page.places[0]
  const lines = [`# ${text(tool.label)}`, '', text(tool.description), '']
  const meta = where(page).map(w => `- **Ribbon:** ${w}`)
  if (tool.shortcut) meta.push(`- **Shortcut:** <kbd>${text(tool.shortcut)}</kbd>`)
  const guide = tabGuides[first.tab]
  if (guide) meta.push(`- **Guide:** [${guide.text}](${guide.link})`)
  lines.push(...meta, '')
  const tip = groupTips[`${first.tab}:${first.group}`] ?? groupTips[first.group]
  if (tip) lines.push('::: tip', tip, ':::', '')
  if (command) lines.push(...commandSections(command, tool))
  return lines.join('\n')
}

export default {
  paths() {
    return toolPages.map(page => ({ params: { tool: page.slug }, content: toolPage(page) }))
  },
}
