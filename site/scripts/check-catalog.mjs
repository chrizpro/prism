import { readFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'
import { createHash } from 'node:crypto'

const catalog = JSON.parse(readFileSync(new URL('../public/ribbon-catalog.json', import.meta.url), 'utf8'))
const root = resolve(import.meta.dirname, '../..')
const files = ['Prism/ViewModels/MainViewModel.cs', 'Prism/ViewModels/MainViewModel.RibbonCustom.cs', 'Prism/ViewModels/MainViewModel.Workspaces.cs', 'Prism/Ribbon/RibbonLayoutPolicy.cs']
// A standalone hosting checkout carries the generated catalog; the CAD checkout verifies its provenance.
if (files.every(f => existsSync(resolve(root, f)))) {
  const source = files.map(f => readFileSync(resolve(root, f), 'utf8').replace(/\r\n/g, '\n')).join('\n')
  if (createHash('sha256').update(source).digest('hex').toUpperCase() !== catalog.sourceHash)
    throw new Error('Tool catalog is stale. Run dotnet run --project tools/Prism.DocsCatalog -- . from the repository root.')
}
console.log(`Verified ${catalog.tabs.length} ribbon tabs in the Prism tool catalog.`)
