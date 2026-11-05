<script setup>
import { withBase } from 'vitepress'
</script>

<template>
  <div class="alpha-banner" role="note">
    <span class="alpha-pill">Alpha</span>
    <span class="alpha-text">Prism is in active development: features may change and you may run into bugs. Keep backups of your work.</span>
    <a :href="withBase('/reference/known-issues')">Known issues</a>
    <a href="https://github.com/chrizpro/prism/issues/new?template=bug_report.yml">Report a bug</a>
  </div>
</template>

<style>
:root { --vp-layout-top-height: 36px; }
.alpha-banner {
  position: fixed; top: 0; left: 0; right: 0; z-index: 40; height: var(--vp-layout-top-height);
  display: flex; align-items: center; justify-content: center; gap: 12px; padding: 0 16px;
  background: #fff4dc; color: #5c4209; border-bottom: 1px solid #f0d9a3; font-size: 13px; white-space: nowrap; overflow: hidden;
}
.dark .alpha-banner { background: #2a2210; color: #f3dfb0; border-color: #4a3c16; }
.alpha-pill { background: #f5b83d; color: #1c1405; font-weight: 700; font-size: 11px; letter-spacing: .08em; text-transform: uppercase; padding: 1px 8px; border-radius: 999px; }
.alpha-text { overflow: hidden; text-overflow: ellipsis; }
.alpha-banner a { color: inherit; font-weight: 600; text-decoration: underline; text-underline-offset: 3px; flex-shrink: 0; }
@media (max-width: 640px) { .alpha-text { display: none; } }
</style>
