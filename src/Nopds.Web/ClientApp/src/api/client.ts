import type { AuthResponse } from './types'

/** Access token lives only in memory; the refresh token is an httpOnly cookie scoped to /api/v1/auth. */
let accessToken: string | null = null
let refreshing: Promise<AuthResponse | null> | null = null
const listeners = new Set<(auth: AuthResponse | null) => void>()

export function onAuthChanged(fn: (auth: AuthResponse | null) => void) {
  listeners.add(fn)
  return () => listeners.delete(fn)
}

export function setAuth(auth: AuthResponse | null) {
  accessToken = auth?.accessToken ?? null
  listeners.forEach((l) => l(auth))
}

export function getAccessToken() {
  return accessToken
}

export class ApiError extends Error {
  status: number
  body: unknown
  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.status = status
    this.body = body
  }
}

/** Single-flight refresh: concurrent 401s share one refresh request. */
export function refreshAuth(): Promise<AuthResponse | null> {
  refreshing ??= fetch('/api/v1/auth/refresh', { method: 'POST', credentials: 'same-origin' })
    .then(async (r) => (r.ok ? ((await r.json()) as AuthResponse) : null))
    .catch(() => null)
    .then((auth) => {
      setAuth(auth)
      return auth
    })
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

type Query = Record<string, string | number | boolean | undefined | null>

export function qs(query?: Query) {
  if (!query) return ''
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v))
  }
  const s = p.toString()
  return s ? `?${s}` : ''
}

export async function api<T>(path: string, init: RequestInit & { query?: Query; json?: unknown } = {}): Promise<T> {
  const { query, json, ...rest } = init
  const url = `/api/v1${path}${qs(query)}`
  const doFetch = () => {
    const headers = new Headers(rest.headers)
    if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
    if (json !== undefined) headers.set('Content-Type', 'application/json')
    headers.set('Accept-Language', document.documentElement.lang || navigator.language)
    return fetch(url, { ...rest, headers, credentials: 'same-origin', body: json !== undefined ? JSON.stringify(json) : rest.body })
  }

  let res = await doFetch()
  if (res.status === 401 && !path.startsWith('/auth/')) {
    const auth = await refreshAuth()
    if (auth) res = await doFetch()
  }

  if (res.status === 204 || res.status === 202) return undefined as T
  const text = await res.text()
  const body = text ? safeJson(text) : undefined
  if (!res.ok) {
    const message =
      (body && typeof body === 'object' && ('title' in body || 'error' in body)
        ? String((body as Record<string, unknown>).error ?? (body as Record<string, unknown>).title)
        : res.statusText) || `HTTP ${res.status}`
    throw new ApiError(res.status, message, body)
  }
  return body as T
}

function safeJson(text: string) {
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

/** Fetches a binary resource with auth (covers, book content) and returns an object URL or Blob. */
export async function apiBlob(path: string): Promise<Blob> {
  const doFetch = () =>
    fetch(`/api/v1${path}`, {
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {},
      credentials: 'same-origin',
    })
  let res = await doFetch()
  if (res.status === 401 && (await refreshAuth())) res = await doFetch()
  if (!res.ok) throw new ApiError(res.status, res.statusText)
  return res.blob()
}

/** Triggers a browser download of an authenticated resource. */
export async function download(path: string) {
  const doFetch = () =>
    fetch(`/api/v1${path}`, { headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {}, credentials: 'same-origin' })
  let res = await doFetch()
  if (res.status === 401 && (await refreshAuth())) res = await doFetch()
  if (!res.ok) {
    const t = await res.text()
    throw new ApiError(res.status, safeJson(t)?.detail ?? res.statusText)
  }
  const blob = await res.blob()
  const name = fileNameFrom(res.headers.get('Content-Disposition')) ?? 'book'
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

function fileNameFrom(header: string | null) {
  if (!header) return null
  const star = /filename\*=UTF-8''([^;]+)/i.exec(header)
  if (star) return decodeURIComponent(star[1])
  const plain = /filename="?([^";]+)"?/i.exec(header)
  return plain ? plain[1] : null
}
