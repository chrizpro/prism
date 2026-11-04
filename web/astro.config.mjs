import { defineConfig } from 'astro/config'

export default defineConfig({
  site: process.env.PRISM_WEB_SITE || 'https://prism.example',
  base: process.env.PRISM_WEB_BASE || '/',
  trailingSlash: 'ignore',
})
