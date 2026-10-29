import { cpSync, existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs'
import { resolve } from 'node:path'
const root = resolve(import.meta.dirname, '..')
const source = resolve(root, '.vitepress/dist')
const destination = resolve(root, 'dist')
if (!existsSync(resolve(source, 'index.html'))) throw new Error('Build VitePress before staging the site.')
// This fixed destination contains generated output only. Empty it so removed pages cannot remain online.
if (existsSync(destination)) rmSync(destination, { recursive: true, force: true })
mkdirSync(destination, { recursive: true })
for (const name of readdirSync(source)) cpSync(resolve(source, name), resolve(destination, name), { recursive: true })
console.log('Staged VitePress output in dist for static hosting.')
