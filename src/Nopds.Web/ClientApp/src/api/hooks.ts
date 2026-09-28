import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { api } from './client'
import type {
  AlphabetGroup,
  AuthorRef,
  BookDetails,
  BookSort,
  BookSummary,
  CatalogListing,
  GenreInfo,
  GenreSectionInfo,
  LangCode,
  LibraryInfo,
  NamedCount,
  Page,
  SiteConfig,
  Stats,
  TextMatch,
} from './types'
import { useLibrary } from '../hooks/useLibrary'
import { useTranslation } from 'react-i18next'

export function useConfig() {
  return useQuery({ queryKey: ['config'], queryFn: () => api<SiteConfig>('/config'), staleTime: 5 * 60_000 })
}

export function useStats() {
  const { library } = useLibrary()
  return useQuery({ queryKey: ['stats', library], queryFn: () => api<Stats>('/stats', { query: { library } }) })
}

export function useLibraries() {
  return useQuery({ queryKey: ['libraries'], queryFn: () => api<LibraryInfo[]>('/libraries'), staleTime: 60_000 })
}

export interface BookFilter {
  q?: string
  match?: TextMatch
  lang?: LangCode
  author?: number
  series?: number
  genre?: number
  section?: string
  language?: string
  format?: string
  sort?: BookSort
  dupes?: boolean
  shelf?: boolean
}

export function useBooks(filter: BookFilter, enabled = true, pageSize = 48) {
  const { library } = useLibrary()
  const { i18n } = useTranslation()
  return useInfiniteQuery({
    queryKey: ['books', library, filter, pageSize, i18n.language],
    queryFn: ({ pageParam }) =>
      api<Page<BookSummary>>('/books', { query: { library, ...filter, page: pageParam, pageSize } }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasNext ? last.pageNumber + 1 : undefined),
    enabled,
    placeholderData: keepPreviousData,
  })
}

export function useRecentBooks(pageSize = 12) {
  const { library } = useLibrary()
  return useQuery({
    queryKey: ['books', 'recent', library, pageSize],
    queryFn: () => api<Page<BookSummary>>('/books/recent', { query: { library, pageSize } }),
  })
}

export function useBook(id: number) {
  const { i18n } = useTranslation()
  return useQuery({ queryKey: ['book', id, i18n.language], queryFn: () => api<BookDetails>(`/books/${id}`) })
}

export function useEditions(id: number, enabled: boolean) {
  return useQuery({
    queryKey: ['editions', id],
    queryFn: () => api<Page<BookSummary>>(`/books/${id}/editions`),
    enabled,
  })
}

export function useNames(kind: 'authors' | 'series', params: { q?: string; match?: TextMatch; lang?: LangCode; author?: number }, enabled = true) {
  const { library } = useLibrary()
  return useInfiniteQuery({
    queryKey: [kind, library, params],
    queryFn: ({ pageParam }) => api<Page<NamedCount>>(`/${kind}`, { query: { library, ...params, page: pageParam, pageSize: 100 } }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasNext ? last.pageNumber + 1 : undefined),
    enabled,
    placeholderData: keepPreviousData,
  })
}

export function useAuthor(id: number) {
  return useQuery({ queryKey: ['author', id], queryFn: () => api<AuthorRef>(`/authors/${id}`) })
}

export function useSeriesName(id: number) {
  return useQuery({ queryKey: ['seriesName', id], queryFn: () => api<AuthorRef>(`/series/${id}`) })
}

export function useAlphabet(kind: 'books' | 'authors' | 'series', lang: LangCode, prefix: string) {
  const { library } = useLibrary()
  return useQuery({
    queryKey: ['alphabet', kind, library, lang, prefix],
    queryFn: () => api<AlphabetGroup[]>(`/alphabet/${kind}`, { query: { library, lang, prefix } }),
    placeholderData: keepPreviousData,
  })
}

export function useGenreSections() {
  const { library } = useLibrary()
  const { i18n } = useTranslation()
  return useQuery({
    queryKey: ['genres', library, i18n.language],
    queryFn: () => api<GenreSectionInfo[]>('/genres', { query: { library } }),
  })
}

export function useGenres(section: string) {
  const { library } = useLibrary()
  const { i18n } = useTranslation()
  return useQuery({
    queryKey: ['genres', section, library, i18n.language],
    queryFn: () => api<GenreInfo[]>(`/genres/${encodeURIComponent(section)}`, { query: { library } }),
  })
}

export function useCatalog(libraryId: number, catalogId: number | undefined, page: number) {
  return useQuery({
    queryKey: ['catalog', libraryId, catalogId, page],
    queryFn: () => api<CatalogListing>(`/libraries/${libraryId}/catalog`, { query: { id: catalogId, page } }),
    placeholderData: keepPreviousData,
  })
}
