import { Link, useParams, useSearchParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import { Archive, FileArchive, Folder, ListTree } from 'lucide-react'
import { useCatalog, useLibraries } from '../api/hooks'
import { BookGrid } from '../components/BookCard'
import { Empty, ErrorBox, Loading, PageTitle } from '../components/ui'
import type { CatalogNode } from '../api/types'

export default function Folders() {
  const { libraryId, catalogId } = useParams()
  const { t } = useTranslation()
  const libraries = useLibraries()

  if (!libraryId) {
    if (libraries.isLoading) return <Loading />
    if (libraries.data?.length === 1) return <FolderView libraryId={libraries.data[0].id} />
    return (
      <>
        <PageTitle>{t('nav.folders')}</PageTitle>
        <div className="grid gap-3 sm:grid-cols-2">
          {libraries.data?.map((l) => (
            <Link key={l.id} to={`/folders/${l.id}`} className="card flex items-center gap-3 p-4 hover:border-accent-400">
              <Folder className="h-5 w-5 text-accent-600" />
              <span className="flex-1 font-medium">{l.name}</span>
              <span className="text-sm muted">{t('common.count', { count: l.books })}</span>
            </Link>
          ))}
        </div>
      </>
    )
  }
  return <FolderView libraryId={Number(libraryId)} catalogId={catalogId ? Number(catalogId) : undefined} />
}

const icons = { directory: Folder, zip: FileArchive, inpx: ListTree, inp: Archive }

function FolderView({ libraryId, catalogId }: { libraryId: number; catalogId?: number }) {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? 1)
  const q = useCatalog(libraryId, catalogId, page)
  if (q.isLoading) return <Loading />
  if (q.error) return <ErrorBox error={q.error} />
  const data = q.data!
  const link = (c: CatalogNode) => `/folders/${libraryId}/${c.id}`

  return (
    <>
      <PageTitle>{data.current && data.current.path !== '.' ? data.current.name : t('nav.folders')}</PageTitle>
      {data.breadcrumbs.length > 0 && (
        <nav className="mb-4 flex flex-wrap items-center gap-1 text-sm" aria-label={t('folders.path')}>
          {data.breadcrumbs.map((b) => (
            <span key={b.id} className="flex items-center gap-1">
              <Link className="link" to={link(b)}>{b.path === '.' ? t('folders.root') : b.name}</Link>
              <span className="muted">/</span>
            </span>
          ))}
          <span>{data.current?.name}</span>
        </nav>
      )}
      {data.children.length > 0 && (
        <ul className="mb-8 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {data.children.map((c) => {
            const Icon = icons[c.type] ?? Folder
            return (
              <li key={c.id}>
                <Link to={link(c)} className="card flex items-center gap-3 px-3 py-2.5 hover:border-accent-400">
                  <Icon className="h-4 w-4 shrink-0 text-accent-600" />
                  <span className="truncate">{c.name}</span>
                </Link>
              </li>
            )
          })}
        </ul>
      )}
      {data.books.items.length > 0 ? (
        <>
          <BookGrid books={data.books.items} />
          <div className="mt-6 flex justify-center gap-2">
            {page > 1 && <button className="btn-secondary" onClick={() => setParams({ page: String(page - 1) })}>← {t('common.prev')}</button>}
            {data.books.hasNext && <button className="btn-secondary" onClick={() => setParams({ page: String(page + 1) })}>{t('common.next')} →</button>}
          </div>
        </>
      ) : (
        data.children.length === 0 && <Empty />
      )}
    </>
  )
}
