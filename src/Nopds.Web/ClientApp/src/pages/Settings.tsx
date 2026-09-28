import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import QRCode from 'qrcode'
import { Copy, KeyRound, RefreshCw } from 'lucide-react'
import { api, ApiError } from '../api/client'
import type { User } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { LANGUAGES, detectLanguage, rememberLanguage } from '../i18n'
import { PageTitle, Toggle } from '../components/ui'

function Section({ title, children, description }: { title: string; description?: ReactNode; children: ReactNode }) {
  return (
    <section className="card p-5">
      <h2 className="text-lg font-semibold">{title}</h2>
      {description && <p className="mt-1 mb-4 text-sm muted">{description}</p>}
      <div className={description ? '' : 'mt-4'}>{children}</div>
    </section>
  )
}

function CopyField({ value, label }: { value: string; label: string }) {
  const { t } = useTranslation()
  const [copied, setCopied] = useState(false)
  return (
    <label className="block">
      <span className="label">{label}</span>
      <div className="flex gap-2">
        <input className="input font-mono text-xs" readOnly value={value} onFocus={(e) => e.target.select()} />
        <button
          type="button"
          className="btn-secondary shrink-0"
          onClick={() => navigator.clipboard.writeText(value).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1500) })}
        >
          <Copy className="h-4 w-4" /> {copied ? t('common.copied') : t('common.copy')}
        </button>
      </div>
    </label>
  )
}

