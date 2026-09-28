import { useTranslation } from 'react-i18next'
import { useBooks, type BookFilter } from '../api/hooks'
import { BookGrid } from './BookCard'
import { Empty, ErrorBox, Loading, LoadMore } from './ui'

/** Infinite-scrolling cover grid for any book filter. */
export function InfiniteBooks({ filter, enabled = true }: { filter: BookFilter; enabled?: boolean }) {
  const { t } = useTranslation()
  const q = useBooks(filter, enabled)
  if (q.isLoading) return <Loading />
  if (q.error) return <ErrorBox error={q.error} />
  const books = q.data?.pages.flatMap((p) => p.items) ?? []
  if (books.length === 0) return <Empty>{t('common.noBooks')}</Empty>
  return (
    <>
      <BookGrid books={books} />
      <LoadMore onVisible={() => q.fetchNextPage()} loading={q.isFetchingNextPage} hasMore={!!q.hasNextPage} />
    </>
  )
}
