import { useState } from 'react'
import clsx from 'clsx'
import { useMediaBase } from '../auth/AuthContext'
import type { BookSummary } from '../api/types'

/** Book cover (lazy, cached by the browser/service worker) with a generated placeholder. */
export function Cover({ book, size = 'thumb', className }: { book: Pick<BookSummary, 'id' | 'title' | 'cover' | 'format' | 'authors'>; size?: 'thumb' | 'cover'; className?: string }) {
  const base = useMediaBase()
  const [failed, setFailed] = useState(book.cover === 'none')
  const hue = hash(book.title) % 360
  return (
    <div className={clsx('relative aspect-[2/3] overflow-hidden rounded-md bg-stone-200 shadow-sm dark:bg-stone-800', className)}>
      {!failed && (
        <img
          src={`${base}/${size}/${book.id}`}
          alt=""
          loading="lazy"
          decoding="async"
          className="h-full w-full object-cover"
          onError={() => setFailed(true)}
        />
      )}
      {failed && (
        <div
          className="flex h-full w-full flex-col justify-between p-2.5 text-white"
          style={{ background: `linear-gradient(160deg, hsl(${hue} 35% 38%), hsl(${(hue + 40) % 360} 40% 22%))` }}
        >
          <span className="line-clamp-5 font-serif text-sm leading-snug font-semibold">{book.title}</span>
          <span className="flex items-end justify-between gap-1 text-[10px] uppercase tracking-wide opacity-80">
            <span className="line-clamp-2">{book.authors[0]?.name}</span>
            <span>{book.format}</span>
          </span>
        </div>
      )}
    </div>
  )
}

function hash(s: string) {
  let h = 0
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0
  return Math.abs(h)
}
