import { useSearchParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useBooks, useNames } from '../api/hooks'
import { BookGrid, ViewToggle } from '../components/BookCard'
import { Empty, Loading, LoadMore, PageTitle } from '../components/ui'
import { NameList } from './Names'

export default function Search() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const q = (params.get('q') ?? '').trim()
  const enabled = q.length > 0
  const books = useBooks({ q, match: 'contains' }, enabled, 24)
  const authors = useNames('authors', { q, match: 'contains' }, enabled)
  const series = useNames('series', { q, match: 'contains' }, enabled)

  if (!enabled) return <Empty>{t('search.hint')}</Empty>
  const bookItems = books.data?.pages.flatMap((p) => p.items) ?? []
  const authorItems = authors.data?.pages.flatMap((p) => p.items) ?? []
  const seriesItems = series.data?.pages.flatMap((p) => p.items) ?? []
  const loading = books.isLoading || authors.isLoading || series.isLoading
  const nothing = !loading && bookItems.length + authorItems.length + seriesItems.length === 0

  return (
    <>
      <PageTitle>{t('search.results', { q })}</PageTitle>
      {loading && <Loading />}
      {nothing && <Empty />}
      {authorItems.length > 0 && (
        <section className="mb-8">
          <h2 className="mb-2 text-lg font-semibold">{t('nav.authors')}</h2>
          <NameList kind="authors" items={authorItems.slice(0, 30)} />
        </section>
      )}
      {seriesItems.length > 0 && (
        <section className="mb-8">
          <h2 className="mb-2 text-lg font-semibold">{t('nav.series')}</h2>
          <NameList kind="series" items={seriesItems.slice(0, 30)} />
        </section>
      )}
      {bookItems.length > 0 && (
        <section>
          <div className="mb-4 flex items-center justify-between"><h2 className="text-lg font-semibold">{t('nav.books')}</h2><ViewToggle /></div>
          <BookGrid books={bookItems} />
          <LoadMore onVisible={() => books.fetchNextPage()} loading={books.isFetchingNextPage} hasMore={!!books.hasNextPage} />
        </section>
      )}
    </>
  )
}
