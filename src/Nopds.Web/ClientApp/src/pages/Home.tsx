import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Shuffle } from 'lucide-react'
import { useConfig, useRecentBooks, useStats } from '../api/hooks'
import { api } from '../api/client'
import type { BookSummary, Page, ShelfItem } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { BookGrid } from '../components/BookCard'
import { Cover } from '../components/Cover'
import { ErrorBox, Loading } from '../components/ui'
import { useLibrary } from '../hooks/useLibrary'
import { displayName } from '../lib/format'

export default function Home() {
  const { t, i18n } = useTranslation()
  const { user } = useAuth()
  const { library } = useLibrary()
  const config = useConfig()
  const stats = useStats()
  const recent = useRecentBooks(12)
  const reading = useQuery({
    queryKey: ['shelf', 'continue'],
    queryFn: () => api<Page<ShelfItem>>('/shelf', { query: { unfinished: true } }),
    enabled: !!user,
  })
  const random = useQuery({
    queryKey: ['random', library],
    queryFn: () => api<BookSummary | undefined>('/books/random', { query: { library } }),
    staleTime: Infinity,
  })

  const nf = new Intl.NumberFormat(i18n.language)
  const tiles = stats.data
    ? [
        { to: '/books', label: t('stats.books'), value: stats.data.books },
        { to: '/authors', label: t('stats.authors'), value: stats.data.authors },
        { to: '/series', label: t('stats.series'), value: stats.data.series },
        { to: '/genres', label: t('stats.genres'), value: stats.data.genres },
      ]
    : []

  return (
    <div className="space-y-10">
      <section>
        <h1 className="font-serif text-3xl font-semibold tracking-tight">{config.data?.title}</h1>
        <p className="mt-1 muted">{config.data?.subtitle}</p>
        {stats.error && <ErrorBox error={stats.error} />}
        <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
          {tiles.map((x) => (
            <Link key={x.to} to={x.to} className="card p-4 transition-colors hover:border-accent-400">
              <div className="text-2xl font-semibold tabular-nums">{nf.format(x.value)}</div>
              <div className="text-sm muted">{x.label}</div>
            </Link>
          ))}
        </div>
      </section>

      {reading.data && reading.data.items.length > 0 && (
        <section>
          <div className="mb-4 flex items-baseline justify-between">
            <h2 className="text-xl font-semibold">{t('home.continue')}</h2>
            <Link to="/shelf" className="link text-sm">{t('common.all')}</Link>
          </div>
          <BookGrid books={reading.data.items.slice(0, 6).map((i) => i.book)} progress={Object.fromEntries(reading.data.items.map((i) => [i.book.id, i.progress]))} />
        </section>
      )}

      <section>
        <div className="mb-4 flex items-baseline justify-between">
          <h2 className="text-xl font-semibold">{t('home.recent')}</h2>
          <Link to="/books?sort=added" className="link text-sm">{t('common.all')}</Link>
        </div>
        {recent.isLoading ? <Loading /> : recent.data && <BookGrid books={recent.data.items} />}
      </section>

      {random.data && (
        <section className="card flex gap-5 p-5">
          <Link to={`/book/${random.data.id}`} className="w-28 shrink-0 sm:w-36">
            <Cover book={random.data} />
          </Link>
          <div className="min-w-0 flex-1">
            <div className="mb-1 flex items-center justify-between gap-2">
              <h2 className="text-sm font-semibold tracking-wide text-accent-700 uppercase dark:text-accent-300">{t('home.random')}</h2>
              <button className="btn-ghost px-2 py-1" onClick={() => random.refetch()} title={t('home.another')}>
                <Shuffle className="h-4 w-4" />
              </button>
            </div>
            <Link to={`/book/${random.data.id}`} className="font-serif text-xl font-semibold hover:underline">{random.data.title}</Link>
            <p className="muted">{random.data.authors.map((a) => displayName(a.name)).join(', ')}</p>
            {random.data.annotation && <p className="mt-3 line-clamp-4 text-sm">{random.data.annotation}</p>}
          </div>
        </section>
      )}
    </div>
  )
}
