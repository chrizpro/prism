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
      <figure class="prism-model" aria-label="A faceted prism with construction guides">
        <svg viewBox="0 0 520 490" role="img" aria-labelledby="shape-title">
          <title id="shape-title">Geometry, from construction to solid</title>
          <defs><pattern id="grid" width="28" height="28" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1" fill="currentColor" opacity=".14"/></pattern></defs>
          <rect width="520" height="490" fill="url(#grid)"/>
          <g fill="none" stroke="currentColor" opacity=".3" stroke-width="1">
            <path d="M60 371H460M165 51V430M58 419L450 93" stroke-dasharray="4 7"/>
            <path d="M80 385V411M423 385V411M80 402H423M73 402L87 398M416 406L430 398"/>
          </g>
          <path d="M220 90L280 145L408 376L348 321Z" fill="#7762ed"/>
          <path d="M95 321L348 321L408 376L155 376Z" fill="#354fa8"/>
          <path d="M220 90L95 321L220 245Z" fill="#b5f2ff"/>
          <path d="M220 90L220 245L348 321Z" fill="#39c8ed"/>
          <path d="M95 321L348 321L220 245Z" fill="#4088ed"/>
          <g fill="currentColor" font-family="inherit" font-size="12"><text x="225" y="426">120.00 mm</text><text x="42" y="65">Construction → solid</text></g>
        </svg>
        <figcaption>One shape. Many ways to build it.</figcaption>
      </figure>
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
