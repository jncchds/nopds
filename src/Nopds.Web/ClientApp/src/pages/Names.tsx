import { Link, useSearchParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import type { LangCode } from '../api/types'
import { useConfig, useNames } from '../api/hooks'
import { AlphabetBar, LangTabs } from '../components/Alphabet'
import { Empty, ErrorBox, Loading, LoadMore, PageTitle } from '../components/ui'

/** Authors or series index with language tabs and letter navigation. */
export default function Names({ kind }: { kind: 'authors' | 'series' }) {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const config = useConfig()
  const lang = (params.get('lang') as LangCode) ?? 'all'
  const prefix = params.get('prefix') ?? ''
  const q = useNames(kind, { lang: lang === 'all' ? undefined : lang, q: prefix || undefined, match: 'begins' })

  const update = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v)
      else next.delete(k)
    }
    setParams(next, { replace: true })
  }

  const items = q.data?.pages.flatMap((p) => p.items) ?? []
  return (
    <>
      <PageTitle>{t(`nav.${kind}`)}</PageTitle>
      <LangTabs value={lang} onChange={(v) => update({ lang: v === 'all' ? undefined : v, prefix: undefined })} />
      {config.data?.alphabetMenu !== false && (
        <AlphabetBar kind={kind} lang={lang} prefix={prefix} split={config.data?.splitItems ?? 300} onPrefix={(p) => update({ prefix: p || undefined })} />
      )}
      {q.isLoading ? (
        <Loading />
      ) : q.error ? (
        <ErrorBox error={q.error} />
      ) : items.length === 0 ? (
        <Empty />
      ) : (
        <>
          <NameList kind={kind} items={items} />
          <LoadMore onVisible={() => q.fetchNextPage()} loading={q.isFetchingNextPage} hasMore={!!q.hasNextPage} />
        </>
      )}
    </>
  )
}

export function NameList({ kind, items }: { kind: 'authors' | 'series'; items: { id: number; name: string; books: number }[] }) {
  const { t } = useTranslation()
  return (
    <ul className="grid gap-x-6 sm:grid-cols-2 lg:grid-cols-3">
      {items.map((a) => (
        <li key={a.id} className="border-b border-line">
          <Link to={`/${kind}/${a.id}`} className="flex items-baseline justify-between gap-3 py-2.5 hover:text-accent">
            <span className="truncate">{a.name}</span>
            <span className="shrink-0 text-xs muted" title={t('common.count', { count: a.books })}>{a.books}</span>
          </Link>
        </li>
      ))}
    </ul>
  )
}

