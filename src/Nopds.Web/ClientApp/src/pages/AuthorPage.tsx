import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import clsx from 'clsx'
import { useAuthor, useNames } from '../api/hooks'
import { InfiniteBooks } from '../components/InfiniteBooks'
import { PageTitle } from '../components/ui'
import { displayName } from '../lib/format'

export default function AuthorPage() {
  const { id } = useParams()
  const authorId = Number(id)
  const { t } = useTranslation()
  const author = useAuthor(authorId)
  const series = useNames('series', { author: authorId })
  const [tab, setTab] = useState<'all' | 'series'>('all')
  const seriesItems = series.data?.pages.flatMap((p) => p.items) ?? []

  return (
    <>
      <PageTitle subtitle={author.data?.name}>{author.data ? displayName(author.data.name) : '…'}</PageTitle>
      {seriesItems.length > 0 && (
        <div className="mb-6 flex gap-1 border-b border-line" role="tablist">
          {(['all', 'series'] as const).map((x) => (
            <button
              key={x}
              role="tab"
              aria-selected={tab === x}
              onClick={() => setTab(x)}
              className={clsx('-mb-px border-b-2 px-4 py-2 text-sm', tab === x ? 'border-accent font-medium' : 'border-transparent muted')}
            >
              {t(x === 'all' ? 'author.allBooks' : 'author.bySeries')}
            </button>
          ))}
        </div>
      )}
      {tab === 'all' ? (
        <InfiniteBooks filter={{ author: authorId }} />
      ) : (
        <ul className="space-y-2">
          {seriesItems.map((s) => (
            <li key={s.id}>
              <Link to={`/series/${s.id}`} className="card flex items-center justify-between p-3 hover:border-accent">
                <span>{s.name}</span>
                <span className="text-sm muted">{t('common.count', { count: s.books })}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
