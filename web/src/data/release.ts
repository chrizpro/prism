// The newest published release, read from GitHub when the site is built. The installer
// workflow uploads each package under a fixed name (see .github/workflows/installers.yml).
import { repo } from '../config'

export type Download = { id: string; os: string; label: string; detail: string; file: string; url?: string; size?: number; sha256Url?: string }
export type Release = { tag: string; name: string; date: string; url: string; prerelease: boolean }

const targets: Omit<Download, 'url' | 'size' | 'sha256Url'>[] = [
  { id: 'windows', os: 'Windows', label: 'Windows', detail: 'Windows 10 and 11 · x64 installer', file: 'Prism-windows-x64-Setup.exe' },
  { id: 'macos-arm64', os: 'macOS', label: 'macOS · Apple silicon', detail: 'M1 and newer · .dmg', file: 'Prism-macos-arm64.dmg' },
  { id: 'macos-x64', os: 'macOS', label: 'macOS · Intel', detail: 'Intel Macs · .dmg', file: 'Prism-macos-x64.dmg' },
  { id: 'linux', os: 'Linux', label: 'Linux', detail: 'Debian and Ubuntu · x64 .deb', file: 'Prism-linux-x64.deb' },
]

type GitHubAsset = { name: string; browser_download_url: string; size: number }
type GitHubRelease = { tag_name: string; name: string | null; published_at: string; html_url: string; prerelease: boolean; draft: boolean; assets: GitHubAsset[] }

export async function latestRelease(): Promise<{ release?: Release; downloads: Download[] }> {
  let found: GitHubRelease | undefined
  try {
    const token = process.env.GITHUB_TOKEN
    const response = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=10`, {
      headers: { Accept: 'application/vnd.github+json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    })
    if (response.ok) found = ((await response.json()) as GitHubRelease[]).find(r => !r.draft)
  } catch {
    // Offline builds show the "no release yet" state.
  }
  const downloads = targets.map(t => {
    const asset = found?.assets.find(a => a.name === t.file)
    const sha = found?.assets.find(a => a.name === `${t.file}.sha256`)
    return asset ? { ...t, url: asset.browser_download_url, size: asset.size, sha256Url: sha?.browser_download_url } : { ...t }
  })
  const release = found && { tag: found.tag_name, name: found.name || found.tag_name, date: found.published_at, url: found.html_url, prerelease: found.prerelease }
  return { release, downloads }
}
