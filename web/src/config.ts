// Where the VitePress guide is hosted. Override with PUBLIC_PRISM_DOCS_URL at build time.
export const docsUrl = (import.meta.env.PUBLIC_PRISM_DOCS_URL ?? 'https://chrizpro.github.io/prism/docs').replace(/\/$/, '')

export const docs = (path = '') => `${docsUrl}${path.startsWith('/') ? path : `/${path}`}`

export const base = import.meta.env.BASE_URL.replace(/\/$/, '')

export const href = (path: string) => `${base}${path}`

// Prism is not stable yet; say so wherever the version appears.
export const stage = 'alpha'

export const version = `0.1 ${stage}`

export const repo = process.env.PRISM_REPO ?? 'chrizpro/prism'

export const repoUrl = `https://github.com/${repo}`
