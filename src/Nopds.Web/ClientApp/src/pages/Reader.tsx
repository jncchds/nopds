import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, ChevronLeft, ChevronRight, List, Minus, Plus } from 'lucide-react'
import { api, apiBlob } from '../api/client'
import { useBook } from '../api/hooks'
import type { Progress } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ErrorBox, Spinner } from '../components/ui'

interface TocItem { label: string; href: string; subitems?: TocItem[] }
interface FoliateView extends HTMLElement {
  open(book: File | Blob): Promise<void>
  init(opts: { lastLocation?: string; showTextStart?: boolean }): Promise<void>
  goTo(target: string): Promise<void>
  goLeft(): Promise<void>
  goRight(): Promise<void>
  book: { toc?: TocItem[]; dir?: string }
  renderer: HTMLElement & { setStyles(css: string): void }
}

const FONT_SIZES = [85, 100, 115, 130, 150]

/** In-browser reader based on foliate-js (EPUB, FB2, MOBI/AZW3, CBZ). Position is saved per user. */
export default function Reader() {
  const { id } = useParams()
  const bookId = Number(id)
  const { t } = useTranslation()
  const { user } = useAuth()
  const book = useBook(bookId)
  const host = useRef<HTMLDivElement>(null)
  const view = useRef<FoliateView | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [fraction, setFraction] = useState(0)
  const [toc, setToc] = useState<TocItem[]>([])
  const [tocOpen, setTocOpen] = useState(false)
  const [chapter, setChapter] = useState('')
  const [fontSize, setFontSize] = useState(() => Number(localStorage.getItem('nopds.reader.font') ?? 100))
  const saveTimer = useRef<number | undefined>(undefined)

  const applyStyles = useCallback(() => {
    const dark = document.documentElement.classList.contains('dark')
    view.current?.renderer.setStyles?.(`
      html { color-scheme: ${dark ? 'dark' : 'light'}; }
      body { font-size: ${fontSize}% !important; line-height: 1.5; ${dark ? 'color: #e7e5e4 !important; background: #0c0a09 !important;' : ''} }
      ${dark ? 'a { color: #eaaf5c !important; }' : ''}
    `)
  }, [fontSize])

  useEffect(() => {
    let cancelled = false
    const container = host.current
    if (!container || !book.data) return
    ;(async () => {
      try {
        await import('foliate-js/view.js')
        const [blob, progress] = await Promise.all([
          apiBlob(`/books/${bookId}/content`),
          user ? api<Progress | undefined>(`/progress/${bookId}`).catch(() => undefined) : Promise.resolve(undefined),
        ])
        if (cancelled) return
        const file = new File([blob], book.data.book.fileName, { type: blob.type })
        const v = document.createElement('foliate-view') as FoliateView
        v.style.cssText = 'display:block;width:100%;height:100%'
        container.replaceChildren(v)
        view.current = v
        await v.open(file)
        v.addEventListener('relocate', ((e: CustomEvent) => {
          const d = e.detail as { fraction: number; cfi: string; tocItem?: { label: string } }
          setFraction(d.fraction ?? 0)
          setChapter(d.tocItem?.label ?? '')
          if (!user) return
          window.clearTimeout(saveTimer.current)
          saveTimer.current = window.setTimeout(() => {
            api(`/progress/${bookId}`, { method: 'PUT', json: { location: d.cfi, progress: d.fraction, finished: d.fraction > 0.995 } }).catch(() => {})
          }, 1500)
        }) as EventListener)
        setToc(v.book.toc ?? [])
        applyStyles()
        await v.init({ lastLocation: progress?.location, showTextStart: !progress?.location })
        setLoading(false)
      } catch (e) {
        if (!cancelled) {
          setError(e)
          setLoading(false)
        }
      }
    })()
    return () => {
      cancelled = true
      window.clearTimeout(saveTimer.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bookId, book.data?.book.fileName])

  useEffect(() => {
    applyStyles()
    localStorage.setItem('nopds.reader.font', String(fontSize))
  }, [applyStyles, fontSize])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowLeft' || e.key === 'PageUp') view.current?.goLeft()
      if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === ' ') view.current?.goRight()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const size = (delta: number) => {
    const i = FONT_SIZES.indexOf(fontSize)
    setFontSize(FONT_SIZES[Math.min(FONT_SIZES.length - 1, Math.max(0, (i < 0 ? 1 : i) + delta))])
  }

  return (
    <div className="fixed inset-0 z-40 flex flex-col bg-bg">
      <header className="flex h-12 items-center gap-2 border-b border-line px-2">
        <Link to={`/book/${bookId}`} className="btn-ghost px-2" aria-label={t('common.back')}>
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-medium">{book.data?.book.title}</div>
          {chapter && <div className="truncate text-xs muted">{chapter}</div>}
        </div>
        <button className="btn-ghost px-2" onClick={() => size(-1)} aria-label={t('reader.smaller')}><Minus className="h-4 w-4" /></button>
        <span className="w-10 text-center text-xs tabular-nums muted">{fontSize}%</span>
        <button className="btn-ghost px-2" onClick={() => size(1)} aria-label={t('reader.larger')}><Plus className="h-4 w-4" /></button>
        {toc.length > 0 && (
          <button className="btn-ghost px-2" onClick={() => setTocOpen((v) => !v)} aria-label={t('reader.toc')} aria-expanded={tocOpen}>
            <List className="h-5 w-5" />
          </button>
        )}
      </header>
      <div className="relative flex min-h-0 flex-1">
        <button className="hidden w-12 items-center justify-center muted hover:text-accent sm:flex" onClick={() => view.current?.goLeft()} aria-label={t('common.prev')}>
          <ChevronLeft className="h-6 w-6" />
        </button>
        <div ref={host} className="min-w-0 flex-1" />
        <button className="hidden w-12 items-center justify-center muted hover:text-accent sm:flex" onClick={() => view.current?.goRight()} aria-label={t('common.next')}>
          <ChevronRight className="h-6 w-6" />
        </button>
        {loading && (
          <div className="absolute inset-0 flex items-center justify-center">
            <Spinner className="h-8 w-8" />
          </div>
        )}
        {error != null && (
          <div className="absolute inset-0 flex items-center justify-center p-6">
            <ErrorBox error={error} />
          </div>
        )}
        {tocOpen && (
          <nav className="absolute inset-y-0 right-0 w-80 max-w-full overflow-y-auto border-l border-line bg-surface p-3 shadow-xl">
            <TocList items={toc} onPick={(href) => { view.current?.goTo(href); setTocOpen(false) }} />
          </nav>
        )}
      </div>
      <footer className="flex h-8 items-center gap-3 px-4 text-xs muted">
        <div className="h-1 flex-1 overflow-hidden rounded bg-accent-faint">
          <div className="h-full bg-accent transition-all" style={{ width: `${fraction * 100}%` }} />
        </div>
        <span className="tabular-nums">{Math.round(fraction * 100)}%</span>
      </footer>
    </div>
  )
}

function TocList({ items, onPick, depth = 0 }: { items: TocItem[]; onPick: (href: string) => void; depth?: number }) {
  return (
    <ul>
      {items.map((it, i) => (
        <li key={i}>
          <button className="w-full rounded px-2 py-1.5 text-left text-sm hover:bg-accent-faint" style={{ paddingLeft: 8 + depth * 14 }} onClick={() => onPick(it.href)}>
            {it.label}
          </button>
          {it.subitems && <TocList items={it.subitems} onPick={onPick} depth={depth + 1} />}
        </li>
      ))}
    </ul>
  )
}
