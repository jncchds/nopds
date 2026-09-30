import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import clsx from 'clsx'
import { useAuth } from '../auth/AuthContext'
import { useConfig, useLibraries } from '../api/hooks'
import { api } from '../api/client'
import type { Page, ShelfItem } from '../api/types'
import { useLibrary } from '../hooks/useLibrary'
import { useTheme, type Theme } from '../hooks/useTheme'
import { LANGUAGES, rememberLanguage } from '../i18n'
import { displayName } from '../lib/format'
import { UpdatePrompt } from './UpdatePrompt'

const COLLAPSE_KEY = 'nopds.sidebar'

function initialCollapsed() {
  try {
    const v = localStorage.getItem(COLLAPSE_KEY)
    if (v !== null) return v === '1'
  } catch {
    /* ignore */
  }
  return window.matchMedia('(max-width: 768px)').matches
}

/** App shell modelled on ABook: collapsible sidebar with navigation, sections and account controls. */
export function Layout() {
  const { t, i18n } = useTranslation()
  const { user, logout, setUser } = useAuth()
  const config = useConfig()
  const libraries = useLibraries()
  const { library, setLibrary } = useLibrary()
  const { theme, setTheme } = useTheme()
  const [collapsed, setCollapsed] = useState(initialCollapsed)
  const [query, setQuery] = useState('')
  const navigate = useNavigate()
  const location = useLocation()
  const reading = useQuery({
    queryKey: ['shelf', 'sidebar'],
    queryFn: () => api<Page<ShelfItem>>('/shelf', { query: { unfinished: true } }),
    enabled: !!user,
    staleTime: 60_000,
  })

  useEffect(() => {
    try {
      localStorage.setItem(COLLAPSE_KEY, collapsed ? '1' : '0')
    } catch {
      /* ignore */
    }
  }, [collapsed])

  // On phones the expanded sidebar overlays content; close it after navigating.
  useEffect(() => {
    if (window.matchMedia('(max-width: 768px)').matches) setCollapsed(true)
  }, [location.pathname])

  const nav = [
    { to: '/', icon: '🏠', label: t('nav.home'), end: true },
    { to: '/books', icon: '📖', label: t('nav.books') },
    { to: '/authors', icon: '👤', label: t('nav.authors') },
    { to: '/series', icon: '📚', label: t('nav.series') },
    { to: '/genres', icon: '🏷️', label: t('nav.genres') },
    { to: '/folders', icon: '🗂️', label: t('nav.folders') },
    ...(user ? [{ to: '/shelf', icon: '🔖', label: t('nav.shelf') }] : []),
    ...(user && config.data?.uploads ? [{ to: '/upload', icon: '📤', label: t('nav.upload') }] : []),
    ...(user ? [{ to: '/settings', icon: '⚙️', label: t('nav.settings') }] : []),
    ...(user?.isAdmin ? [{ to: '/admin', icon: '🛡️', label: t('nav.admin') }] : []),
  ]

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = query.trim()
    if (q) navigate(`/search?q=${encodeURIComponent(q)}`)
  }

  const changeLanguage = async (lang: string) => {
    await i18n.changeLanguage(lang)
    rememberLanguage(lang)
    if (user) {
      try {
        setUser(await api('/me', { method: 'PUT', json: { uiLanguage: lang } }))
      } catch {
        /* offline: keep local choice */
      }
    }
  }

  const themes: Theme[] = ['system', 'light', 'dark']
  const nextTheme = themes[(themes.indexOf(theme) + 1) % themes.length]
  const libs = libraries.data ?? []
  const continueItems = reading.data?.items.slice(0, 8) ?? []

  return (
    <div className="flex h-dvh overflow-hidden">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 btn-primary">{t('nav.skip')}</a>
      <aside
        className={clsx(
          'z-30 flex shrink-0 flex-col overflow-hidden border-r border-line bg-surface transition-[width] duration-200',
          collapsed ? 'w-12' : 'w-[min(75vw,15rem)] max-md:absolute max-md:inset-y-0 max-md:left-0 max-md:shadow-2xl',
        )}
      >
        <div className="shrink-0 px-1 pt-1.5 pb-1">
          <button className="sidebar-btn" onClick={() => setCollapsed((v) => !v)} title={t('nav.menu')} aria-expanded={!collapsed}>
            <span className="w-[22px] shrink-0 text-center">☰</span>
            <Label collapsed={collapsed}>
              <span className="font-semibold">{config.data?.title ?? '.NET OPDS'}</span>
            </Label>
            {!collapsed && config.data?.version && (
              <a
                href="https://github.com/jncchds/nopds"
                target="_blank"
                rel="noopener noreferrer"
                onClick={(e) => e.stopPropagation()}
                className="shrink-0 rounded-full border border-accent/25 bg-accent-faint px-1.5 text-[0.65rem] leading-relaxed text-muted no-underline hover:text-accent"
              >
                v{config.data.version}
              </a>
            )}
          </button>
          {collapsed ? (
            <Link to="/search" className="sidebar-btn" title={t('search.placeholder')}>
              <span className="w-[22px] shrink-0 text-center">🔍</span>
            </Link>
          ) : (
            <form onSubmit={submit} role="search" className="px-1 pt-1">
              <input
                type="search"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder={t('search.placeholder')}
                aria-label={t('search.placeholder')}
                className="input"
              />
            </form>
          )}
        </div>

        <nav className="flex-1 overflow-x-hidden overflow-y-auto px-1 py-0.5" aria-label={t('nav.main')}>
          {nav.map((n) => (
            <NavLink
              key={n.to}
              to={n.to}
              end={n.end}
              title={n.label}
              className={({ isActive }) => clsx('sidebar-btn', isActive && 'bg-accent-faint text-accent')}
            >
              <span className="w-[22px] shrink-0 text-center">{n.icon}</span>
              <Label collapsed={collapsed}>{n.label}</Label>
            </NavLink>
          ))}

          {!collapsed && libs.length > 1 && (
            <>
              <Divider />
              <SectionTitle>{t('nav.library')}</SectionTitle>
              <SidebarEntry icon="🏛️" title={t('nav.allLibraries')} active={library === undefined} onClick={() => setLibrary(undefined)} />
              {libs.map((l) => (
                <SidebarEntry
                  key={l.id}
                  icon="📚"
                  title={l.name}
                  subtitle={t('common.count', { count: l.books })}
                  active={library === l.id}
                  onClick={() => setLibrary(l.id)}
                />
              ))}
            </>
          )}

          {!collapsed && continueItems.length > 0 && (
            <>
              <Divider />
              <SectionTitle>{t('home.continue')}</SectionTitle>
              {continueItems.map((i) => (
                <SidebarEntry
                  key={i.book.id}
                  icon="📖"
                  title={i.book.title}
                  subtitle={[i.progress >= 0.01 ? `${Math.round(i.progress * 100)}%` : null, i.book.authors.map((a) => displayName(a.name)).join(', ')].filter(Boolean).join(' · ')}
                  active={location.pathname === `/book/${i.book.id}`}
                  onClick={() => navigate(`/book/${i.book.id}`)}
                />
              ))}
            </>
          )}
        </nav>

        <div className="shrink-0 border-t border-line px-1 pt-1 pb-2">
          {!collapsed && (
            <label className="block px-1 pb-1">
              <span className="sr-only">{t('settings.language')}</span>
              <select className="input py-1" value={i18n.language} onChange={(e) => changeLanguage(e.target.value)}>
                {LANGUAGES.map((l) => (
                  <option key={l.code} value={l.code}>🌐 {l.name}</option>
                ))}
              </select>
            </label>
          )}
          {user ? (
            <button className="sidebar-btn" onClick={() => logout().then(() => navigate('/'))} title={t('auth.logout')}>
              <span className="w-[22px] shrink-0 text-center">🚪</span>
              <Label collapsed={collapsed}>{t('auth.logout')} ({user.userName})</Label>
            </button>
          ) : (
            <Link to="/login" className="sidebar-btn" title={t('auth.login')}>
              <span className="w-[22px] shrink-0 text-center">🔑</span>
              <Label collapsed={collapsed}>{t('auth.login')}</Label>
            </Link>
          )}
          <button className="sidebar-btn" onClick={() => setTheme(nextTheme)} title={`${t('theme.label')}: ${t(`theme.${theme}`)}`}>
            <span className="w-[22px] shrink-0 text-center">{theme === 'dark' ? '🌙' : theme === 'light' ? '☀️' : '◐'}</span>
            <Label collapsed={collapsed}>{t('theme.label')}: {t(`theme.${theme}`)}</Label>
          </button>
        </div>
      </aside>

      {!collapsed && <div className="fixed inset-0 z-20 bg-black/40 md:hidden" onClick={() => setCollapsed(true)} />}

      <main id="main" className="min-w-0 flex-1 overflow-y-auto bg-bg px-4 py-6 md:px-8">
        <div className="mx-auto max-w-6xl">
          <Outlet />
        </div>
      </main>
      <UpdatePrompt />
    </div>
  )
}

function Label({ collapsed, children }: { collapsed: boolean; children: ReactNode }) {
  return <span className={clsx('min-w-0 flex-1 truncate transition-opacity', collapsed && 'w-0 opacity-0')}>{children}</span>
}

function Divider() {
  return <div className="mx-0.5 my-1 h-px bg-line" />
}

function SectionTitle({ children }: { children: ReactNode }) {
  return <div className="truncate px-2 pt-1.5 pb-0.5 text-[0.7rem] font-bold tracking-wider text-muted uppercase">{children}</div>
}

function SidebarEntry({ icon, title, subtitle, active, onClick }: { icon: string; title: string; subtitle?: string; active?: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      title={title}
      className={clsx(
        'flex w-full items-start gap-1.5 overflow-hidden rounded-md px-2 py-1.5 text-left text-[0.82rem] leading-snug hover:bg-accent-faint',
        active && 'bg-accent-faint text-accent',
      )}
    >
      <span className="shrink-0 text-[0.85rem]">{icon}</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate font-semibold">{title}</span>
        {subtitle && <span className="mt-px block truncate text-[0.76rem] text-muted">{subtitle}</span>}
      </span>
    </button>
  )
}
