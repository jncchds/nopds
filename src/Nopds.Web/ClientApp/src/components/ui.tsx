import { useEffect, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Loader2 } from 'lucide-react'
import clsx from 'clsx'

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={clsx('animate-spin text-accent-600', className ?? 'h-6 w-6')} aria-hidden />
}

export function Loading() {
  const { t } = useTranslation()
  return (
    <div className="flex items-center justify-center gap-3 py-16 muted" role="status">
      <Spinner />
      <span>{t('common.loading')}</span>
    </div>
  )
}

export function ErrorBox({ error }: { error: unknown }) {
  const { t } = useTranslation()
  const message = error instanceof Error ? error.message : String(error)
  return (
    <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200" role="alert">
      <strong>{t('common.error')}:</strong> {message}
    </div>
  )
}

export function Empty({ children }: { children?: ReactNode }) {
  const { t } = useTranslation()
  return <div className="py-16 text-center muted">{children ?? t('common.nothing')}</div>
}

export function PageTitle({ children, actions, subtitle }: { children: ReactNode; actions?: ReactNode; subtitle?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        <h1 className="font-serif text-2xl font-semibold tracking-tight sm:text-3xl">{children}</h1>
        {subtitle && <p className="mt-1 muted">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

/** Calls onVisible when the sentinel scrolls into view (infinite lists). */
export function LoadMore({ onVisible, loading, hasMore }: { onVisible: () => void; loading: boolean; hasMore: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  const { t } = useTranslation()
  useEffect(() => {
    const el = ref.current
    if (!el || !hasMore) return
    const io = new IntersectionObserver((entries) => entries[0].isIntersecting && !loading && onVisible(), { rootMargin: '600px' })
    io.observe(el)
    return () => io.disconnect()
  }, [onVisible, loading, hasMore])
  if (!hasMore) return null
  return (
    <div ref={ref} className="flex justify-center py-6">
      {loading ? <Spinner /> : <button className="btn-secondary" onClick={onVisible}>{t('common.more')}</button>}
    </div>
  )
}

export function Toggle({ checked, onChange, label, hint }: { checked: boolean; onChange: (v: boolean) => void; label: ReactNode; hint?: ReactNode }) {
  return (
    <label className="flex cursor-pointer items-start gap-3 py-1.5">
      <input type="checkbox" className="mt-0.5 h-4 w-4 accent-accent-600" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>
        <span className="text-sm font-medium">{label}</span>
        {hint && <span className="block text-xs muted">{hint}</span>}
      </span>
    </label>
  )
}

export function Modal({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: ReactNode; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) d.showModal()
    if (!open && d.open) d.close()
  }, [open])
  return (
    <dialog
      ref={ref}
      onClose={onClose}
      onClick={(e) => e.target === ref.current && onClose()}
      className="m-auto w-[min(40rem,calc(100vw-2rem))] rounded-xl border border-stone-200 bg-white p-0 text-stone-900 shadow-xl backdrop:bg-black/40 dark:border-stone-800 dark:bg-stone-900 dark:text-stone-100"
    >
      <div className="flex items-center justify-between border-b border-stone-200 px-5 py-3 dark:border-stone-800">
        <h2 className="font-semibold">{title}</h2>
        <button className="btn-ghost px-2 py-1" onClick={onClose} aria-label="Close">✕</button>
      </div>
      <div className="max-h-[75vh] overflow-y-auto p-5">{children}</div>
    </dialog>
  )
}
