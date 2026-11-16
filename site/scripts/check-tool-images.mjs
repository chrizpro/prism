// Documentation coverage: fail when a tool has no illustration, metadata or asset.
import { readFileSync, existsSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { toolPages } from '../tools/catalog-model.mjs'

const registry = JSON.parse(readFileSync(new URL('../data/tool-images.json', import.meta.url), 'utf8'))
const publicDir = new URL('../public/', import.meta.url)
const errors = []
for (const page of toolPages) {
  const entry = registry[page.slug]
  if (!entry) { errors.push(`${page.slug}: no illustration`); continue }
  if (!entry.alt?.trim() || !entry.caption?.trim() || !(entry.width > 0 && entry.height > 0)) errors.push(`${page.slug}: incomplete metadata`)
  if (!['example', 'diagram', 'capture'].includes(entry.kind)) errors.push(`${page.slug}: missing source kind`)
  if (!/^\/images\/tools\/[a-z0-9-]+\.(svg|webp)$/.test(entry.src)) { errors.push(`${page.slug}: invalid image path`); continue }
  const file = new URL(entry.src.slice(1), publicDir)
  if (!existsSync(file)) { errors.push(`${page.slug}: missing ${entry.src}`); continue }
  const data = readFileSync(file)
  if (createHash('sha256').update(data).digest('hex') !== entry.sha256) errors.push(`${page.slug}: asset hash differs`)
  if (entry.src.endsWith('.svg')) {
    const svg = data.toString('utf8')
    if (!svg.includes('<title') || !svg.includes('<desc')) errors.push(`${page.slug}: SVG needs an accessible title and description`)
    if (!svg.includes(`width="${entry.width}" height="${entry.height}"`)) errors.push(`${page.slug}: SVG dimensions differ`)
  }
}
for (const slug of Object.keys(registry)) if (!toolPages.some(p => p.slug === slug)) errors.push(`${slug}: obsolete registry entry`)
if (errors.length) throw new Error(errors.join('\n'))
console.log(`Verified illustrations for all ${toolPages.length} tool pages (${new Set(Object.values(registry).map(i => i.src)).size} assets).`)
