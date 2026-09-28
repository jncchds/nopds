import { useState, type FormEvent } from 'react'
import { Link, NavLink, Outlet, useNavigate } from 'react-router'
import { useTranslation } from 'react-i18next'
import clsx from 'clsx'
import {
  BookOpen, Bookmark, FolderTree, Home, LibraryBig, LogIn, LogOut, Menu, Monitor, Moon, Search, Settings, Shield, Sun, Tags, Users, X,
} from 'lucide-react'
import { useAuth } from '../auth/AuthContext'
import { useConfig, useLibraries } from '../api/hooks'
import { useLibrary } from '../hooks/useLibrary'
import { useTheme, type Theme } from '../hooks/useTheme'
import { LANGUAGES, rememberLanguage } from '../i18n'
import { api } from '../api/client'
import { UpdatePrompt } from './UpdatePrompt'
import { Logo } from './Logo'

export function Layout() {
  const { t, i18n } = useTranslation()
  const { user, logout, setUser } = useAuth()
  const config = useConfig()
  const libraries = useLibraries()
  const { library, setLibrary } = useLibrary()
  const { theme, setTheme } = useTheme()
  const [menuOpen, setMenuOpen] = useState(false)
  const [query, setQuery] = useState('')
  const navigate = useNavigate()

  const nav = [
    { to: '/', icon: Home, label: t('nav.home'), end: true },
    { to: '/books', icon: BookOpen, label: t('nav.books') },
    { to: '/authors', icon: Users, label: t('nav.authors') },
    { to: '/series', icon: LibraryBig, label: t('nav.series') },
    { to: '/genres', icon: Tags, label: t('nav.genres') },
    { to: '/folders', icon: FolderTree, label: t('nav.folders') },
    ...(user ? [{ to: '/shelf', icon: Bookmark, label: t('nav.shelf') }] : []),
    ...(user?.isAdmin ? [{ to: '/admin', icon: Shield, label: t('nav.admin') }] : []),
  ]

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = query.trim()
    if (q) navigate(`/search?q=${encodeURIComponent(q)}`)
    setMenuOpen(false)
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

  const themes: { value: Theme; icon: typeof Sun; label: string }[] = [
    { value: 'system', icon: Monitor, label: t('theme.system') },
    { value: 'light', icon: Sun, label: t('theme.light') },
    { value: 'dark', icon: Moon, label: t('theme.dark') },
  ]
  const nextTheme = themes[(themes.findIndex((x) => x.value === theme) + 1) % themes.length]
  const ThemeIcon = themes.find((x) => x.value === theme)!.icon

  return (
    <div className="flex min-h-dvh flex-col">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 btn-primary">{t('nav.skip')}</a>
      <header className="sticky top-0 z-30 border-b border-stone-200 bg-stone-50/90 backdrop-blur dark:border-stone-800 dark:bg-stone-950/90">
        <div className="mx-auto flex h-14 max-w-7xl items-center gap-3 px-4">
          <button className="btn-ghost px-2 lg:hidden" onClick={() => setMenuOpen((v) => !v)} aria-label={t('nav.menu')} aria-expanded={menuOpen}>
            {menuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
          </button>
          <Link to="/" className="flex shrink-0 items-center gap-2 font-serif text-lg font-semibold">
            <Logo className="h-7 w-7" />
            <span className="hidden sm:inline">{config.data?.title ?? '.NET OPDS'}</span>
          </Link>
          <form onSubmit={submit} className="relative ml-auto w-full max-w-md" role="search">
            <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-stone-400" />
            <input
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t('search.placeholder')}
              className="input pl-9"
              aria-label={t('search.placeholder')}
            />
          </form>
          <button className="btn-ghost px-2" onClick={() => setTheme(nextTheme.value)} title={`${t('theme.label')}: ${themes.find((x) => x.value === theme)!.label}`}>
            <ThemeIcon className="h-5 w-5" />
          </button>
          {user ? (
            <div className="hidden items-center gap-1 sm:flex">
              <Link to="/settings" className="btn-ghost px-2" title={t('nav.settings')}>
                <Settings className="h-5 w-5" />
              </Link>
              <button className="btn-ghost px-2" onClick={() => logout().then(() => navigate('/'))} title={t('auth.logout')}>
                <LogOut className="h-5 w-5" />
              </button>
            </div>
          ) : (
            <Link to="/login" className="btn-secondary hidden sm:inline-flex">
              <LogIn className="h-4 w-4" /> {t('auth.login')}
            </Link>
          )}
        </div>
      </header>

      <div className="mx-auto flex w-full max-w-7xl flex-1">
        <aside
          className={clsx(
            'fixed inset-y-0 left-0 z-20 w-64 shrink-0 border-r border-stone-200 bg-stone-50 pt-14 transition-transform lg:sticky lg:top-14 lg:h-[calc(100dvh-3.5rem)] lg:translate-x-0 lg:border-0 lg:bg-transparent lg:pt-0 dark:border-stone-800 dark:bg-stone-950 lg:dark:bg-transparent',
            menuOpen ? 'translate-x-0' : '-translate-x-full',
          )}
        >
          <nav className="flex h-full flex-col gap-1 overflow-y-auto p-3" aria-label={t('nav.main')}>
            {(libraries.data?.length ?? 0) > 1 && (
              <label className="mb-2 block">
                <span className="mb-1 block px-2 text-xs font-medium tracking-wide uppercase muted">{t('nav.library')}</span>
                <select className="input" value={library ?? ''} onChange={(e) => setLibrary(e.target.value ? Number(e.target.value) : undefined)}>
                  <option value="">{t('nav.allLibraries')}</option>
                  {libraries.data!.map((l) => (
                    <option key={l.id} value={l.id}>{l.name}</option>
                  ))}
                </select>
              </label>
            )}
            {nav.map(({ to, icon: Icon, label, end }) => (
              <NavLink
                key={to}
                to={to}
                end={end}
                onClick={() => setMenuOpen(false)}
                className={({ isActive }) =>
                  clsx(
                    'flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors',
                    isActive ? 'bg-accent-100 font-medium text-accent-800 dark:bg-accent-900/40 dark:text-accent-200' : 'hover:bg-stone-200/70 dark:hover:bg-stone-800',
                  )
                }
              >
                <Icon className="h-4 w-4" /> {label}
              </NavLink>
            ))}
            <div className="mt-auto space-y-2 border-t border-stone-200 pt-3 dark:border-stone-800">
              {user ? (
                <div className="flex items-center justify-between gap-2 px-2 sm:hidden">
                  <Link to="/settings" className="link text-sm" onClick={() => setMenuOpen(false)}>{user.userName}</Link>
                  <button className="btn-ghost px-2 py-1 text-sm" onClick={() => logout().then(() => navigate('/'))}>{t('auth.logout')}</button>
                </div>
              ) : (
                <Link to="/login" className="btn-secondary w-full sm:hidden" onClick={() => setMenuOpen(false)}>{t('auth.login')}</Link>
              )}
              <label className="block px-2">
                <span className="sr-only">{t('settings.language')}</span>
                <select className="input py-1.5" value={i18n.language} onChange={(e) => changeLanguage(e.target.value)}>
                  {LANGUAGES.map((l) => (
                    <option key={l.code} value={l.code}>{l.name}</option>
                  ))}
                </select>
              </label>
              <p className="px-2 text-[11px] muted">.NET OPDS by CHDS {config.data?.version && `· v${config.data.version}`}</p>
            </div>
          </nav>
        </aside>
        {menuOpen && <div className="fixed inset-0 z-10 bg-black/30 lg:hidden" onClick={() => setMenuOpen(false)} />}
        <main id="main" className="min-w-0 flex-1 px-4 py-6 lg:px-8">
          <Outlet />
        </main>
      </div>
      <UpdatePrompt />
    </div>
  )
}
