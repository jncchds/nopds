import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../api/client'
import { Logo } from '../components/Logo'
import { useConfig } from '../api/hooks'

export default function Login() {
  const { t } = useTranslation()
  const { login } = useAuth()
  const config = useConfig()
  const navigate = useNavigate()
  const location = useLocation()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(userName, password)
      const from = (location.state as { from?: string } | null)?.from
      navigate(from && from !== '/login' ? from : '/', { replace: true })
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401 ? t('auth.invalid') : err instanceof ApiError && err.status === 423 ? t('auth.locked') : String((err as Error).message))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center px-4">
      <form onSubmit={submit} className="card w-full max-w-sm space-y-4 p-6">
        <div className="flex flex-col items-center gap-2 text-center">
          <Logo className="h-12 w-12" />
          <h1 className="text-2xl font-semibold">{config.data?.title ?? '.NET OPDS'}</h1>
          <p className="text-sm muted">{t('auth.prompt')}</p>
        </div>
        <label className="block">
          <span className="label">{t('auth.userName')}</span>
          <input className="input" autoComplete="username" value={userName} onChange={(e) => setUserName(e.target.value)} required autoFocus />
        </label>
        <label className="block">
          <span className="label">{t('auth.password')}</span>
          <input className="input" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        {error && <p className="text-sm text-red-600 dark:text-red-400" role="alert">{error}</p>}
        <button className="btn-primary w-full" disabled={busy}>{t('auth.login')}</button>
      </form>
    </div>
  )
}
