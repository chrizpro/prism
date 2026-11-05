// Shared reading of ribbon-catalog.json, used by the tool and ribbon pages here and by the
// promo site in /web. Keep slugs stable: the promo site and other pages link to them.
import catalog from '../public/ribbon-catalog.json' with { type: 'json' }
import commands from '../data/commands.json' with { type: 'json' }
import { commandMap } from './guide-data.mjs'

export { catalog }

export const slug = s => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')

export const tabSlug = title => slug(title)

const signature = group => group.tools.map(t => t.id).join(',')

// A section is "shared" when the same title and the same tools appear on three or more ribbons.
const sharedSignatures = (() => {
  const counts = new Map()
  for (const tab of catalog.tabs) for (const g of tab.groups) {
    const key = `${g.title}|${signature(g)}`
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  return new Map([...counts].filter(([, n]) => n >= 3))
})()

export const isShared = group => sharedSignatures.has(`${group.title}|${signature(group)}`)

export const sharedGroups = (() => {
  const seen = new Set()
  const out = []
  for (const tab of catalog.tabs) for (const g of tab.groups) {
    const key = `${g.title}|${signature(g)}`
    if (sharedSignatures.has(key) && !seen.has(key)) { seen.add(key); out.push(g) }
  }
  return out
})()

export const sharedTabsFor = group => catalog.tabs.filter(t => t.groups.some(g => g.title === group.title && signature(g) === signature(group))).map(t => t.title)

export const workspacesFor = tabTitle => catalog.workspaces.filter(w => w.Tabs.includes(tabTitle)).map(w => w.Name)

export const toolCount = () => new Set(catalog.tabs.flatMap(t => t.groups.flatMap(g => g.tools.map(x => x.id)))).size

// ------------------------------------------------------------------ commands behind buttons
const norm = s => (s ?? '').toLowerCase().replace(/[^a-z0-9]/g, '')
const byTitle = new Map()
for (const c of commands) if (!byTitle.has(norm(c.title))) byTitle.set(norm(c.title), c)
const byName = new Map(commands.map(c => [c.name, c]))

// Sketch tools draw inside the open sketch; View and Materials change the display. None of them
// are modeling commands, even where a label matches one.
const noCommands = new Set(['Sketch', 'View', 'Materials'])

export function commandFor(tabTitle, label, groupTitle) {
  if (noCommands.has(tabTitle)) return undefined
  for (const key of [`${tabTitle}:${groupTitle}:${label}`, `${tabTitle}:${label}`, label]) {
    if (key in commandMap) return commandMap[key] ? byName.get(commandMap[key]) : undefined
  }
  return byTitle.get(norm(label))
}

// ------------------------------------------------------------------ one page per tool
// A button that does the same thing on several ribbons is one tool with one page. Buttons that
// share a label but do different things (Extrude on Solid and on Surface, Fillet in a sketch)
// get separate pages; the first one in ribbon order keeps the plain slug.
export const toolPages = (() => {
  const pages = []
  const byKey = new Map()
  for (const tab of catalog.tabs) for (const group of tab.groups) for (const tool of group.tools) {
    const command = commandFor(tab.title, tool.label, group.title)
    // The same command is the same tool, whatever its ribbon description says.
    const key = command ? `${command.name}\u0001${tool.label}` : [tool.id, tool.label, tool.description, tab.title === 'Sketch'].join('\u0001')
    let page = byKey.get(key)
    if (!page) {
      page = { tool, command, places: [] }
      byKey.set(key, page)
      pages.push(page)
    }
    page.places.push({ tab: tab.title, group: group.title, shared: isShared(group) })
  }
  const taken = new Set()
  for (const page of pages) {
    let s = slug(page.tool.label)
    if (taken.has(s)) s = `${s}-${slug(page.places[0].tab)}`
    for (let n = 2; taken.has(s); n++) s = `${slug(page.tool.label)}-${n}`
    page.slug = s
    taken.add(s)
  }
  return pages
})()

export const pageFor = (tabTitle, groupTitle, label) =>
  toolPages.find(p => p.tool.label === label && p.places.some(pl => pl.tab === tabTitle && pl.group === groupTitle))

export const toolUrl = (tabTitle, groupTitle, label) => {
  const page = pageFor(tabTitle, groupTitle, label)
  return page ? `/tool/${page.slug}` : `/tools/${tabSlug(tabTitle)}`
}
