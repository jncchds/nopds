export type LangCode = 'all' | 'cyrillic' | 'latin' | 'digits' | 'other'
export type TextMatch = 'contains' | 'begins' | 'exact'
export type BookSort = 'title' | 'added' | 'seriesNumber' | 'shelf'
export type CoverState = 'unknown' | 'none' | 'present'
export type AccessMode = 'private' | 'public'

export interface Page<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  hasNext: boolean
  total?: number
}

export interface AuthorRef { id: number; name: string }
export interface SeriesInfo { id: number; name: string; number: number }
export interface GenreRef { id: number; code: string; name: string; section: string; sectionName: string }

export interface BookSummary {
  id: number
  libraryId: number
  title: string
  fileName: string
  format: string
  fileSize: number
  lang?: string
  docDate?: string
  registeredAt: string
  annotation?: string
  cover: CoverState
  authors: AuthorRef[]
  series: SeriesInfo[]
  genres: GenreRef[]
  editions: number
  /** Set for books users uploaded. */
  upload?: UploadInfo
}

export interface UploadInfo { id: number; isPrivate: boolean; mine: boolean; uploadedBy?: string; uploadedAt: string }

export interface UploadDto {
  id: number
  fileName: string
  relPath: string
  fileSize: number
  isPrivate: boolean
  uploadedAt: string
  uploadedBy?: string
  mine: boolean
  /** Null until the scan picks the file up. */
  bookId?: number
  title?: string
  /** The file disappeared from disk; the book is hidden. */
  missing: boolean
}

export interface BookDetails {
  book: BookSummary
  convertTargets: string[]
  onShelf: boolean
  koreaderHash?: string
}

export interface NamedCount { id: number; name: string; books: number }
export interface AlphabetGroup { prefix: string; count: number }
export interface GenreSectionInfo { key: string; name: string; books: number }
export interface GenreInfo { id: number; code: string; name: string; section: string; books: number }
export interface LibraryInfo { id: number; name: string; books: number; lastScanFinishedAt?: string; isUploads?: boolean }
export interface CatalogNode { id: number; name: string; path: string; type: 'directory' | 'zip' | 'inpx' | 'inp'; libraryId?: number }
export interface CatalogListing { current?: CatalogNode; breadcrumbs: CatalogNode[]; children: CatalogNode[]; books: Page<BookSummary> }
export interface Stats { books: number; authors: number; series: number; genres: number; libraries: number; lastScan?: string }

export interface SiteConfig {
  title: string
  subtitle: string
  version: string
  access: AccessMode
  alphabetMenu: boolean
  splitItems: number
  pageSize: number
  showCovers: boolean
  languages: string[]
  /** Single sign-on provider name when SSO is configured. */
  sso?: string
  /** Formats the web reader opens as server-converted EPUB. */
  readerConversions: string[]
  /** Source format → formats the server can convert it to, nearest first. */
  conversions: Record<string, string[]>
  /** Present when users may upload books. */
  uploads?: { libraryId: number; extensions: string[]; maxMegabytes: number }
  /** User name of the running Telegram bot; absent when the bot is off. */
  telegramBot?: string
}

export interface User {
  id: string
  userName: string
  isAdmin: boolean
  uiLanguage?: string
  hideDuplicates: boolean
  allowedLibraryIds?: number[]
  telegramLinked: boolean
  /** Telegram @name of the linked account, when it has one. */
  telegramUsername?: string
  kosyncConfigured: boolean
  hasPassword: boolean
  /** Default download format; absent means the book's own format. */
  preferredFormat?: string
}

export interface AuthResponse { accessToken: string; expiresAt: string; user: User }

export interface ShelfItem { book: BookSummary; progress: number; finished: boolean; lastOpenedAt: string }
export interface Progress { bookId: number; location?: string; progress: number; finished: boolean; lastOpenedAt: string }

// ---- admin
export interface LibraryDto {
  id: number
  name: string
  rootPath: string
  enabled: boolean
  extensions: string[]
  scanZip: boolean
  zipCodepage: string
  inpxEnabled: boolean
  inpxSkipUnchanged: boolean
  inpxTestZip: boolean
  inpxTestFiles: boolean
  watchEnabled: boolean
  scanCron?: string
  deleteLogical: boolean
  hashContent: boolean
  lastScanStartedAt?: string
  lastScanFinishedAt?: string
  lastScanSummary?: string
  books: number
  rootExists: boolean
  /** The upload library (its folder comes from the server configuration). */
  isUploads: boolean
}

export type LibraryInput = Omit<LibraryDto, 'id' | 'lastScanStartedAt' | 'lastScanFinishedAt' | 'lastScanSummary' | 'books' | 'rootExists' | 'isUploads'>

export interface ScanStatus {
  libraryId: number
  subPath?: string
  state: 'idle' | 'queued' | 'running' | 'completed' | 'failed' | 'cancelled'
  startedAt?: string
  finishedAt?: string
  filesSeen: number
  booksAdded: number
  booksUpdated: number
  booksSkipped: number
  booksDeleted: number
  booksRestored: number
  archivesScanned: number
  archivesSkipped: number
  errors: number
  currentPath?: string
  message?: string
}

export interface ExternalConverter { source: string; target: string; command: string }

export interface AppSettings {
  title: string
  subtitle: string
  access: AccessMode
  maxItems: number
  splitItems: number
  alphabetMenu: boolean
  titleAsFilename: boolean
  showCovers: boolean
  hideDuplicates: boolean
  preferredFormats: string[]
  conversion: { builtIn: boolean; external: ExternalConverter[]; cacheSizeMb: number; timeoutSeconds: number }
  telegram: { enabled: boolean; botToken?: string; requireLinkedUser: boolean; maxItems: number }
  sso: { requireApproval: boolean }
}

export interface AdminUser {
  id: string
  userName: string
  isAdmin: boolean
  allowedLibraryIds?: number[]
  locked: boolean
  approved: boolean
  createdAt: string
  telegram: boolean
  telegramUsername?: string
  email?: string
  /** Provider name when the account is linked to single sign-on. */
  sso?: string
}

export interface DirListing { path: string; parent?: string; entries: { name: string; path: string }[] }
