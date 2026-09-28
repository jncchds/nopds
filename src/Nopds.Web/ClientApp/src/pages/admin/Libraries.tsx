import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Folder, Pencil, Play, Plus, Square, Trash2 } from 'lucide-react'
import { api, ApiError } from '../../api/client'
import type { DirListing, LibraryDto, LibraryInput, ScanStatus } from '../../api/types'
import { useScanStatus } from '../../hooks/useScanStatus'
import { ErrorBox, Loading, Modal, Spinner, Toggle } from '../../components/ui'
import { formatDateTime } from '../../lib/format'

const EMPTY: LibraryInput = {
  name: '',
  rootPath: '',
  enabled: true,
  extensions: ['fb2', 'epub', 'mobi', 'azw3', 'pdf', 'djvu', 'txt', 'rtf', 'doc', 'docx', 'cbz'],
  scanZip: true,
  zipCodepage: 'cp866',
  inpxEnabled: true,
  inpxSkipUnchanged: true,
  inpxTestZip: false,
  inpxTestFiles: false,
  watchEnabled: false,
  scanCron: '0 0,12 * * *',
  deleteLogical: false,
  hashContent: false,
}

export default function Libraries() {
  const { t, i18n } = useTranslation()
  const qc = useQueryClient()
  const libs = useQuery({ queryKey: ['admin', 'libraries'], queryFn: () => api<LibraryDto[]>('/admin/libraries') })
  const status = useScanStatus()
  const [editing, setEditing] = useState<{ id?: number; value: LibraryInput } | null>(null)

  const scan = useMutation({ mutationFn: (id: number) => api(`/admin/libraries/${id}/scan`, { method: 'POST' }) })
  const cancel = useMutation({ mutationFn: (id: number) => api(`/admin/libraries/${id}/scan`, { method: 'DELETE' }) })
  const remove = useMutation({
    mutationFn: (id: number) => api(`/admin/libraries/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin', 'libraries'] })
      qc.invalidateQueries({ queryKey: ['libraries'] })
    },
  })

  if (libs.isLoading) return <Loading />
  if (libs.error) return <ErrorBox error={libs.error} />

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <button className="btn-primary" onClick={() => setEditing({ value: EMPTY })}><Plus className="h-4 w-4" /> {t('admin.addLibrary')}</button>
      </div>
      {libs.data!.length === 0 && <p className="muted">{t('admin.noLibraries')}</p>}
      {libs.data!.map((l) => {
        const s = status[l.id] as ScanStatus | undefined
        const running = s?.state === 'running' || s?.state === 'queued'
        return (
          <div key={l.id} className="card p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <h3 className="font-semibold">
                  {l.name} {!l.enabled && <span className="ml-2 text-xs muted">({t('admin.disabled')})</span>}
                </h3>
                <p className="truncate font-mono text-xs muted">{l.rootPath}</p>
                {!l.rootExists && (
                  <p className="mt-1 flex items-center gap-1 text-xs text-red-600"><AlertTriangle className="h-3 w-3" /> {t('admin.rootMissing')}</p>
                )}
                <p className="mt-1 text-sm">
                  {t('common.count', { count: l.books })}
                  {l.scanCron && <span className="ml-3 muted">⏱ {l.scanCron}</span>}
                  {l.watchEnabled && <span className="ml-3 muted">👁 {t('admin.watching')}</span>}
                </p>
              </div>
              <div className="flex gap-2">
                {running ? (
                  <button className="btn-secondary" onClick={() => cancel.mutate(l.id)}><Square className="h-4 w-4" /> {t('admin.stop')}</button>
                ) : (
                  <button className="btn-secondary" onClick={() => scan.mutate(l.id)}><Play className="h-4 w-4" /> {t('admin.scan')}</button>
                )}
                <button className="btn-ghost px-2" onClick={() => setEditing({ id: l.id, value: l })} aria-label={t('common.edit')}><Pencil className="h-4 w-4" /></button>
                <button className="btn-ghost px-2 text-red-600" onClick={() => confirm(t('admin.confirmDeleteLibrary', { name: l.name })) && remove.mutate(l.id)} aria-label={t('common.delete')}>
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </div>
            {s && s.state !== 'idle' ? (
              <ScanLine s={s} lang={i18n.language} />
            ) : (
              l.lastScanFinishedAt && (
                <p className="mt-3 text-xs muted">
                  {t('admin.lastScan')}: {formatDateTime(l.lastScanFinishedAt, i18n.language)} — {l.lastScanSummary}
                </p>
              )
            )}
          </div>
        )
      })}
      {editing && <LibraryForm initial={editing.value} id={editing.id} onClose={() => setEditing(null)} />}
    </div>
  )
}

function ScanLine({ s, lang }: { s: ScanStatus; lang: string }) {
  const { t } = useTranslation()
  const running = s.state === 'running'
  const nf = new Intl.NumberFormat(lang)
  return (
    <div className="mt-3 rounded-lg bg-stone-100 p-3 text-xs dark:bg-stone-800/60">
      <div className="mb-1 flex items-center gap-2 font-medium">
        {running && <Spinner className="h-3.5 w-3.5" />}
        {t(`scan.${s.state}`)}
        {s.subPath && <span className="font-mono muted">{s.subPath}</span>}
        {s.finishedAt && <span className="muted">· {formatDateTime(s.finishedAt, lang)}</span>}
      </div>
      <div className="flex flex-wrap gap-x-4 gap-y-1 tabular-nums">
        <span>{t('scan.files')}: {nf.format(s.filesSeen)}</span>
        <span className="text-green-700 dark:text-green-400">+{nf.format(s.booksAdded)}</span>
        <span>{t('scan.updated')}: {nf.format(s.booksUpdated)}</span>
        <span className="muted">{t('scan.skipped')}: {nf.format(s.booksSkipped)}</span>
        <span className="text-red-700 dark:text-red-400">−{nf.format(s.booksDeleted)}</span>
        <span>{t('scan.archives')}: {s.archivesScanned}/{s.archivesSkipped}</span>
        {s.errors > 0 && <span className="text-amber-700 dark:text-amber-400">{t('scan.errors')}: {s.errors}</span>}
      </div>
      {running && s.currentPath && <div className="mt-1 truncate font-mono muted">{s.currentPath}</div>}
      {s.message && <div className="mt-1 text-red-700 dark:text-red-400">{s.message}</div>}
    </div>
  )
}

function LibraryForm({ initial, id, onClose }: { initial: LibraryInput; id?: number; onClose: () => void }) {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [v, setV] = useState<LibraryInput>({ ...EMPTY, ...initial })
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [picker, setPicker] = useState(false)
  const set = <K extends keyof LibraryInput>(k: K, value: LibraryInput[K]) => setV((x) => ({ ...x, [k]: value }))

  const save = useMutation({
    mutationFn: () => api(id ? `/admin/libraries/${id}` : '/admin/libraries', { method: id ? 'PUT' : 'POST', json: v }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin', 'libraries'] })
      qc.invalidateQueries({ queryKey: ['libraries'] })
      onClose()
    },
    onError: (e) => {
      const body = e instanceof ApiError ? (e.body as { errors?: Record<string, string[]>; error?: string }) : undefined
      setErrors(body?.errors ?? { name: [body?.error ?? String(e)] })
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }
  const err = (k: string) => errors[k]?.[0] && <p className="mt-1 text-xs text-red-600">{errors[k][0]}</p>

  return (
    <Modal open onClose={onClose} title={id ? t('admin.editLibrary') : t('admin.addLibrary')}>
      <form onSubmit={submit} className="space-y-4">
        <label className="block">
          <span className="label">{t('admin.name')}</span>
          <input className="input" value={v.name} onChange={(e) => set('name', e.target.value)} required />
          {err('name')}
        </label>
        <label className="block">
          <span className="label">{t('admin.rootPath')}</span>
          <div className="flex gap-2">
            <input className="input font-mono text-xs" value={v.rootPath} onChange={(e) => set('rootPath', e.target.value)} required />
            <button type="button" className="btn-secondary shrink-0" onClick={() => setPicker(true)}><Folder className="h-4 w-4" /> {t('admin.browse')}</button>
          </div>
          {err('rootPath')}
        </label>
        <label className="block">
          <span className="label">{t('admin.extensions')}</span>
          <input className="input" value={v.extensions.join(' ')} onChange={(e) => set('extensions', e.target.value.split(/[\s,]+/).filter(Boolean))} />
        </label>
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block">
            <span className="label">{t('admin.cron')}</span>
            <input className="input font-mono" value={v.scanCron ?? ''} onChange={(e) => set('scanCron', e.target.value)} placeholder="0 0,12 * * *" />
            <span className="mt-1 block text-xs muted">{t('admin.cronHint')}</span>
            {err('scanCron')}
          </label>
          <label className="block">
            <span className="label">{t('admin.codepage')}</span>
            <select className="input" value={v.zipCodepage} onChange={(e) => set('zipCodepage', e.target.value)}>
              {['cp866', 'cp1251', 'cp437', 'cp852', 'utf-8'].map((c) => <option key={c}>{c}</option>)}
            </select>
          </label>
        </div>
        <div className="grid gap-x-4 sm:grid-cols-2">
          <Toggle checked={v.enabled} onChange={(x) => set('enabled', x)} label={t('admin.enabled')} />
          <Toggle checked={v.scanZip} onChange={(x) => set('scanZip', x)} label={t('admin.scanZip')} />
          <Toggle checked={v.inpxEnabled} onChange={(x) => set('inpxEnabled', x)} label={t('admin.inpx')} hint={t('admin.inpxHint')} />
          <Toggle checked={v.inpxSkipUnchanged} onChange={(x) => set('inpxSkipUnchanged', x)} label={t('admin.inpxSkip')} />
          <Toggle checked={v.inpxTestZip} onChange={(x) => set('inpxTestZip', x)} label={t('admin.inpxTestZip')} />
          <Toggle checked={v.inpxTestFiles} onChange={(x) => set('inpxTestFiles', x)} label={t('admin.inpxTestFiles')} />
          <Toggle checked={v.watchEnabled} onChange={(x) => set('watchEnabled', x)} label={t('admin.watch')} hint={t('admin.watchHint')} />
          <Toggle checked={v.deleteLogical} onChange={(x) => set('deleteLogical', x)} label={t('admin.softDelete')} hint={t('admin.softDeleteHint')} />
          <Toggle checked={v.hashContent} onChange={(x) => set('hashContent', x)} label={t('admin.hash')} hint={t('admin.hashHint')} />
        </div>
        <div className="flex justify-end gap-2 border-t border-stone-200 pt-4 dark:border-stone-800">
          <button type="button" className="btn-ghost" onClick={onClose}>{t('common.cancel')}</button>
          <button className="btn-primary" disabled={save.isPending}>{t('common.save')}</button>
        </div>
      </form>
      {picker && (
        <FolderPicker
          start={v.rootPath || '/'}
          onClose={() => setPicker(false)}
          onPick={(p) => {
            set('rootPath', p)
            if (!v.name) set('name', p.split('/').filter(Boolean).pop() ?? p)
            setPicker(false)
          }}
        />
      )}
    </Modal>
  )
}

function FolderPicker({ start, onPick, onClose }: { start: string; onPick: (p: string) => void; onClose: () => void }) {
  const { t } = useTranslation()
  const [path, setPath] = useState(start)
  const q = useQuery({ queryKey: ['fs', path], queryFn: () => api<DirListing>('/admin/fs', { query: { path } }), retry: false })
  return (
    <Modal open onClose={onClose} title={t('admin.chooseFolder')}>
      <div className="mb-3 flex items-center gap-2">
        <input className="input font-mono text-xs" value={q.data?.path ?? path} onChange={(e) => setPath(e.target.value)} />
        <button className="btn-primary shrink-0" onClick={() => onPick(q.data?.path ?? path)}>{t('admin.select')}</button>
      </div>
      {q.error && <ErrorBox error={q.error} />}
      <ul className="max-h-80 overflow-y-auto">
        {q.data?.parent && (
          <li><button className="w-full rounded px-2 py-1.5 text-left hover:bg-stone-100 dark:hover:bg-stone-800" onClick={() => setPath(q.data!.parent!)}>..</button></li>
        )}
        {q.data?.entries.map((e) => (
          <li key={e.path}>
            <button className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-left hover:bg-stone-100 dark:hover:bg-stone-800" onClick={() => setPath(e.path)}>
              <Folder className="h-4 w-4 text-accent-600" /> {e.name}
            </button>
          </li>
        ))}
      </ul>
    </Modal>
  )
}
