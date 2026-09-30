import { useConfig } from '../api/hooks'

/** Download format when nobody is signed in. Matches the server default for new accounts. */
export const DEFAULT_FORMAT = 'fb2'

/** Every format the server can offer as a download, for the preferred-format setting. */
export function useDownloadFormats() {
  const config = useConfig().data
  const all = new Set<string>([DEFAULT_FORMAT, 'epub'])
  for (const [from, to] of Object.entries(config?.conversions ?? {})) {
    all.add(from)
    to.forEach((f) => all.add(f))
  }
  return [...all].sort()
}
