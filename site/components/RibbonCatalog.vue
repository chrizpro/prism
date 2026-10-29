<script setup>
import { computed, ref } from 'vue'
import catalog from '../public/ribbon-catalog.json'
const query = ref('')
const tabName = ref('Home')
const tabs = computed(() => catalog.tabs.filter(t => tabName.value === 'all' || t.title === tabName.value).map(t => ({ ...t,
  groups: t.groups.map(g => ({ ...g, tools: g.tools.filter(tool => `${tool.label} ${tool.description} ${g.title}`.toLowerCase().includes(query.value.toLowerCase())) })).filter(g => g.tools.length),
})).filter(t => t.groups.length))
const count = computed(() => tabs.value.reduce((n, t) => n + t.groups.reduce((n, g) => n + g.tools.length, 0), 0))
</script>

<template>
  <div class="catalog-controls">
    <label>Ribbon<select v-model="tabName"><option value="all">All ribbons</option><option v-for="tab in catalog.tabs" :key="tab.title" :value="tab.title">{{ tab.title }}</option></select></label>
    <label>Find a tool<input v-model="query" type="search" placeholder="Try “scale” or “AR”"></label>
  </div>
  <p class="catalog-count" role="status">{{ count }} matching tools</p>
  <section v-for="tab in tabs" :key="tab.title" class="catalog-tab">
    <h2>{{ tab.title }} <small v-if="tab.contextual">Contextual</small></h2>
    <section v-for="group in tab.groups" :key="group.title" class="catalog-group">
      <h3>{{ group.title }}</h3>
      <dl><template v-for="tool in group.tools" :key="tool.id"><dt>{{ tool.label }} <kbd v-if="tool.shortcut">{{ tool.shortcut }}</kbd></dt><dd>{{ tool.description }}</dd></template></dl>
    </section>
  </section>
  <p v-if="!tabs.length">No matching tools. Try another ribbon or a shorter search.</p>
</template>
