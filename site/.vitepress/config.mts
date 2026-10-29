import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Prism',
  description: 'A workspace for solid, surface and freeform CAD.',
  cleanUrls: true,
  base: process.env.PRISM_SITE_BASE || '/',
  lastUpdated: true,
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: '/prism-icon.svg' }]],
  themeConfig: {
    logo: '/prism-mark.svg',
    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Tools', link: '/reference/ribbons' },
      { text: 'Install', link: '/download' },
      { text: 'Development', link: '/development' },
    ],
    sidebar: [
      { text: 'Start modeling', items: [
        { text: 'Getting started', link: '/guide/getting-started' },
        { text: 'Sketches and features', link: '/guide/modeling' },
        { text: 'Ribbons and workspaces', link: '/guide/ribbons' },
        { text: 'AR and sharing', link: '/guide/ar' },
        { text: 'Files and compatibility', link: '/guide/files' },
      ] },
      { text: 'Reference', items: [
        { text: 'Tool catalog', link: '/reference/ribbons' },
        { text: 'Install Prism', link: '/download' },
        { text: 'Current limitations', link: '/reference/limitations' },
        { text: 'Build and maintain', link: '/development' },
        { text: 'Changes', link: '/changelog' },
      ] },
    ],
    search: { provider: 'local' },
    outline: [2, 3],
    footer: { message: 'Prism CAD · Documentation evolves with the application.' },
  },
})
