import { lazy, Suspense, type ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useAuth } from './auth/AuthContext'
import { useConfig } from './api/hooks'
import { Layout } from './components/Layout'
import { Loading } from './components/ui'
import Home from './pages/Home'
import Login from './pages/Login'

const Books = lazy(() => import('./pages/Books'))
const Names = lazy(() => import('./pages/Names'))
const AuthorPage = lazy(() => import('./pages/AuthorPage'))
const SeriesPage = lazy(() => import('./pages/SeriesPage'))
const Genres = lazy(() => import('./pages/Genres'))
const Folders = lazy(() => import('./pages/Folders'))
const Search = lazy(() => import('./pages/Search'))
const BookPage = lazy(() => import('./pages/BookPage'))
const Shelf = lazy(() => import('./pages/Shelf'))
const Upload = lazy(() => import('./pages/Upload'))
const Settings = lazy(() => import('./pages/Settings'))
const Reader = lazy(() => import('./pages/Reader'))
const AdminLayout = lazy(() => import('./pages/admin/AdminLayout'))
const Libraries = lazy(() => import('./pages/admin/Libraries'))
const AppSettingsPage = lazy(() => import('./pages/admin/AppSettingsPage'))
const Users = lazy(() => import('./pages/admin/Users'))
const Logs = lazy(() => import('./pages/admin/Logs'))

/** Private libraries need a signed-in user; public ones allow anonymous browsing. */
function Guard({ children, user, admin }: { children: ReactNode; user?: boolean; admin?: boolean }) {
  const auth = useAuth()
  const config = useConfig()
  const location = useLocation()
  if (!auth.ready || config.isLoading) return <Loading />
  const needsLogin = (user || admin || config.data?.access !== 'public') && !auth.user
  if (needsLogin) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  if (admin && !auth.user?.isAdmin) return <Navigate to="/" replace />
  return <>{children}</>
}

function NotFound() {
  const { t } = useTranslation()
  return <div className="py-24 text-center"><h1 className="text-4xl font-semibold">404</h1><p className="mt-2 muted">{t('common.notFound')}</p></div>
}

export default function App() {
  return (
    <Suspense fallback={<Loading />}>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/read/:id" element={<Guard><Reader /></Guard>} />
        <Route element={<Guard><Layout /></Guard>}>
          <Route index element={<Home />} />
          <Route path="books" element={<Books />} />
          <Route path="authors" element={<Names kind="authors" />} />
          <Route path="authors/:id" element={<AuthorPage />} />
          <Route path="series" element={<Names kind="series" />} />
          <Route path="series/:id" element={<SeriesPage />} />
          <Route path="genres" element={<Genres />} />
          <Route path="genres/:section" element={<Genres />} />
          <Route path="folders" element={<Folders />} />
          <Route path="folders/:libraryId" element={<Folders />} />
          <Route path="folders/:libraryId/:catalogId" element={<Folders />} />
          <Route path="search" element={<Search />} />
          <Route path="book/:id" element={<BookPage />} />
          <Route path="shelf" element={<Guard user><Shelf /></Guard>} />
          <Route path="upload" element={<Guard user><Upload /></Guard>} />
          <Route path="settings" element={<Guard user><Settings /></Guard>} />
          <Route path="admin" element={<Guard admin><AdminLayout /></Guard>}>
            <Route index element={<Libraries />} />
            <Route path="settings" element={<AppSettingsPage />} />
            <Route path="users" element={<Users />} />
            <Route path="logs" element={<Logs />} />
          </Route>
          <Route path="*" element={<NotFound />} />
        </Route>
      </Routes>
    </Suspense>
  )
}
