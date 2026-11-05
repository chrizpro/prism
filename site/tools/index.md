---
title: Tool reference
editLink: false
---

<script setup>
import { withBase } from 'vitepress'
import { catalog, tabSlug, toolCount } from './catalog-model.mjs'

const count = tab => new Set(tab.groups.flatMap(g => g.tools.map(t => t.id))).size
const byName = new Map(catalog.tabs.map(t => [t.title, t]))
const workspaces = catalog.workspaces.map(w => ({ ...w, tabs: w.Tabs.map(t => byName.get(t)).filter(Boolean) }))
const contextual = catalog.tabs.filter(t => t.contextual)
const total = toolCount()
</script>

# Tool reference

Every tool on every ribbon, {{ total }} in all, has its own page: what it does, the inputs it asks for, each setting with its default, and how to run it from a script. Pick a ribbon below to see its tools. Pages are generated from the application, so they match the shipped ribbons; a customized ribbon may arrange tools differently.

New to Prism? Read [how a command works](/guide/solid#how-a-command-works) first. Looking for one tool? Use search (<kbd>Ctrl</kbd>+<kbd>K</kbd>) or the [tool finder](/reference/ribbons). Sections that repeat on many ribbons, such as Select and Transform, are on [Shared tools](/tools/shared).

<div class="ws-grid">
  <section v-for="w in workspaces" :key="w.Id" class="ws-card">
    <h2 :id="w.Id">{{ w.Name }}</h2>
    <p>{{ w.Tip }}</p>
    <ul>
      <li v-for="t in w.tabs" :key="t.title"><a :href="withBase(`/tools/${tabSlug(t.title)}`)">{{ t.title }}</a> <span>{{ count(t) }}</span></li>
    </ul>
  </section>
  <section class="ws-card">
    <h2 id="contextual">Contextual</h2>
    <p>These ribbons appear while their editing mode is active.</p>
    <ul>
      <li v-for="t in contextual" :key="t.title"><a :href="withBase(`/tools/${tabSlug(t.title)}`)">{{ t.title }}</a> <span>{{ count(t) }}</span></li>
      <li><a :href="withBase('/tools/file')">File</a> <span>{{ count(byName.get('File')) }}</span></li>
    </ul>
  </section>
</div>
