import { defineConfig } from 'astro/config'

export default defineConfig({
  site: process.env.PRISM_WEB_SITE || 'https://chrizpro.github.io',
  base: process.env.PRISM_WEB_BASE || '/',
  trailingSlash: 'ignore',
})