export default function Settings() {
  const { t, i18n } = useTranslation()
  const { user, setUser, feedToken, setFeedToken } = useAuth()
  const qc = useQueryClient()
  const origin = window.location.origin
  const [qr, setQr] = useState<string>()
  const [telegram, setTelegram] = useState(user?.telegramUsername ?? '')
  const [msg, setMsg] = useState<Record<string, string>>({})

  const opdsUrl = feedToken ? `${origin}/opds/t/${feedToken}/` : ''
  useEffect(() => {
    if (opdsUrl) QRCode.toDataURL(opdsUrl, { margin: 1, width: 180 }).then(setQr)
  }, [opdsUrl])

  if (!user) return null

  const update = async (patch: Partial<Pick<User, 'uiLanguage' | 'hideDuplicates' | 'telegramUsername'>> & { uiLanguage?: string }) => {
    const u = await api<User>('/me', { method: 'PUT', json: patch })
    setUser(u)
    qc.invalidateQueries({ queryKey: ['books'] })
    return u
  }

  const setLanguage = async (value: string) => {
    await update({ uiLanguage: value })
    rememberLanguage(value || null)
    await i18n.changeLanguage(value || detectLanguage())
  }

  const regenerate = async () => {
    if (!confirm(t('settings.regenerateConfirm'))) return
    const r = await api<{ token: string }>('/me/feed-token', { method: 'POST' })
    setFeedToken(r.token)
  }

  const changePassword = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    if (f.get('newPassword') !== f.get('confirm')) {
      setMsg({ password: t('settings.passwordMismatch') })
      return
    }
    try {
      await api('/me/password', { method: 'POST', json: { currentPassword: f.get('currentPassword') ?? undefined, newPassword: f.get('newPassword') } })
      if (!user.hasPassword) setUser({ ...user, hasPassword: true })
      setMsg({ password: t('settings.passwordChanged') })
      e.currentTarget.reset()
    } catch (err) {
      setMsg({ password: err instanceof ApiError ? JSON.stringify((err.body as { errors?: unknown })?.errors ?? err.message) : String(err) })
    }
  }

  const setKosync = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    await api('/me/kosync', { method: 'PUT', json: { password: f.get('kosync') } })
    setUser({ ...user, kosyncConfigured: !!f.get('kosync') })
    setMsg({ kosync: t('settings.saved') })
    e.currentTarget.reset()
  }

  return (
    <div className="max-w-3xl space-y-6">
      <PageTitle subtitle={user.userName}>{t('nav.settings')}</PageTitle>

      <Section title={t('settings.preferences')}>
        <label className="block max-w-xs">
          <span className="label">{t('settings.language')}</span>
          <select className="input" value={user.uiLanguage ?? ''} onChange={(e) => setLanguage(e.target.value)}>
            <option value="">{t('settings.systemLanguage')}</option>
            {LANGUAGES.map((l) => <option key={l.code} value={l.code}>{l.name}</option>)}
          </select>
        </label>
        <div className="mt-3">
          <Toggle checked={user.hideDuplicates} onChange={(v) => update({ hideDuplicates: v })} label={t('settings.hideDuplicates')} hint={t('settings.hideDuplicatesHint')} />
        </div>
      </Section>

      <Section title={t('settings.opds')} description={t('settings.opdsHint')}>
        <div className="flex flex-col gap-5 sm:flex-row">
          <div className="min-w-0 flex-1 space-y-3">
            <CopyField label={t('settings.opdsToken')} value={opdsUrl} />
            <CopyField label={t('settings.opdsBasic')} value={`${origin}/opds/`} />
            <CopyField label={t('settings.opds2')} value={feedToken ? `${origin}/opds/t/${feedToken}/v2/` : ''} />
            <button className="btn-ghost" onClick={regenerate}><RefreshCw className="h-4 w-4" /> {t('settings.regenerate')}</button>
          </div>
          {qr && <img src={qr} alt={t('settings.qr')} className="h-44 w-44 self-center rounded-lg bg-surface p-1" />}
        </div>
      </Section>

      <Section title={t('settings.kosync')} description={t('settings.kosyncHint', { url: `${origin}/kosync`, user: user.userName })}>
        <form onSubmit={setKosync} className="flex flex-wrap items-end gap-2">
          <label className="block flex-1">
            <span className="label">{t('settings.kosyncPassword')}</span>
            <input name="kosync" type="password" className="input" autoComplete="new-password" placeholder={user.kosyncConfigured ? '••••••••' : ''} />
          </label>
          <button className="btn-primary"><KeyRound className="h-4 w-4" /> {t('common.save')}</button>
        </form>
        <p className="mt-2 text-xs muted">{user.kosyncConfigured ? t('settings.kosyncOn') : t('settings.kosyncOff')} {msg.kosync}</p>
      </Section>

      <Section title={t('settings.telegram')} description={t('settings.telegramHint')}>
        <form
          onSubmit={async (e) => {
            e.preventDefault()
            await update({ telegramUsername: telegram })
            setMsg({ telegram: t('settings.saved') })
          }}
          className="flex flex-wrap items-end gap-2"
        >
          <label className="block flex-1">
            <span className="label">{t('settings.telegramUser')}</span>
            <input className="input" value={telegram} onChange={(e) => setTelegram(e.target.value)} placeholder="@username" />
          </label>
          <button className="btn-primary">{t('common.save')}</button>
        </form>
        {msg.telegram && <p className="mt-2 text-xs muted">{msg.telegram}</p>}
      </Section>

      <Section title={t('settings.password')} description={user.hasPassword ? undefined : t('settings.noPasswordHint')}>
        <form onSubmit={changePassword} className="grid gap-3 sm:grid-cols-3">
          {user.hasPassword && <input name="currentPassword" type="password" className="input" placeholder={t('settings.currentPassword')} autoComplete="current-password" required />}
          <input name="newPassword" type="password" className="input" placeholder={t('settings.newPassword')} autoComplete="new-password" minLength={6} required />
          <input name="confirm" type="password" className="input" placeholder={t('settings.confirmPassword')} autoComplete="new-password" required />
          <div className="flex items-center gap-3 sm:col-span-3">
            <button className="btn-primary">{user.hasPassword ? t('settings.changePassword') : t('settings.setPassword')}</button>
            {msg.password && <span className="text-sm muted">{msg.password}</span>}
          </div>
        </form>
      </Section>
    </div>
  )
}
