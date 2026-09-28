import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { api } from '../../api/client'
import type { AppSettings } from '../../api/types'
import { ErrorBox, Loading, Toggle } from '../../components/ui'

export default function AppSettingsPage() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['admin', 'settings'], queryFn: () => api<AppSettings>('/admin/settings') })
  if (q.isLoading) return <Loading />
  if (q.error) return <ErrorBox error={q.error} />
  return <SettingsForm initial={q.data!} onSaved={() => qc.invalidateQueries({ queryKey: ['config'] })} t={t} />
}

function SettingsForm({ initial, onSaved, t }: { initial: AppSettings; onSaved: () => void; t: (k: string) => string }) {
  const [s, setS] = useState<AppSettings>(initial)
  const [saved, setSaved] = useState(false)

  const set = <K extends keyof AppSettings>(k: K, v: AppSettings[K]) => setS({ ...s, [k]: v })
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const r = await api<AppSettings>('/admin/settings', { method: 'PUT', json: s })
    setS(r)
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
    onSaved()
  }

  return (
    <form onSubmit={submit} className="max-w-3xl space-y-6">
      <section className="card space-y-4 p-5">
        <h2 className="font-semibold">{t('admin.general')}</h2>
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block"><span className="label">{t('admin.siteTitle')}</span><input className="input" value={s.title} onChange={(e) => set('title', e.target.value)} /></label>
          <label className="block"><span className="label">{t('admin.siteSubtitle')}</span><input className="input" value={s.subtitle} onChange={(e) => set('subtitle', e.target.value)} /></label>
          <label className="block">
            <span className="label">{t('admin.access')}</span>
            <select className="input" value={s.access} onChange={(e) => set('access', e.target.value as AppSettings['access'])}>
              <option value="private">{t('admin.accessPrivate')}</option>
              <option value="public">{t('admin.accessPublic')}</option>
            </select>
          </label>
          <label className="block"><span className="label">{t('admin.pageSize')}</span><input type="number" className="input" value={s.maxItems} onChange={(e) => set('maxItems', Number(e.target.value))} /></label>
          <label className="block"><span className="label">{t('admin.splitItems')}</span><input type="number" className="input" value={s.splitItems} onChange={(e) => set('splitItems', Number(e.target.value))} /></label>
          <label className="block"><span className="label">{t('admin.preferredFormats')}</span><input className="input" value={s.preferredFormats.join(' ')} onChange={(e) => set('preferredFormats', e.target.value.split(/[\s,]+/).filter(Boolean))} /></label>
        </div>
        <Toggle checked={s.alphabetMenu} onChange={(v) => set('alphabetMenu', v)} label={t('admin.alphabetMenu')} />
        <Toggle checked={s.titleAsFilename} onChange={(v) => set('titleAsFilename', v)} label={t('admin.titleAsFilename')} />
        <Toggle checked={s.showCovers} onChange={(v) => set('showCovers', v)} label={t('admin.showCovers')} />
        <Toggle checked={s.hideDuplicates} onChange={(v) => set('hideDuplicates', v)} label={t('admin.hideDuplicates')} />
      </section>

      <section className="card space-y-4 p-5">
        <h2 className="font-semibold">{t('admin.conversion')}</h2>
        <Toggle checked={s.conversion.builtInFb2ToEpub} onChange={(v) => set('conversion', { ...s.conversion, builtInFb2ToEpub: v })} label={t('admin.builtIn')} />
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block"><span className="label">{t('admin.cacheSize')}</span><input type="number" className="input" value={s.conversion.cacheSizeMb} onChange={(e) => set('conversion', { ...s.conversion, cacheSizeMb: Number(e.target.value) })} /></label>
          <label className="block"><span className="label">{t('admin.timeout')}</span><input type="number" className="input" value={s.conversion.timeoutSeconds} onChange={(e) => set('conversion', { ...s.conversion, timeoutSeconds: Number(e.target.value) })} /></label>
        </div>
        <div>
          <span className="label">{t('admin.external')}</span>
          <p className="mb-2 text-xs muted">{t('admin.externalHint')}</p>
          {s.conversion.external.map((c, i) => (
            <div key={i} className="mb-2 flex gap-2">
              <input className="input w-20" value={c.source} onChange={(e) => set('conversion', { ...s.conversion, external: s.conversion.external.map((x, j) => (j === i ? { ...x, source: e.target.value } : x)) })} />
              <span className="self-center">→</span>
              <input className="input w-20" value={c.target} onChange={(e) => set('conversion', { ...s.conversion, external: s.conversion.external.map((x, j) => (j === i ? { ...x, target: e.target.value } : x)) })} />
              <input className="input font-mono text-xs" value={c.command} placeholder="ebook-convert {input} {output}" onChange={(e) => set('conversion', { ...s.conversion, external: s.conversion.external.map((x, j) => (j === i ? { ...x, command: e.target.value } : x)) })} />
              <button type="button" className="btn-ghost px-2" onClick={() => set('conversion', { ...s.conversion, external: s.conversion.external.filter((_, j) => j !== i) })}><Trash2 className="h-4 w-4" /></button>
            </div>
          ))}
          <button type="button" className="btn-secondary" onClick={() => set('conversion', { ...s.conversion, external: [...s.conversion.external, { source: 'epub', target: 'azw3', command: 'ebook-convert {input} {output}' }] })}>
            <Plus className="h-4 w-4" /> {t('admin.addConverter')}
          </button>
        </div>
      </section>

      <section className="card space-y-4 p-5">
        <h2 className="font-semibold">{t('admin.telegram')}</h2>
        <Toggle checked={s.telegram.enabled} onChange={(v) => set('telegram', { ...s.telegram, enabled: v })} label={t('admin.telegramEnabled')} />
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block"><span className="label">{t('admin.botToken')}</span><input className="input font-mono text-xs" type="password" autoComplete="off" value={s.telegram.botToken ?? ''} onChange={(e) => set('telegram', { ...s.telegram, botToken: e.target.value })} /></label>
          <label className="block"><span className="label">{t('admin.botMaxItems')}</span><input type="number" className="input" value={s.telegram.maxItems} onChange={(e) => set('telegram', { ...s.telegram, maxItems: Number(e.target.value) })} /></label>
        </div>
        <Toggle checked={s.telegram.requireLinkedUser} onChange={(v) => set('telegram', { ...s.telegram, requireLinkedUser: v })} label={t('admin.botAuth')} hint={t('admin.botAuthHint')} />
      </section>

      <section className="card space-y-4 p-5">
        <h2 className="font-semibold">{t('admin.sso')}</h2>
        <Toggle checked={s.sso.requireApproval} onChange={(v) => set('sso', { ...s.sso, requireApproval: v })} label={t('admin.ssoRequireApproval')} hint={t('admin.ssoRequireApprovalHint')} />
      </section>

      <div className="flex items-center gap-3">
        <button className="btn-primary">{t('common.save')}</button>
        {saved && <span className="text-sm text-green-700 dark:text-green-400">{t('settings.saved')}</span>}
      </div>
    </form>
  )
}
