// Shared reading of ribbon-catalog.json, used by the tool pages here and by the promo site in /web.
// Keep anchors stable: the promo site and in-app links point at them.
import catalog from '../public/ribbon-catalog.json' with { type: 'json' }

export { catalog }

export const slug = s => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')

export const tabSlug = title => slug(title)

const signature = group => group.tools.map(t => t.id).join(',')

// A section is "shared" when the same title and the same tools appear on three or more ribbons.
// Those are documented once, on the shared page, instead of on every ribbon.
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

// Anchor of a tool on its ribbon page. A label repeated on one ribbon gets its section as a prefix.
export const toolAnchor = (tab, group, tool) => {
  const first = tab.groups.find(g => !isShared(g) && g.tools.some(t => slug(t.label) === slug(tool.label)))
  return first && first !== group ? `${slug(group.title)}-${slug(tool.label)}` : slug(tool.label)
}

export const sharedAnchor = (group, tool) => `${slug(group.title)}-${slug(tool.label)}`

// Where a tool is documented: its ribbon page, or the shared page for shared sections.
export const toolUrl = (tabTitle, groupTitle, label) => {
  const tab = catalog.tabs.find(t => t.title === tabTitle)
  const group = tab?.groups.find(g => g.title === groupTitle)
  const tool = group?.tools.find(t => t.label === label)
  if (!tool) return `/tools/${tabSlug(tabTitle)}`
  return isShared(group) ? `/tools/shared#${sharedAnchor(group, tool)}` : `/tools/${tabSlug(tabTitle)}#${toolAnchor(tab, group, tool)}`
}

export const workspacesFor = tabTitle => catalog.workspaces.filter(w => w.Tabs.includes(tabTitle)).map(w => w.Name)

export const toolCount = () => new Set(catalog.tabs.flatMap(t => t.groups.flatMap(g => g.tools.map(x => x.id)))).size
