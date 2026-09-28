import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { BookmarkMinus, BookmarkPlus, BookOpenText, Download, FileArchive, RefreshCw } from 'lucide-react'
import { useBook, useEditions } from '../api/hooks'
import { api } from '../api/client'
import { useAuth, useMediaBase } from '../auth/AuthContext'
import { Cover } from '../components/Cover'
import { BookGrid } from '../components/BookCard'
import { ErrorBox, Loading } from '../components/ui'
import { useReadable } from '../hooks/useReadable'
import { displayName, formatDate, formatSize } from '../lib/format'

const NO_ZIP = ['epub', 'kepub', 'mobi', 'azw', 'azw3', 'cbz', 'docx']

export default function BookPage() {
  const { id } = useParams()
  const bookId = Number(id)
  const { t, i18n } = useTranslation()
  const { user } = useAuth()
  const media = useMediaBase()
  const readable = useReadable()
  const q = useBook(bookId)
  const [showEditions, setShowEditions] = useState(false)
  const editions = useEditions(bookId, showEditions)
  const qc = useQueryClient()
  const shelf = useMutation({
    mutationFn: (add: boolean) => api(`/shelf/${bookId}`, { method: add ? 'PUT' : 'DELETE' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['book', bookId] })
      qc.invalidateQueries({ queryKey: ['shelf'] })
    },
  })

  if (q.isLoading) return <Loading />
  if (q.error) return <ErrorBox error={q.error} />
  const { book, convertTargets, onShelf } = q.data!

  const info: [string, string | undefined][] = [
    [t('book.format'), book.format.toUpperCase()],
    [t('book.size'), formatSize(book.fileSize)],
    [t('book.language'), book.lang],
    [t('book.date'), book.docDate],
    [t('book.added'), formatDate(book.registeredAt, i18n.language)],
    [t('book.file'), book.fileName],
  ]

  return (
    <article className="space-y-8">
      <div className="flex flex-col gap-6 sm:flex-row">
        <div className="w-40 shrink-0 self-center sm:w-52 sm:self-start">
          <Cover book={book} size="cover" className="shadow-lg" />
        </div>
        <div className="min-w-0 flex-1 space-y-4">
          <div>
            <h1 className="text-3xl leading-tight font-semibold">{book.title}</h1>
            <p className="mt-2 text-lg">
              {book.authors.length === 0 && <span className="muted">{t('book.unknownAuthor')}</span>}
              {book.authors.map((a, i) => (
                <span key={a.id}>
                  {i > 0 && ', '}
                  <Link to={`/authors/${a.id}`} className="link">{displayName(a.name)}</Link>
                </span>
              ))}
            </p>
            {book.series.map((s) => (
              <p key={s.id} className="muted">
                <Link to={`/series/${s.id}`} className="link">{s.name}</Link>
                {s.number > 0 && ` · #${s.number}`}
              </p>
            ))}
          </div>

          <div className="flex flex-wrap gap-2">
            {readable(book.format) && (
              <Link to={`/read/${book.id}`} className="btn-primary">
                <BookOpenText className="h-4 w-4" /> {t('book.read')}
              </Link>
            )}
            <a href={`${media}/download/${book.id}/0`} className="btn-secondary" download>
              <Download className="h-4 w-4" /> {book.format.toUpperCase()}
            </a>
            {!NO_ZIP.includes(book.format) && (
              <a href={`${media}/download/${book.id}/1`} className="btn-secondary" download>
                <FileArchive className="h-4 w-4" /> {book.format.toUpperCase()}.ZIP
              </a>
            )}
            {convertTargets.map((f) => (
              <a key={f} href={`${media}/convert/${book.id}/${f}`} className="btn-secondary" download title={t('book.convert', { format: f.toUpperCase() })}>
                <RefreshCw className="h-4 w-4" /> {f.toUpperCase()}
              </a>
            ))}
            {user && (
              <button className="btn-ghost" onClick={() => shelf.mutate(!onShelf)} disabled={shelf.isPending}>
                {onShelf ? <BookmarkMinus className="h-4 w-4" /> : <BookmarkPlus className="h-4 w-4" />}
                {onShelf ? t('book.removeShelf') : t('book.addShelf')}
              </button>
            )}
          </div>

          {book.genres.length > 0 && (
            <div className="flex flex-wrap gap-2">
              {book.genres.map((g) => (
                <Link key={g.id} to={`/genres/${g.section}?genre=${g.id}`} className="chip">{g.name}</Link>
              ))}
            </div>
          )}

          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
            {info.filter(([, v]) => v).map(([k, v]) => (
              <div key={k} className="contents">
                <dt className="muted">{k}</dt>
                <dd className="min-w-0 truncate">{v}</dd>
              </div>
            ))}
          </dl>
        </div>
      </div>

      {book.annotation && (
        <section>
          <h2 className="mb-2 text-lg font-semibold">{t('book.annotation')}</h2>
          <div className="max-w-prose space-y-2 leading-relaxed">
            {book.annotation.split('\n').map((p, i) => <p key={i}>{p}</p>)}
          </div>
        </section>
      )}

      {book.editions > 1 && (
        <section>
          <button className="btn-secondary" onClick={() => setShowEditions((v) => !v)}>
            {t('book.editions', { count: book.editions })}
          </button>
          {showEditions && (
            <div className="mt-4">{editions.isLoading ? <Loading /> : <BookGrid mode="list" books={editions.data?.items.filter((b) => b.id !== book.id) ?? []} />}</div>
          )}
        </section>
      )}
    </article>
  )
}
