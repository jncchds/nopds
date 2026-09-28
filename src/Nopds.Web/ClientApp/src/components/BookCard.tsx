import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import clsx from 'clsx'
import type { BookSummary } from '../api/types'
import { useMediaBase } from '../auth/AuthContext'
import { useViewMode } from '../hooks/useViewMode'
import { useReadable } from '../hooks/useReadable'
import { displayName, formatSize } from '../lib/format'
import { Cover } from './Cover'

/** Cover tile for the grid view. */
export function BookTile({ book, progress }: { book: BookSummary; progress?: number }) {
  const { t } = useTranslation()
  const series = book.series[0]
  return (
    <Link to={`/book/${book.id}`} className="group block min-w-0" title={book.title}>
      <div className="relative transition-transform duration-200 group-hover:-translate-y-0.5">
        <Cover book={book} />
        {book.editions > 1 && (
          <span className="absolute top-1.5 right-1.5 rounded bg-black/65 px-1.5 py-0.5 text-[10px] text-white" title={t('book.editions', { count: book.editions })}>
            ×{book.editions}
          </span>
        )}
        {progress !== undefined && progress > 0 && (
          <div className="absolute inset-x-0 bottom-0 h-1 bg-black/30">
            <div className="h-full bg-accent" style={{ width: `${Math.round(progress * 100)}%` }} />
          </div>
        )}
      </div>
      <div className="mt-2 space-y-0.5">
        <div className="line-clamp-2 text-sm leading-snug font-semibold group-hover:text-accent">{book.title}</div>
        <div className="line-clamp-1 text-xs muted">{book.authors.map((a) => displayName(a.name)).join(', ') || t('book.unknownAuthor')}</div>
        {series && <div className="line-clamp-1 text-xs muted">{series.name}{series.number > 0 && ` #${series.number}`}</div>}
      </div>
    </Link>
  )
}

/** ABook-style list card: cover, title, pills, annotation, actions on the right. */
export function BookRow({ book, progress }: { book: BookSummary; progress?: number }) {
  const { t } = useTranslation()
  const media = useMediaBase()
  const readable = useReadable()
  return (
    <div className="card flex items-start gap-4 px-4 py-4 transition-colors hover:border-accent sm:px-6">
      <Link to={`/book/${book.id}`} className="w-14 shrink-0 sm:w-16" tabIndex={-1} aria-hidden>
        <Cover book={book} compact />
      </Link>
      <div className="min-w-0 flex-1">
        <Link to={`/book/${book.id}`} className="mb-1 block text-[1.05rem] leading-snug font-bold hover:text-accent">{book.title}</Link>
        <div className="mb-1 text-sm muted">
          {book.authors.length === 0 ? t('book.unknownAuthor') : book.authors.map((a, i) => (
            <span key={a.id}>{i > 0 && ', '}<Link to={`/authors/${a.id}`} className="hover:text-accent">{displayName(a.name)}</Link></span>
          ))}
          {book.series.map((s) => (
            <span key={s.id}> · <Link to={`/series/${s.id}`} className="italic hover:text-accent">{s.name}{s.number > 0 && ` #${s.number}`}</Link></span>
          ))}
        </div>
        <div className="mb-1.5 flex flex-wrap gap-1.5">
          {book.genres.slice(0, 3).map((g) => <Link key={g.id} to={`/genres/${g.section}?genre=${g.id}`} className="chip">{g.name}</Link>)}
          {book.lang && <span className="pill-muted">{book.lang}</span>}
          <span className="pill-leaf uppercase">{book.format}</span>
        </div>
        {book.annotation && <p className="line-clamp-3 text-sm leading-relaxed muted">{book.annotation}</p>}
        {progress !== undefined && progress > 0 && (
          <div className="mt-2 h-1 max-w-xs overflow-hidden rounded bg-accent-faint">
            <div className="h-full bg-accent" style={{ width: `${Math.round(progress * 100)}%` }} />
          </div>
        )}
      </div>
      <div className="flex shrink-0 flex-col items-end gap-2">
        <span className="text-xs whitespace-nowrap muted">
          {formatSize(book.fileSize)}
          {book.editions > 1 && ` · ${t('book.editions', { count: book.editions })}`}
        </span>
        {readable(book.format) && <Link to={`/read/${book.id}`} className="btn-primary">{t('book.read')} →</Link>}
        <a href={`${media}/download/${book.id}/0`} className="btn-secondary" download>⬇ {book.format.toUpperCase()}</a>
      </div>
    </div>
  )
}

export function BookGrid({ books, progress, mode }: { books: BookSummary[]; progress?: Record<number, number>; mode?: 'list' | 'grid' }) {
  const [stored] = useViewMode()
  const view = mode ?? stored
  return view === 'grid' ? (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(8.5rem,1fr))] gap-x-4 gap-y-6">
      {books.map((b) => <BookTile key={b.id} book={b} progress={progress?.[b.id]} />)}
    </div>
  ) : (
    <div className="flex flex-col gap-3">
      {books.map((b) => <BookRow key={b.id} book={b} progress={progress?.[b.id]} />)}
    </div>
  )
}

export function ViewToggle() {
  const { t } = useTranslation()
  const [mode, setMode] = useViewMode()
  return (
    <div className="inline-flex overflow-hidden rounded-md border border-line text-sm" role="group" aria-label={t('view.label')}>
      {(['list', 'grid'] as const).map((m) => (
        <button
          key={m}
          onClick={() => setMode(m)}
          aria-pressed={mode === m}
          className={clsx('px-2.5 py-1', mode === m ? 'bg-accent-faint text-accent' : 'text-muted hover:text-fg')}
        >
          {m === 'list' ? '☰' : '▦'} <span className="sr-only sm:not-sr-only">{t(`view.${m}`)}</span>
        </button>
      ))}
    </div>
  )
}
