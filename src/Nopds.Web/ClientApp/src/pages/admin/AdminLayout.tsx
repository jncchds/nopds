import { NavLink, Outlet } from 'react-router'
import { useTranslation } from 'react-i18next'
import clsx from 'clsx'
import { PageTitle } from '../../components/ui'

export default function AdminLayout() {
  const { t } = useTranslation()
  const tabs = [
    { to: '/admin', label: t('admin.libraries'), end: true },
    { to: '/admin/settings', label: t('admin.settings') },
    { to: '/admin/users', label: t('admin.users') },
    { to: '/admin/logs', label: t('admin.logs') },
  ]
  return (
    <>
      <PageTitle>{t('nav.admin')}</PageTitle>
      <nav className="mb-6 flex gap-1 overflow-x-auto overflow-y-hidden border-b border-stone-200 dark:border-stone-800">
        {tabs.map((x) => (
          <NavLink
            key={x.to}
            to={x.to}
            end={x.end}
            className={({ isActive }) => clsx('-mb-px shrink-0 border-b-2 px-4 py-2 text-sm', isActive ? 'border-accent-600 font-medium' : 'border-transparent muted hover:text-stone-900 dark:hover:text-stone-100')}
          >
            {x.label}
          </NavLink>
        ))}
      </nav>
      <Outlet />
    </>
  )
}
