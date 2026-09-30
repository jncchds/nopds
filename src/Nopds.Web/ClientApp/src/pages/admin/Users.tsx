import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Lock, Pencil, Plus, Trash2, Unlock } from 'lucide-react'
import { api, ApiError } from '../../api/client'
import type { AdminUser, LibraryDto } from '../../api/types'
import { useAuth } from '../../auth/AuthContext'
import { ErrorBox, Loading, Modal, Toggle } from '../../components/ui'
import { formatDate } from '../../lib/format'

export default function Users() {
  const { t, i18n } = useTranslation()
  const { user: me } = useAuth()
  const qc = useQueryClient()
  const users = useQuery({ queryKey: ['admin', 'users'], queryFn: () => api<AdminUser[]>('/admin/users') })
  const [editing, setEditing] = useState<AdminUser | 'new' | null>(null)
  const invalidate = () => qc.invalidateQueries({ queryKey: ['admin', 'users'] })
  const lock = useMutation({ mutationFn: (u: AdminUser) => api(`/admin/users/${u.id}`, { method: 'PUT', json: { locked: !u.locked } }), onSuccess: invalidate })
  const approve = useMutation({ mutationFn: (id: string) => api(`/admin/users/${id}`, { method: 'PUT', json: { approved: true } }), onSuccess: invalidate })
  const remove = useMutation({ mutationFn: (id: string) => api(`/admin/users/${id}`, { method: 'DELETE' }), onSuccess: invalidate })

  if (users.isLoading) return <Loading />
  if (users.error) return <ErrorBox error={users.error} />
  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <button className="btn-primary" onClick={() => setEditing('new')}><Plus className="h-4 w-4" /> {t('admin.addUser')}</button>
      </div>
      <div className="card divide-y divide-line">
        {users.data!.map((u) => (
          <div key={u.id} className="flex flex-wrap items-center gap-3 p-3">
            <div className="min-w-0 flex-1">
              <div className="font-medium">
                {u.userName}
                {u.isAdmin && <span className="ml-2 rounded bg-accent-faint px-1.5 py-0.5 text-xs text-accent">{t('admin.admin')}</span>}
                {!u.approved && <span className="ml-2 rounded bg-amber-100 px-1.5 py-0.5 text-xs text-amber-900 dark:bg-amber-900/50 dark:text-amber-200">{t('admin.pending')}</span>}
                {u.locked && <span className="ml-2 rounded bg-red-100 px-1.5 py-0.5 text-xs text-red-800 dark:bg-red-900/50 dark:text-red-200">{t('admin.locked')}</span>}
              </div>
              <div className="text-xs muted">
                {formatDate(u.createdAt, i18n.language)} · {u.allowedLibraryIds ? t('admin.someLibraries', { count: u.allowedLibraryIds.length }) : t('admin.allLibraries')}
                {u.sso && ` · ${u.sso}`}
                {u.email && ` · ${u.email}`}
                {u.telegram && ` · Telegram${u.telegramUsername ? ` @${u.telegramUsername}` : ''}`}
              </div>
            </div>
            {!u.approved && (
              <button className="btn-primary" onClick={() => approve.mutate(u.id)}><Check className="h-4 w-4" /> {t('admin.approve')}</button>
            )}
            <button className="btn-ghost px-2" onClick={() => setEditing(u)} aria-label={t('common.edit')}><Pencil className="h-4 w-4" /></button>
            {u.id !== me?.id && (
              <>
                <button className="btn-ghost px-2" onClick={() => lock.mutate(u)} aria-label={u.locked ? t('admin.unlock') : t('admin.lock')}>
                  {u.locked ? <Unlock className="h-4 w-4" /> : <Lock className="h-4 w-4" />}
                </button>
                <button className="btn-ghost px-2 text-red-600" onClick={() => confirm(t('admin.confirmDeleteUser', { name: u.userName })) && remove.mutate(u.id)} aria-label={t('common.delete')}>
                  <Trash2 className="h-4 w-4" />
                </button>
              </>
            )}
          </div>
        ))}
      </div>
      {editing && <UserForm user={editing === 'new' ? undefined : editing} onClose={() => { setEditing(null); invalidate() }} />}
    </div>
  )
}

function UserForm({ user, onClose }: { user?: AdminUser; onClose: () => void }) {
  const { t } = useTranslation()
  const libs = useQuery({ queryKey: ['admin', 'libraries'], queryFn: () => api<LibraryDto[]>('/admin/libraries') })
  const [userName, setUserName] = useState(user?.userName ?? '')
  const [password, setPassword] = useState('')
  const [isAdmin, setIsAdmin] = useState(user?.isAdmin ?? false)
  const [all, setAll] = useState(!user?.allowedLibraryIds)
  const [allowed, setAllowed] = useState<number[]>(user?.allowedLibraryIds ?? [])
  const [error, setError] = useState<string>()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    try {
      await api(user ? `/admin/users/${user.id}` : '/admin/users', {
        method: user ? 'PUT' : 'POST',
        json: { userName, password: password || undefined, isAdmin, allLibraries: all, allowedLibraryIds: allowed },
      })
      onClose()
    } catch (err) {
      const body = err instanceof ApiError ? (err.body as { errors?: Record<string, string[]> }) : undefined
      setError(body?.errors ? Object.values(body.errors).flat().join(' ') : String((err as Error).message))
    }
  }

  return (
    <Modal open onClose={onClose} title={user ? t('admin.editUser') : t('admin.addUser')}>
      <form onSubmit={submit} className="space-y-4">
        <label className="block"><span className="label">{t('auth.userName')}</span><input className="input" value={userName} onChange={(e) => setUserName(e.target.value)} required /></label>
        <label className="block">
          <span className="label">{t('auth.password')}</span>
          <input className="input" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} required={!user} placeholder={user ? t('admin.keepPassword') : ''} />
        </label>
        <Toggle checked={isAdmin} onChange={setIsAdmin} label={t('admin.admin')} />
        <Toggle checked={all} onChange={setAll} label={t('admin.allLibraries')} />
        {!all && (
          <div className="ml-7 space-y-1">
            {libs.data?.map((l) => (
              <Toggle key={l.id} checked={allowed.includes(l.id)} onChange={(v) => setAllowed(v ? [...allowed, l.id] : allowed.filter((x) => x !== l.id))} label={l.name} />
            ))}
          </div>
        )}
        {error && <p className="text-sm text-red-600">{error}</p>}
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-ghost" onClick={onClose}>{t('common.cancel')}</button>
          <button className="btn-primary">{t('common.save')}</button>
        </div>
      </form>
    </Modal>
  )
}
