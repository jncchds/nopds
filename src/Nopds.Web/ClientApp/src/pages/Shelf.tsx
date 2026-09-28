import { useTranslation } from 'react-i18next'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Page, ShelfItem } from '../api/types'
import { BookGrid, ViewToggle } from '../components/BookCard'
import { Empty, ErrorBox, Loading, LoadMore, PageTitle } from '../components/ui'

export default function Shelf() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const q = useInfiniteQuery({
    queryKey: ['shelf', 'all'],
    queryFn: ({ pageParam }) => api<Page<ShelfItem>>('/shelf', { query: { page: pageParam } }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasNext ? last.pageNumber + 1 : undefined),
  })
  const clear = useMutation({
    mutationFn: () => api('/shelf', { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['shelf'] }),
  })
  const items = q.data?.pages.flatMap((p) => p.items) ?? []

  return (
    <>
      <PageTitle
        actions={
          items.length > 0 && (
            <>
            <ViewToggle />
            <button className="btn-ghost" onClick={() => confirm(t('shelf.confirmClear')) && clear.mutate()}>
              {t('shelf.clear')}
            </button>
            </>
          )
        }
      >
        {t('nav.shelf')}
      </PageTitle>
      {q.isLoading ? (
        <Loading />
      ) : q.error ? (
        <ErrorBox error={q.error} />
      ) : items.length === 0 ? (
        <Empty>{t('shelf.empty')}</Empty>
      ) : (
        <>
          <BookGrid books={items.map((i) => i.book)} progress={Object.fromEntries(items.map((i) => [i.book.id, i.progress]))} />
          <LoadMore onVisible={() => q.fetchNextPage()} loading={q.isFetchingNextPage} hasMore={!!q.hasNextPage} />
        </>
      )}
    </>
  )
}
