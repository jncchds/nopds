import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, onAuthChanged, refreshAuth, setAuth } from '../api/client'
import type { AuthResponse, User } from '../api/types'

interface AuthState {
  user: User | null
  /** Per-user OPDS token, used for media URLs (<img>, native downloads) that cannot carry a bearer header. */
  feedToken: string | null
  ready: boolean
  login: (userName: string, password: string) => Promise<void>
  logout: () => Promise<void>
  setUser: (u: User) => void
  setFeedToken: (t: string) => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUserState] = useState<User | null>(null)
  const [feedToken, setFeedToken] = useState<string | null>(null)
  const [ready, setReady] = useState(false)
  const queryClient = useQueryClient()

  const loadToken = useCallback(async () => {
    try {
      const r = await api<{ token: string }>('/me/feed-token')
      setFeedToken(r.token)
    } catch {
      setFeedToken(null)
    }
  }, [])

  useEffect(() => {
    const off = onAuthChanged((auth: AuthResponse | null) => {
      setUserState(auth?.user ?? null)
      if (!auth) setFeedToken(null)
    })
    refreshAuth()
      .then(async (auth) => {
        if (auth) await loadToken()
      })
      .finally(() => setReady(true))
    return () => {
      off()
    }
  }, [loadToken])

  const login = useCallback(
    async (userName: string, password: string) => {
      const auth = await api<AuthResponse>('/auth/login', { method: 'POST', json: { userName, password } })
      setAuth(auth)
      await loadToken()
      queryClient.invalidateQueries()
    },
    [loadToken, queryClient],
  )

  const logout = useCallback(async () => {
    try {
      await api('/auth/logout', { method: 'POST' })
    } finally {
      setAuth(null)
      queryClient.clear()
    }
  }, [queryClient])

  const value = useMemo<AuthState>(
    () => ({ user, feedToken, ready, login, logout, setUser: setUserState, setFeedToken }),
    [user, feedToken, ready, login, logout],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth outside AuthProvider')
  return ctx
}

/** Base for media URLs: /opds/t/{token} when signed in, /opds otherwise (public libraries). */
// eslint-disable-next-line react-refresh/only-export-components
export function useMediaBase() {
  const { feedToken } = useAuth()
  return feedToken ? `/opds/t/${feedToken}` : '/opds'
}
