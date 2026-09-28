import { useTranslation } from 'react-i18next'
import { useRegisterSW } from 'virtual:pwa-register/react'

/** Shows a banner when a new app version has been downloaded by the service worker. */
export function UpdatePrompt() {
  const { t } = useTranslation()
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW({ immediate: true })
  if (!needRefresh) return null
  return (
    <div className="fixed right-4 bottom-4 left-4 z-50 mx-auto flex max-w-md items-center justify-between gap-3 card p-3 text-sm" role="status">
      <span>{t('pwa.update')}</span>
      <div className="flex gap-2">
        <button className="btn-ghost" onClick={() => setNeedRefresh(false)}>{t('common.later')}</button>
        <button className="btn-primary" onClick={() => updateServiceWorker(true)}>{t('pwa.reload')}</button>
      </div>
    </div>
  )
}
