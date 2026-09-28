import { Link, useParams, useSearchParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useGenreSections, useGenres } from '../api/hooks'
import { InfiniteBooks } from '../components/InfiniteBooks'
import { Empty, ErrorBox, Loading, PageTitle } from '../components/ui'

export default function Genres() {
  const { section } = useParams()
  const { t } = useTranslation()
  const sections = useGenreSections()

  if (!section) {
    return (
      <>
        <PageTitle>{t('nav.genres')}</PageTitle>
        {sections.isLoading ? (
          <Loading />
        ) : sections.error ? (
          <ErrorBox error={sections.error} />
        ) : !sections.data?.length ? (
          <Empty />
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {sections.data.map((s) => (
              <Link key={s.key} to={`/genres/${s.key}`} className="card flex items-center justify-between p-4 hover:border-accent">
                <span className="font-medium">{s.name}</span>
                <span className="text-sm muted">{t('common.count', { count: s.books })}</span>
              </Link>
            ))}
          </div>
        )}
      </>
    )
  }
  return <GenreSection section={section} />
}

function GenreSection({ section }: { section: string }) {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const genres = useGenres(section)
  const sections = useGenreSections()
  const genreId = params.get('genre') ? Number(params.get('genre')) : undefined
  const title = sections.data?.find((s) => s.key === section)?.name ?? section

  return (
    <>
      <PageTitle subtitle={<Link to="/genres" className="link">← {t('nav.genres')}</Link>}>{title}</PageTitle>
      <div className="mb-6 flex flex-wrap gap-2">
        <button className={genreId === undefined ? 'chip border-accent text-accent' : 'chip'} onClick={() => setParams({}, { replace: true })}>
          {t('common.all')}
        </button>
        {genres.data?.map((g) => (
          <button
            key={g.id}
            className={genreId === g.id ? 'chip border-accent text-accent' : 'chip'}
            onClick={() => setParams({ genre: String(g.id) }, { replace: true })}
          >
            {g.name} <span className="ml-1 muted">{g.books}</span>
          </button>
        ))}
      </div>
      <InfiniteBooks filter={genreId ? { genre: genreId } : { section }} />
    </>
  )
}
