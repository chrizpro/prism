import { readdir, writeFile } from 'node:fs/promises'
import { join, relative, sep } from 'node:path'

const output = 'dist'
const origin = (process.env.PRISM_WEB_SITE || 'https://chrizpro.github.io').replace(/\/$/, '')
const base = `/${(process.env.PRISM_WEB_BASE || '').replace(/^\/+|\/+$/g, '')}`.replace(/\/$/, '')
const pages = []
async function visit(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) await visit(path)
    else if (entry.name === 'index.html' || entry.name.endsWith('.html') && entry.name !== '404.html') {
      let route = relative(output, path).split(sep).join('/').replace(/(^|\/)index\.html$/, '$1').replace(/\.html$/, '')
      pages.push(`${origin}${base}/${route}`)
    }
  }
}
await visit(output)
const escape = value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
await writeFile(join(output, 'sitemap.xml'), `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${pages.sort().map(url => `  <url><loc>${escape(url)}</loc></url>`).join('\n')}\n</urlset>\n`)
