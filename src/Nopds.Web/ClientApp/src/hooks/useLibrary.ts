import { useSyncExternalStore } from 'react'

const KEY = 'nopds.library'
const listeners = new Set<() => void>()

function read(): number | undefined {
  try {
    const v = localStorage.getItem(KEY)
    return v ? Number(v) : undefined
  } catch {
    return undefined
  }
}

let current = read()

/** Selected library (undefined = all accessible libraries), remembered per browser. */
export function useLibrary() {
  const library = useSyncExternalStore(
    (cb) => {
      listeners.add(cb)
      return () => listeners.delete(cb)
    },
    () => current,
  )
  return {
    library,
    setLibrary(id: number | undefined) {
      current = id
      try {
        if (id === undefined) localStorage.removeItem(KEY)
        else localStorage.setItem(KEY, String(id))
      } catch {
        /* ignore */
      }
      listeners.forEach((l) => l())
    },
  }
}
