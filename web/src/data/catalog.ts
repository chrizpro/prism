// The ribbon catalog is generated from the application by tools/Prism.DocsCatalog and shared
// with the VitePress guide, so the numbers on this site always match the shipped ribbons.
// Tool links use the guide's own catalog model, so they land on the right anchors.
import { catalog, slug, toolUrl } from '../../../site/tools/catalog-model.mjs'

type Tool = { id: string; label: string; description: string; shortcut: string }
type Group = { title: string; tools: Tool[] }
type Tab = { title: string; contextual: boolean; groups: Group[] }
type Workspace = { Id: string; Name: string; Tip: string; Tabs: string[] }

export const tabs = catalog.tabs as Tab[]
export const workspaces = catalog.workspaces as Workspace[]
export { slug, toolUrl }

export const uniqueTools = new Set(tabs.flatMap(t => t.groups.flatMap(g => g.tools.map(x => x.id)))).size

export const tab = (title: string) => tabs.find(t => t.title === title)

export const toolsIn = (...titles: string[]) => {
  const seen = new Set<string>()
  for (const t of titles) for (const g of tab(t)?.groups ?? []) for (const x of g.tools) seen.add(x.id)
  return seen.size
}

// Shared sections (Select, Transform...) repeat on every ribbon; list a tab's own groups.
const shared = new Set(['Select', 'Datum', 'References', 'Transform', 'Inspect', 'Appearance', 'Share'])
export const ownGroups = (title: string) => (tab(title)?.groups ?? []).filter(g => !shared.has(g.title))
