import { StrictMode, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@fontsource-variable/inter'
import '@fontsource-variable/literata'
import './index.css'
import i18n, { detectLanguage } from './i18n'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { applyTheme } from './hooks/useTheme'
import { ApiError } from './api/client'
import App from './App'

applyTheme((localStorage.getItem('nopds.theme') as 'light' | 'dark' | null) ?? 'system')

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: (count, err) => !(err instanceof ApiError && err.status < 500) && count < 2,
      refetchOnWindowFocus: false,
    },
  },
})

/** Applies the account's UI language after sign-in (system language otherwise). */
function LanguageSync() {
  const { user } = useAuth()
  useEffect(() => {
    const lang = detectLanguage(user?.uiLanguage)
    if (lang !== i18n.language) i18n.changeLanguage(lang)
  }, [user?.uiLanguage])
  return null
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <LanguageSync />
          <App />
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
