import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import { Layers } from 'lucide-react'
import type { BookSummary } from '../api/types'
import { Cover } from './Cover'
import { displayName } from '../lib/format'

export function BookCard({ book, progress }: { book: BookSummary; progress?: number }) {
  const { t } = useTranslation()
  const series = book.series[0]
  return (
    <Link to={`/book/${book.id}`} className="group block min-w-0" title={book.title}>
      <div className="relative transition-transform duration-200 group-hover:-translate-y-0.5">
        <Cover book={book} />
        {book.editions > 1 && (
          <span className="absolute top-1.5 right-1.5 inline-flex items-center gap-1 rounded bg-black/65 px-1.5 py-0.5 text-[10px] text-white" title={t('book.editions', { count: book.editions })}>
            <Layers className="h-3 w-3" /> {book.editions}
          </span>
        )}
        {progress !== undefined && progress > 0 && (
          <div className="absolute inset-x-0 bottom-0 h-1 bg-black/30">
            <div className="h-full bg-accent-500" style={{ width: `${Math.round(progress * 100)}%` }} />
          </div>
        )}
      </div>
      <div className="mt-2 space-y-0.5">
        <div className="line-clamp-2 text-sm leading-snug font-medium group-hover:text-accent-700 dark:group-hover:text-accent-300">{book.title}</div>
        <div className="line-clamp-1 text-xs muted">{book.authors.map((a) => displayName(a.name)).join(', ') || t('book.unknownAuthor')}</div>
        {series && (
          <div className="line-clamp-1 text-xs muted">
            {series.name}
            {series.number > 0 && ` #${series.number}`}
          </div>
        )}
      </div>
    </Link>
  )
}

export function BookGrid({ books, progress }: { books: BookSummary[]; progress?: Record<number, number> }) {
  return (
    <div className="grid grid-cols-2 gap-x-4 gap-y-6 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
      {books.map((b) => (
        <BookCard key={b.id} book={b} progress={progress?.[b.id]} />
      ))}
    </div>
  )
}
