import { defineConfig } from 'vitepress'
import { catalog, tabSlug } from '../tools/catalog-model.mjs'

// Tool pages are grouped by the first workspace that shows the ribbon.
const toolSidebar = (() => {
  const placed = new Set<string>(['Home', 'View', 'Analyze'])
  const groups = catalog.workspaces.map(w => {
    const tabs = w.Tabs.filter(t => !placed.has(t))
    tabs.forEach(t => placed.add(t))
    return { text: w.Name, collapsed: false, items: tabs.map(t => ({ text: t, link: `/tools/${tabSlug(t)}` })) }
  }).filter(g => g.items.length)
  const contextual = catalog.tabs.filter(t => !placed.has(t.title) && !['File'].includes(t.title))
  return [
    { text: 'Tool reference', items: [
      { text: 'All ribbons', link: '/tools/' },
      { text: 'Shared tools', link: '/tools/shared' },
      { text: 'File', link: '/tools/file' },
      { text: 'Home', link: '/tools/home' },
      { text: 'Analyze', link: '/tools/analyze' },
      { text: 'View', link: '/tools/view' },
      { text: 'Tool finder', link: '/reference/ribbons' },
    ] },
    ...groups,
    { text: 'Contextual', items: contextual.map(t => ({ text: t.title, link: `/tools/${tabSlug(t.title)}` })) },
  ]
})()

const guideSidebar = [
  { text: 'Start', items: [
    { text: 'Getting started', link: '/guide/getting-started' },
    { text: 'Ribbons and workspaces', link: '/guide/ribbons' },
    { text: 'Files and compatibility', link: '/guide/files' },
  ] },
  { text: 'Design', items: [
    { text: 'Sketching', link: '/guide/sketching' },
    { text: 'Solid modeling', link: '/guide/solid' },
    { text: 'Surfaces and NURBS', link: '/guide/surfacing' },
    { text: 'Freeform', link: '/guide/freeform' },
    { text: 'Sketches, solids and surfaces', link: '/guide/modeling' },
  ] },
  { text: 'Beyond modeling', items: [
    { text: 'Assembly and animation', link: '/guide/assembly' },
    { text: 'Simulation', link: '/guide/simulation' },
    { text: 'Manufacturing', link: '/guide/manufacturing' },
    { text: 'Rendering and materials', link: '/guide/rendering' },
    { text: 'AR and sharing', link: '/guide/ar' },
    { text: 'Scan and mesh', link: '/guide/scan' },
    { text: 'ECAD integration', link: '/guide/ecad' },
  ] },
  { text: 'About Prism', items: [
    { text: 'Install Prism', link: '/download' },
    { text: 'Current limitations', link: '/reference/limitations' },
    { text: 'Build and maintain', link: '/development' },
    { text: 'Changes', link: '/changelog' },
  ] },
]

const apiSidebar = [
  { text: 'Scripting and API', items: [
    { text: 'Overview', link: '/api/' },
    { text: 'Python scripting', link: '/api/scripting' },
    { text: 'HTTP protocol', link: '/api/http' },
    { text: 'MCP bridge', link: '/api/mcp' },
    { text: 'Examples', link: '/api/examples' },
  ] },
  { text: 'Reference', items: [
    { text: 'Python (cadkit)', link: '/api/python' },
    { text: 'Automation tools', link: '/api/automation' },
    { text: 'Commands', link: '/api/commands' },
  ] },
]

const base = process.env.PRISM_SITE_BASE || '/'
const repo = 'https://github.com/chrizpro/prism'

export default defineConfig({
  title: 'Prism',
  description: 'A workspace for solid, surface and freeform CAD.',
  cleanUrls: true,
  base,
  lastUpdated: true,
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}prism-icon.svg` }]],
  themeConfig: {
    logo: '/prism-mark.svg',
    socialLinks: [{ icon: 'github', link: repo }],
    editLink: { pattern: `${repo}/edit/main/site/:path`, text: 'Edit this page on GitHub' },
    nav: [
      { text: 'Guide', link: '/guide/getting-started', activeMatch: '^/guide/' },
      { text: 'Tools', link: '/tools/', activeMatch: '^/(tools|reference)/' },
      { text: 'API', link: '/api/', activeMatch: '^/api/' },
      { text: 'Install', link: '/download' },
      { text: 'Development', link: '/development' },
    ],
    sidebar: {
      '/guide/': guideSidebar,
      '/download': guideSidebar,
      '/changelog': guideSidebar,
      '/development': guideSidebar,
      '/reference/limitations': guideSidebar,
      '/tools/': toolSidebar,
      '/reference/ribbons': toolSidebar,
      '/api/': apiSidebar,
    },
    search: { provider: 'local' },
    outline: [2, 3],
    footer: { message: 'Prism CAD · Documentation evolves with the application.' },
  },
})
