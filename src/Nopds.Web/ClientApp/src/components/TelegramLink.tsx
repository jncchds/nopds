import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import QRCode from 'qrcode'
import { Link2, Send, Unlink } from 'lucide-react'
import { api } from '../api/client'
import type { User } from '../api/types'
import { useAuth } from '../auth/AuthContext'

interface Pending { url: string; token: string; expiresAt: string; qr: string }

/** Links the account through a one-time bot deep link and waits for the bot to confirm it. */
export function TelegramLink({ bot }: { bot?: string }) {
  const { t } = useTranslation()
  const { user, setUser } = useAuth()
  const [pending, setPending] = useState<Pending>()

  useEffect(() => {
    if (!pending) return
    const timer = setInterval(async () => {
      if (Date.parse(pending.expiresAt) < Date.now()) {
        setPending(undefined)
        return
      }
      const r = await api<{ linked: boolean }>(`/me/telegram/${pending.token}`)
      if (r.linked) {
        setUser(await api<User>('/me'))
        setPending(undefined)
      }
    }, 3000)
    return () => clearInterval(timer)
  }, [pending, setUser])

  if (!user) return null

  const start = async () => {
    const r = await api<{ url: string; token: string; expiresAt: string }>('/me/telegram', { method: 'POST' })
    const qr = await QRCode.toDataURL(r.url, { margin: 1, width: 180 })
    setPending({ ...r, qr })
    window.open(r.url, '_blank', 'noopener')
  }

  const unlink = async () => {
    if (!confirm(t('settings.telegramUnlinkConfirm'))) return
    await api('/me/telegram', { method: 'DELETE' })
    setUser({ ...user, telegramLinked: false, telegramUsername: undefined })
  }

  return (
    <div className="space-y-3">
      <p className="text-sm">
        {user.telegramLinked
          ? user.telegramUsername ? t('settings.telegramLinkedAs', { name: `@${user.telegramUsername}` }) : t('settings.telegramLinked')
          : t('settings.telegramNotLinked')}
      </p>
      <div className="flex flex-wrap gap-2">
        {bot && (
          <button className="btn-primary" onClick={start}>
            <Link2 className="h-4 w-4" /> {user.telegramLinked ? t('settings.telegramRelink') : t('settings.telegramLink')}
          </button>
        )}
        {user.telegramLinked && (
          <button className="btn-ghost" onClick={unlink}><Unlink className="h-4 w-4" /> {t('settings.telegramUnlink')}</button>
        )}
      </div>
      {pending && (
        <div className="flex flex-col gap-4 rounded-lg border border-line p-4 sm:flex-row sm:items-center">
          <img src={pending.qr} alt={t('settings.qr')} className="h-44 w-44 self-center rounded-lg bg-surface p-1" />
          <div className="space-y-2 text-sm">
            <p>{t('settings.telegramPending')}</p>
            <a className="btn-secondary" href={pending.url} target="_blank" rel="noopener noreferrer">
              <Send className="h-4 w-4" /> {t('settings.telegramOpen', { bot: `@${bot}` })}
            </a>
            <p className="text-xs muted">{t('settings.telegramWaiting')}</p>
          </div>
        </div>
      )}
    </div>
  )
}
