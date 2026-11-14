<script setup>
import { withBase } from 'vitepress'
import { computed } from 'vue'
import images from '../data/guide-images.json'
import diagrams from '../data/guide-diagrams.json'
import toolImages from '../data/tool-images.json'

const props = defineProps({
  image: String,
  tool: String,
  src: String,
  alt: String,
  caption: String,
  width: Number,
  height: Number,
})

const picture = computed(() => {
  const registered = props.tool ? toolImages[props.tool] : props.image ? images[props.image] ?? diagrams[props.image] : {}
  if (!registered) throw new Error(`Unknown guide image: ${props.tool ?? props.image}`)
  const overrides = Object.fromEntries(Object.entries(props).filter(([key, value]) => !['image', 'tool'].includes(key) && value !== undefined))
  return { ...registered, ...overrides }
})
</script>

<template>
  <figure class="guide-image">
    <a :href="withBase(picture.src)" :aria-label="`${picture.alt}. Open full-size image`">
      <img :src="withBase(picture.src)" :alt="picture.alt" :width="picture.width" :height="picture.height" loading="lazy" decoding="async" />
    </a>
    <figcaption><strong v-if="picture.kind === 'diagram' || image && diagrams[image]">Concept diagram. </strong><strong v-else-if="picture.kind === 'example'">Prism help example. </strong><strong v-else-if="picture.kind === 'capture'">Prism result capture. </strong>{{ picture.caption }} <a :href="withBase(picture.src)">View full size</a></figcaption>
  </figure>
</template>

<style scoped>
.guide-image { margin: 28px 0; }
.guide-image img { display: block; width: 100%; height: auto; border: 1px solid var(--vp-c-divider); border-radius: 8px; }
.guide-image figcaption { margin-top: 10px; color: var(--vp-c-text-2); font-size: 13px; line-height: 1.6; }
.guide-image figcaption a { color: var(--vp-c-brand-1); white-space: nowrap; text-decoration: underline; text-underline-offset: 3px; }
</style>
