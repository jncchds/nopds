import { useSyncExternalStore } from 'react'

export type ViewMode = 'list' | 'grid'
const KEY = 'nopds.view'
const listeners = new Set<() => void>()
let current: ViewMode = (() => {
  try {
    return localStorage.getItem(KEY) === 'grid' ? 'grid' : 'list'
  } catch {
    return 'list'
  }
})()

/** Book list presentation (ABook-style list cards by default, or a cover grid), remembered per browser. */
export function useViewMode() {
  const mode = useSyncExternalStore(
    (cb) => {
      listeners.add(cb)
      return () => listeners.delete(cb)
    },
    () => current,
  )
  const setMode = (m: ViewMode) => {
    current = m
    try {
      localStorage.setItem(KEY, m)
    } catch {
      /* ignore */
    }
    listeners.forEach((l) => l())
  }
  return [mode, setMode] as const
}
