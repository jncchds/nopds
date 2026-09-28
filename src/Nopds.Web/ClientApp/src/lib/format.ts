export function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

export function formatDate(iso: string | undefined, lang: string) {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString(lang, { year: 'numeric', month: 'short', day: 'numeric' })
}

export function formatDateTime(iso: string | undefined, lang: string) {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString(lang, { dateStyle: 'medium', timeStyle: 'short' })
}

/** "Last First Middle" → "First Middle Last" for display. */
export function displayName(lastFirst: string) {
  const parts = lastFirst.split(' ').filter(Boolean)
  return parts.length > 1 ? `${parts.slice(1).join(' ')} ${parts[0]}` : lastFirst
}

export function isReadable(format: string) {
  return ['epub', 'kepub', 'fb2', 'mobi', 'azw', 'azw3', 'cbz'].includes(format.toLowerCase())
}
