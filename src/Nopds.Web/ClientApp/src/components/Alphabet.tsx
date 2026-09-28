import { useTranslation } from 'react-i18next'
import clsx from 'clsx'
import type { LangCode } from '../api/types'
import { useAlphabet } from '../api/hooks'
import { Spinner } from './ui'

export const LANG_CODES: LangCode[] = ['all', 'cyrillic', 'latin', 'digits', 'other']

export function LangTabs({ value, onChange }: { value: LangCode; onChange: (v: LangCode) => void }) {
  const { t } = useTranslation()
  return (
    <div className="mb-4 flex flex-wrap gap-1 rounded-lg bg-stone-200/60 p-1 dark:bg-stone-800/60" role="tablist">
      {LANG_CODES.map((c) => (
        <button
          key={c}
          role="tab"
          aria-selected={value === c}
          onClick={() => onChange(c)}
          className={clsx(
            'rounded-md px-3 py-1.5 text-sm transition-colors',
            value === c ? 'bg-white font-medium shadow-sm dark:bg-stone-900' : 'muted hover:text-stone-900 dark:hover:text-stone-100',
          )}
        >
          {t(`alphabet.${c}`)}
        </button>
      ))}
    </div>
  )
}

/**
 * Letter navigation. Groups larger than `split` drill down to the next letter (SimpleOPDS SPLITITEMS),
 * smaller ones select a prefix for the list below.
 */
export function AlphabetBar({
  kind,
  lang,
  prefix,
  split,
  onPrefix,
}: {
  kind: 'books' | 'authors' | 'series'
  lang: LangCode
  prefix: string
  split: number
  onPrefix: (p: string) => void
}) {
  const { t } = useTranslation()
  const drill = prefix.length > 0 ? prefix : ''
  const q = useAlphabet(kind, lang, drill)
  const groups = q.data ?? []
  return (
    <nav className="mb-6" aria-label={t('alphabet.label')}>
      <div className="flex flex-wrap items-center gap-1.5">
        {prefix && (
          <button className="btn-ghost px-2 py-1 text-sm" onClick={() => onPrefix(prefix.slice(0, -1))}>
            ← {prefix.slice(0, -1) || t('alphabet.allLetters')}
          </button>
        )}
        {q.isLoading && <Spinner className="h-4 w-4" />}
        {groups.map((g) => (
          <button
            key={g.prefix}
            onClick={() => onPrefix(g.prefix)}
            title={t('common.count', { count: g.count })}
            className={clsx(
              'min-w-9 rounded-md border px-2 py-1 text-sm transition-colors',
              g.count > split ? 'border-dashed' : '',
              'border-stone-300 hover:border-accent-500 hover:text-accent-700 dark:border-stone-700 dark:hover:text-accent-300',
            )}
          >
            {g.prefix}
            <span className="ml-1 text-[10px] muted">{g.count}</span>
          </button>
        ))}
      </div>
    </nav>
  )
}
