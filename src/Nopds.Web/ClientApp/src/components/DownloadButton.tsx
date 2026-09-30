import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import clsx from 'clsx'
import { ChevronDown, Download } from 'lucide-react'
import type { BookSummary } from '../api/types'
import { useConfig } from '../api/hooks'
import { useAuth, useMediaBase } from '../auth/AuthContext'
import { DEFAULT_FORMAT } from '../hooks/useDownloadFormats'

/** Formats that are already compressed; mirrors NoZipFormats in OpdsCatalog. */
const NO_ZIP = ['epub', 'kepub', 'mobi', 'azw', 'azw3', 'cbz', 'docx', 'zip']

interface Option {
  key: string
  label: string
  href: string
  kind: 'original' | 'zip' | 'converted'
}

function useDownloadOptions(book: BookSummary, targets?: string[]) {
  const media = useMediaBase()
  const config = useConfig().data
  const convertible = targets ?? config?.conversions?.[book.format] ?? []
  const format = book.format.toUpperCase()
  const options: Option[] = [{ key: book.format, label: format, href: `${media}/download/${book.id}/0`, kind: 'original' }]
  if (!NO_ZIP.includes(book.format)) {
    options.push({ key: `${book.format}.zip`, label: `${format}.ZIP`, href: `${media}/download/${book.id}/1`, kind: 'zip' })
  }
  for (const f of convertible) {
    options.push({ key: f, label: f.toUpperCase(), href: `${media}/convert/${book.id}/${f}`, kind: 'converted' })
  }
  return options
}

/**
 * Split button: the main part downloads the user's preferred format (converting when the server can),
 * falling back to the original file; the arrow lists every other available format.
 */
export function DownloadButton({ book, targets, compact }: { book: BookSummary; targets?: string[]; compact?: boolean }) {
  const { t } = useTranslation()
  const { user } = useAuth()
  const options = useDownloadOptions(book, targets)
  const preferred = user ? user.preferredFormat : DEFAULT_FORMAT
  const main = options.find((o) => o.key === preferred && o.kind !== 'zip') ?? options[0]
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onPointer = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const hint = (o: Option) => (o.kind === 'converted' ? t('download.converted') : o.kind === 'zip' ? t('download.zipped') : t('download.original'))
  const size = compact ? 'px-2.5' : ''

  return (
    <div ref={ref} className="relative inline-flex">
      <a
        href={main.href}
        className={clsx('btn-secondary', options.length > 1 && 'rounded-r-none', size)}
        download
        title={main.kind === 'converted' ? t('book.convert', { format: main.label }) : undefined}
      >
        <Download className="h-4 w-4" /> {main.label}
      </a>
      {options.length > 1 && (
        <button
          type="button"
          className={clsx('btn-secondary -ml-px rounded-l-none px-1.5', open && 'border-accent text-fg')}
          onClick={() => setOpen((v) => !v)}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-label={t('download.more')}
          title={t('download.more')}
        >
          <ChevronDown className={clsx('h-4 w-4 transition-transform', open && 'rotate-180')} />
        </button>
      )}
      {open && (
        <div role="menu" className="card absolute top-full right-0 z-30 mt-1 min-w-44 py-1 shadow-lg">
          {options.map((o) => (
            <a
              key={o.key}
              role="menuitem"
              href={o.href}
              download
              onClick={() => setOpen(false)}
              className={clsx('flex items-center justify-between gap-4 px-3 py-1.5 text-sm hover:bg-accent-faint', o === main && 'font-semibold')}
            >
              <span>{o.label}</span>
              <span className="text-xs muted">{hint(o)}</span>
            </a>
          ))}
        </div>
      )}
    </div>
  )
}
