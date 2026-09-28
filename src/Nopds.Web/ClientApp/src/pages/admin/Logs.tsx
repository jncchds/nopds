import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery } from '@tanstack/react-query'
import { RefreshCw } from 'lucide-react'
import { api } from '../../api/client'
import { ErrorBox, Loading } from '../../components/ui'

export default function Logs() {
  const { t } = useTranslation()
  const [filter, setFilter] = useState('')
  const q = useQuery({ queryKey: ['admin', 'logs'], queryFn: () => api<string[]>('/admin/logs', { query: { lines: 1000 } }), refetchInterval: 10_000 })
  const cleanup = useMutation({ mutationFn: () => api('/admin/maintenance/cleanup', { method: 'POST' }) })
  const lines = (q.data ?? []).filter((l) => !filter || l.toLowerCase().includes(filter.toLowerCase()))

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        <input className="input max-w-xs" placeholder={t('admin.filter')} value={filter} onChange={(e) => setFilter(e.target.value)} />
        <button className="btn-secondary" onClick={() => q.refetch()}><RefreshCw className="h-4 w-4" /> {t('admin.refresh')}</button>
        <button className="btn-ghost ml-auto" onClick={() => cleanup.mutate()} disabled={cleanup.isPending}>
          {cleanup.isSuccess ? t('admin.cleanupDone') : t('admin.cleanup')}
        </button>
      </div>
      {q.isLoading ? <Loading /> : q.error ? <ErrorBox error={q.error} /> : (
        <pre className="card max-h-[70vh] overflow-auto p-3 text-xs leading-relaxed">
          {lines.map((l, i) => (
            <div key={i} className={l.includes(' ERR]') || l.includes(' FTL]') ? 'text-red-600 dark:text-red-400' : l.includes(' WRN]') ? 'text-amber-700 dark:text-amber-400' : ''}>{l}</div>
          ))}
        </pre>
      )}
    </div>
  )
}
