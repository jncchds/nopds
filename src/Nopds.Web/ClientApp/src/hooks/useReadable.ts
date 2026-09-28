import { useConfig } from '../api/hooks'
import { readerSource } from '../lib/format'

/** Whether the web reader can open a format, directly or via server-side EPUB conversion. */
export function useReadable() {
  const conversions = useConfig().data?.readerConversions ?? []
  return (format: string) => readerSource(format, conversions) !== undefined
}
