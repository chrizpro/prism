<script setup>
import { withBase } from 'vitepress'
</script>

<template>
  <main class="prism-home">
    <section class="prism-intro">
      <div>
        <img class="prism-hero-mark" :src="withBase('/prism-mark.svg')" width="100" height="100" alt="" />
        <h1>Give your ideas<br>their first shape.</h1>
        <p>Sketch with precision. Build solids, shape surfaces, and explore freeform geometry in one CAD workspace.</p>
        <div class="prism-actions">
          <a class="prism-primary" :href="withBase('/guide/getting-started')">Start modeling</a>
          <a :href="withBase('/download')">Install Prism <span aria-hidden="true">↗</span></a>
        </div>
      </div>
      <GuideImage class="prism-model" src="/images/guides/app-compact.png"
        alt="Prism Design workspace showing a solid plate, modeling ribbon and design history"
        caption="The Design workspace: modeling tools, your part and its editable history."
        :width="1500" :height="920" />
    </section>
    <section class="prism-paths" aria-label="Explore Prism">
      <a :href="withBase('/guide/getting-started')"><span class="prism-path-symbol">⌁</span><h2>Start with a sketch</h2><p>Planes, dimensions, constraints, and your first solid feature.</p><span>Getting started ↗</span></a>
      <a :href="withBase('/tools/')"><span class="prism-path-symbol">◈</span><h2>Learn every tool</h2><p>What each tool does, what it asks for and every setting, for every ribbon.</p><span>Tool reference ↗</span></a>
      <a :href="withBase('/api/')"><span class="prism-path-symbol">◇</span><h2>Script it</h2><p>Drive Prism from Python, any language over HTTP, or an AI assistant over MCP.</p><span>Scripting and API ↗</span></a>
    </section>
    <section class="prism-workflow">
      <h2>A familiar place for each action.</h2>
      <p>Shared ribbon sections follow the same order. Move stays visible, related transforms live together, and specialized workspaces keep their own tools close.</p>
      <div class="prism-ribbon-example" aria-label="Common ribbon sections"><span>Select</span><span>References</span><span>Create</span><span>Modify</span><span>Transform</span><span>Inspect</span><span>Share</span></div>
      <a :href="withBase('/guide/ribbons')">Learn the ribbon layout ↗</a>
    </section>
  </main>
</template>
