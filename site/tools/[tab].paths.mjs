// One index page per ribbon (plus one for the shared sections), generated from the app's ribbon
// catalog. Each tool has its own page under /tool/. Refresh the data with tools/Prism.DocsCatalog
// and scripts/sync-api.py; edit hand-written text in guide-data.mjs.
import { catalog, isShared, pageFor, sharedGroups, sharedTabsFor, slug, tabSlug, workspacesFor } from './catalog-model.mjs'
import { groupTips, tabGuides, tabIntros } from './guide-data.mjs'
import { cell, text } from './render.mjs'
import { tabImages, illustration } from './image-data.mjs'

export function groupTip(tabTitle, group) {
  const tip = groupTips[`${tabTitle}:${group.title}`] ?? groupTips[group.title]
  return tip ? ['::: tip', tip, ':::', ''] : []
}

function toolTable(tabTitle, group) {
  const rows = group.tools.map(tool => {
    const page = pageFor(tabTitle, group.title, tool.label)
    const shortcut = tool.shortcut ? ` <kbd>${text(tool.shortcut)}</kbd>` : ''
    return `| [${cell(tool.label)}](/tool/${page.slug})${shortcut} | ${cell(tool.description)} |`
  })
  return ['| Tool | What it does |', '| --- | --- |', ...rows, '']
}

function tabPage(tab) {
  const ws = workspacesFor(tab.title)
  const own = tab.groups.filter(g => !isShared(g))
  const shared = tab.groups.filter(isShared)
  const lines = [
    // The title gets its own anchor so a section with the ribbon's name (Sheet Metal › Sheet metal) can use the plain one.
    `# ${text(tab.title)}${tab.contextual ? ' <Badge type="info" text="contextual" />' : ''} {#overview}`, '',
    tabIntros[tab.title] ?? '', '',
  ]
  if (ws.length) lines.push(`**Workspaces:** ${ws.join(', ')} · **Sections:** ${tab.groups.map(g => g.title).join(' → ')}`, '')
  else if (tab.contextual) lines.push(`**Appears** while its editing mode is active · **Sections:** ${tab.groups.map(g => g.title).join(' → ')}`, '')
  if (tabGuides[tab.title]) lines.push(`For a walkthrough, read the [${tabGuides[tab.title].text}](${tabGuides[tab.title].link}) guide.`, '')
  if (tabImages[tab.title]) lines.push(illustration(tabImages[tab.title]), '')
  for (const group of own) lines.push(`## ${text(group.title)} {#${slug(group.title)}}`, '', ...groupTip(tab.title, group), ...toolTable(tab.title, group))
  if (shared.length) {
    lines.push('## Shared sections {#shared}', '', 'These work the same on every ribbon that has them.', '')
    for (const group of shared) lines.push(`- [${text(group.title)}](/tools/shared#${slug(group.title)}): ${group.tools.map(t => `[${text(t.label)}](/tool/${pageFor(tab.title, group.title, t.label).slug})`).join(', ')}`)
    lines.push('')
  }
  return lines.join('\n')
}

function sharedPage() {
  const lines = ['# Shared tools', '', 'These sections appear with the same tools on many ribbons, in the same order.', '']
  for (const group of sharedGroups) {
    const on = sharedTabsFor(group)
    lines.push(`## ${text(group.title)} {#${slug(group.title)}}`, '', `On ${on.map(t => `[${t}](/tools/${tabSlug(t)})`).join(', ')}.`, '', ...groupTip('', group), ...toolTable(on[0], group))
  }
  return lines.join('\n')
}

export default {
  paths() {
    return [
      { params: { tab: 'shared' }, content: sharedPage() },
      ...catalog.tabs.map(tab => ({ params: { tab: tabSlug(tab.title) }, content: tabPage(tab) })),
    ]
  },
}
