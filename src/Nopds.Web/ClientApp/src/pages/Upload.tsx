import { useRef, useState, type DragEvent, type FormEvent } from 'react'
import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { Lock, LockOpen, Upload as UploadIcon } from 'lucide-react'
import { api } from '../api/client'
import { useConfig } from '../api/hooks'
import type { UploadDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { Empty, ErrorBox, Loading, PageTitle, Spinner, Toggle } from '../components/ui'
import { formatDate, formatSize } from '../lib/format'

/** Scanned recently enough that a missing book means "still processing" rather than "not recognized". */
const pending = (u: UploadDto) => !u.bookId && !u.missing && Date.now() - Date.parse(u.uploadedAt) < 2 * 60_000

/** Upload books into the shared upload library and manage their privacy. */
export default function Upload() {
  const { t, i18n } = useTranslation()
  const { user } = useAuth()
  const config = useConfig().data?.uploads
  const qc = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const [files, setFiles] = useState<File[]>([])
  const [isPrivate, setIsPrivate] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [showAll, setShowAll] = useState(false)

  const list = useQuery({
    queryKey: ['uploads', showAll],
    queryFn: () => api<UploadDto[]>('/uploads', { query: { all: showAll || undefined } }),
    // Freshly uploaded files show up as books once the scan has read them.
    refetchInterval: (q) => (q.state.data?.some(pending) ? 2000 : false),
  })

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['uploads'] })
    qc.invalidateQueries({ queryKey: ['books'] })
    qc.invalidateQueries({ queryKey: ['book'] })
    qc.invalidateQueries({ queryKey: ['stats'] })
    qc.invalidateQueries({ queryKey: ['libraries'] })
  }

  const upload = useMutation({
    mutationFn: () => {
      const form = new FormData()
      files.forEach((f) => form.append('files', f, f.name))
      form.append('isPrivate', String(isPrivate))
      return api<UploadDto[]>('/uploads', { method: 'POST', body: form })
    },
    onSuccess: () => {
      setFiles([])
      if (input.current) input.current.value = ''
      invalidate()
    },
  })

  const privacy = useMutation({
    mutationFn: (u: UploadDto) => api<UploadDto>(`/uploads/${u.id}`, { method: 'PUT', json: { isPrivate: !u.isPrivate } }),
    onSuccess: invalidate,
  })

  if (!config) return <Empty>{t('upload.disabled')}</Empty>

  const accept = config.extensions.map((e) => '.' + e).join(',')
  const total = files.reduce((n, f) => n + f.size, 0)
  const tooBig = total > config.maxMegabytes * 1024 * 1024
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (files.length > 0 && !tooBig) upload.mutate()
  }
  const drop = (e: DragEvent) => {
    e.preventDefault()
    setDragging(false)
    const ok = Array.from(e.dataTransfer.files).filter((f) => config.extensions.includes(f.name.split('.').pop()?.toLowerCase() ?? ''))
    if (ok.length) setFiles(ok)
  }

  return (
    <div className="space-y-8">
      <PageTitle subtitle={t('upload.subtitle')}>{t('nav.upload')}</PageTitle>

      <form onSubmit={submit} className="card max-w-2xl space-y-4 p-5">
        <div
          className={clsx(
            'flex cursor-pointer flex-col items-center gap-2 rounded-lg border-2 border-dashed px-4 py-8 text-center transition-colors',
            dragging ? 'border-accent bg-accent-faint' : 'border-line hover:border-accent',
          )}
          onClick={() => input.current?.click()}
          onDragOver={(e) => {
            e.preventDefault()
            setDragging(true)
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={drop}
        >
          <UploadIcon className="h-8 w-8 text-accent" />
          <span className="font-medium">{files.length ? files.map((f) => f.name).join(', ') : t('upload.choose')}</span>
          <span className="text-xs muted">
            {config.extensions.map((e) => e.toUpperCase()).join(' · ')} — {t('upload.maxSize', { size: config.maxMegabytes })}
          </span>
          <input ref={input} type="file" multiple accept={accept} className="hidden" onChange={(e) => setFiles(Array.from(e.target.files ?? []))} />
        </div>
        <Toggle checked={isPrivate} onChange={setIsPrivate} label={t('upload.private')} hint={t('upload.privateHint')} />
        {tooBig && <p className="text-sm text-red-600">{t('upload.tooBig', { size: config.maxMegabytes })}</p>}
        {upload.error && <ErrorBox error={upload.error} />}
        <div className="flex justify-end">
          <button className="btn-primary" disabled={files.length === 0 || tooBig || upload.isPending}>
            <UploadIcon className="h-4 w-4" />
            {upload.isPending ? t('upload.uploading') : t('upload.submit')}
          </button>
        </div>
      </form>

      <section className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-lg font-semibold">{showAll ? t('upload.all') : t('upload.mine')}</h2>
          {user?.isAdmin && (
            <button className="btn-ghost" onClick={() => setShowAll((v) => !v)}>{showAll ? t('upload.mine') : t('upload.all')}</button>
          )}
        </div>
        {list.isLoading ? (
          <Loading />
        ) : list.error ? (
          <ErrorBox error={list.error} />
        ) : list.data!.length === 0 ? (
          <Empty>{t('upload.empty')}</Empty>
        ) : (
          <ul className="space-y-2">
            {list.data!.map((u) => (
              <li key={u.id} className="card flex flex-wrap items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  {u.bookId ? (
                    <Link to={`/book/${u.bookId}`} className="block truncate font-semibold hover:text-accent">{u.title ?? u.fileName}</Link>
                  ) : (
                    <span className="block truncate font-semibold">{u.title ?? u.fileName}</span>
                  )}
                  <span className="block truncate text-xs muted">
                    {u.fileName} · {formatSize(u.fileSize)} · {formatDate(u.uploadedAt, i18n.language)}
                    {showAll && u.uploadedBy && ` · ${u.uploadedBy}`}
                  </span>
                </div>
                {u.missing ? (
                  <span className="pill-muted">{t('upload.missing')}</span>
                ) : pending(u) ? (
                  <span className="pill-muted flex items-center gap-1"><Spinner className="h-3 w-3" /> {t('upload.processing')}</span>
                ) : (
                  !u.bookId && <span className="pill-muted">{t('upload.unrecognized')}</span>
                )}
                <button
                  className="btn-secondary"
                  onClick={() => privacy.mutate(u)}
                  disabled={privacy.isPending}
                  title={u.isPrivate ? t('upload.makePublic') : t('upload.makePrivate')}
                >
                  {u.isPrivate ? <Lock className="h-4 w-4" /> : <LockOpen className="h-4 w-4" />}
                  {u.isPrivate ? t('upload.isPrivate') : t('upload.isPublic')}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
