import { useSearchParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import type { BookSort, LangCode } from '../api/types'
import { useConfig } from '../api/hooks'
import { AlphabetBar, LangTabs } from '../components/Alphabet'
import { InfiniteBooks } from '../components/InfiniteBooks'
import { PageTitle } from '../components/ui'

export default function Books() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const config = useConfig()
  const lang = (params.get('lang') as LangCode) ?? 'all'
  const prefix = params.get('prefix') ?? ''
  const sort = (params.get('sort') as BookSort) ?? 'title'

  const update = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v)
      else next.delete(k)
    }
    setParams(next, { replace: true })
  }

  return (
    <>
      <PageTitle
        actions={
          <select className="input w-auto" value={sort} onChange={(e) => update({ sort: e.target.value === 'title' ? undefined : e.target.value })}>
            <option value="title">{t('sort.title')}</option>
            <option value="added">{t('sort.added')}</option>
          </select>
        }
      >
        {t('nav.books')}
      </PageTitle>
      {sort === 'title' && (
        <>
          <LangTabs value={lang} onChange={(v) => update({ lang: v === 'all' ? undefined : v, prefix: undefined })} />
          {config.data?.alphabetMenu !== false && (
            <AlphabetBar kind="books" lang={lang} prefix={prefix} split={config.data?.splitItems ?? 300} onPrefix={(p) => update({ prefix: p || undefined })} />
          )}
        </>
      )}
      <InfiniteBooks filter={{ lang: lang === 'all' ? undefined : lang, q: prefix || undefined, match: 'begins', sort }} />
    </>
  )
}
