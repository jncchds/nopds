# SimpleOPDS Architecture

## Overview

SimpleOPDS is a Django-based digital library cataloging and distribution system that implements the OPDS (Open Publication Distribution System) standard. It scans a directory of e-books, catalogs them with metadata (authors, series, genres), and provides both OPDS feed and web-based access to the collection. The system is designed to work with various book formats (FB2, EPUB, MOBI, PDF, DJVU, etc.) and supports file format conversion.

**Current Version**: 0.47-devel  
**Python Version**: 3.7+  
**Framework**: Django 1.10+

---

## Technology Stack

### Backend
- **Django 1.10+** - Web framework and ORM
- **APScheduler 3.3.0+** - Scheduled task execution (book scanning)
- **lxml** - XML processing (FB2 parsing)
- **Pillow 2.9.0+** - Image processing (cover extraction/thumbnails)
- **django-picklefield** - Pickle field support
- **python-telegram-bot 10+** - Telegram bot integration
- **constance** - Dynamic configuration management (FCONSTANCE)

### Database Support
- SQLite (default, single-user)
- MySQL/MariaDB (recommended for production)
- PostgreSQL (with specific optimizations)

### Frontend
- Django templates (HTML/CSS/JavaScript)
- OPDS Atom XML feed generation
- Responsive foundation-based CSS framework

---

## Directory Structure

```
sopds/
├── sopds_web_backend/          # Django app for HTTP web interface
│   ├── models.py               # (Empty - models in opds_catalog)
│   ├── views.py                # Web view handlers (search, browse, login)
│   ├── urls.py                 # URL routing for web interface
│   ├── settings.py             # Web-specific settings
│   ├── admin.py                # Django admin configuration
│   ├── apps.py                 # App configuration
│   ├── migrations/             # Database migrations
│   ├── static/                 # CSS, JS, images
│   ├── templates/              # HTML templates
│   └── locale/                 # Translations
│
├── opds_catalog/               # Django app for OPDS feed generation & core logic
│   ├── models.py               # Core data models (Book, Author, Genre, Series, etc.)
│   ├── feeds.py                # OPDS Atom feed generation (50KB+)
│   ├── views.py                # (Minimal - feeds used instead)
│   ├── urls.py                 # URL routing for OPDS feeds
│   ├── dl.py                   # Download/convert handler logic
│   ├── settings.py             # App-specific constants
│   ├── opdsdb.py               # Database operations & utilities
│   ├── sopdscan.py             # Book collection scanner
│   ├── fb2parse.py             # FB2 format parser
│   ├── inpx_parser.py          # INPX library index parser
│   ├── middleware.py           # Basic auth middleware
│   ├── utils.py                # Utility functions (transliteration, etc.)
│   ├── opds_paginator.py       # Custom pagination for OPDS feeds
│   ├── zipf.py                 # ZIP file handling (66KB - enhanced)
│   ├── admin.py                # Django admin config
│   ├── apps.py                 # App configuration
│   ├── management/
│   │   └── commands/
│   │       ├── sopds_scanner.py    # Scheduled scanner service
│   │       ├── sopds_server.py     # Built-in HTTP server
│   │       ├── sopds_util.py       # Utility commands
│   │       └── sopds_telebot.py    # Telegram bot service
│   ├── migrations/             # Database migrations
│   ├── static/                 # Assets (CSS, JS)
│   ├── templates/              # HTML templates
│   ├── fixtures/               # Genre data (genre.json, mygenres.json)
│   ├── locale/                 # Translations
│   ├── log/                    # Runtime logs
│   ├── tmp/                    # Temporary files
│   ├── tests/                  # Unit tests
│   └── migrations/
│
├── book_tools/                 # Book format detection & handling
│   ├── format/                 # Format-specific handlers
│   │   ├── __init__.py         # mime_detector, bookfile factory
│   │   ├── mimetype.py         # MIME type enumeration
│   │   ├── util.py             # Utilities (ZIP parsing, etc.)
│   │   ├── fb2.py              # FB2 format handler
│   │   ├── fb2sax.py           # FB2 SAX parser
│   │   ├── epub.py             # EPUB format handler
│   │   ├── mobi.py             # MOBI/Mobipocket handler
│   │   ├── other.py            # Generic/dummy handler
│   │   └── ...
│   └── pymobi/                 # MOBI format utilities
│
├── constance/                  # Dynamic configuration module
│   ├── backends/               # Config storage backends
│   ├── base.py                 # Base configuration class
│   ├── settings.py             # Constance settings
│   ├── admin.py                # Django admin integration
│   ├── context_processors.py   # Template context processor
│   ├── signals.py              # Signal handlers
│   └── ...
│
├── convert/                    # Book format converters
│   ├── fb2toepub/              # FB2 to EPUB converter
│   ├── fb2epub/                # Java-based FB2 to EPUB
│   └── fb2conv/                # Multi-format converter (EPUB, MOBI)
│
├── static/                     # Global static assets
│   ├── css/                    # Stylesheets
│   ├── js/                     # JavaScript
│   ├── images/                 # Icons, logos, placeholders
│   └── foundation-icons/       # Icon font
│
├── assets/                     # Frontend source assets
│   └── sopds-sass/             # SASS/SCSS stylesheets
│
├── manage.py                   # Django management script
├── wsgi.py                     # WSGI entry point
├── Pipfile / Pipfile.lock      # Python dependencies (pipenv)
└── requirements.txt            # pip requirements
```

---

## Core Data Models

Located in `opds_catalog/models.py`. Uses Django ORM with the following entities:

### Main Entities

**Book**
- `filename`: Book filename (max 512 chars, indexed)
- `path`: Relative path within catalog (indexed)
- `filesize`: File size in bytes
- `format`: File format (fb2, epub, mobi, pdf, djvu, etc., max 8 chars)
- `catalog`: Foreign key to parent Catalog
- `cat_type`: Catalog type (0=Normal, 1=ZIP, 2=INPX, 3=INP)
- `registerdate`: Registration timestamp (auto=now)
- `docdate`: Document/publication date
- `lang`: Language name (max 16 chars)
- `title`: Book title (max 512 chars, indexed)
- `search_title`: Uppercase title for searching (indexed)
- `annotation`: Book description (max 10,000 chars)
- `lang_code`: Language category code (1=Cyrillic, 2=Latin, 3=Digits, 9=Other, indexed)
- `avail`: Availability flag (0=deleted, 1=unchecked, 2=verified)
- **Relations**: Authors (M2M via bauthor), Genres (M2M via bgenre), Series (M2M via bseries)

**Catalog**
- `parent`: Self-referencing FK (hierarchical)
- `cat_name`: Catalog name (indexed)
- `path`: Catalog path (indexed)
- `cat_type`: Type indicator (normal/zip/inpx/inp)
- `cat_size`: Total size of catalog contents

**Author**
- `full_name`: Author name (indexed)
- `search_full_name`: Uppercase name for searching (indexed)
- `lang_code`: Language category

**Genre**
- `genre`: Genre code (indexed)
- `section`: Genre section (indexed)
- `subsection`: Genre subsection (indexed)

**Series**
- `ser`: Series name (indexed)
- `search_ser`: Uppercase name for searching (indexed)
- `lang_code`: Language category

**Bookshelf** (User Library)
- `user`: FK to Django User
- `book`: FK to Book
- `readtime`: When added to shelf (timestamp, indexed)

**Counter** (Statistics/Cache)
- `name`: Counter name (PK)
- `value`: Counter value
- `update_time`: Last update timestamp
- Tracks: all books, catalogs, authors, genres, series counts

### Through Tables
- `bauthor`: Book-Author relationship
- `bgenre`: Book-Genre relationship
- `bseries`: Book-Series relationship (includes `ser_no` for series order)

---

## Key Components & Services

### 1. **Book Scanner Service** (`sopds_scanner.py`)

**Purpose**: Periodically scans the configured book directory and indexes contents.

**Key Features**:
- **Standalone Process**: Runs as daemon or scheduled task via APScheduler
- **Cron Scheduling**: Configurable via SOPDS_SCAN_SHED_* parameters
  - Default: 0:00 and 12:00 daily
  - Parameters: MINUTE, HOUR, DAY, DAY_OF_WEEK
- **File Format Support**:
  - Direct files: .fb2, .epub, .mobi, .pdf, .djvu, .txt, .rtf, .doc, .docx
  - Compressed: .zip archives (recursive)
  - Index files: .inpx (library indexes)
- **Metadata Extraction**:
  - FB2 (FictionBook) XML parsing via lxml
  - Fallback SAX parser for malformed FB2
  - Cover extraction & thumbnail generation
- **Database Management**:
  - Transaction-based atomic updates
  - Availability tracking (avail field: 0=deleted, 1=unchecked, 2=verified)
  - Duplicate detection (multiple strategies)
  - Soft delete support (logical deletion)
- **Performance**:
  - Database access during scanning may block web interface (use MySQL/PostgreSQL)
  - Statistics collection (books added/skipped/deleted)
  - Logging to file (`SOPDS_SCANNER_LOG`)

**Configuration**:
```python
SOPDS_ROOT_LIB              # Root book directory
SOPDS_BOOK_EXTENSIONS       # Formats to index (.fb2 .pdf .epub .djvu .mobi)
SOPDS_ZIPSCAN               # Enable ZIP archive scanning
SOPDS_ZIPCODEPAGE           # ZIP filename encoding (cp437, cp866, cp1251, utf-8)
SOPDS_INPX_ENABLE           # Use .inpx library indexes
SOPDS_INPX_SKIP_UNCHANGED   # Skip unchanged INPX files
SOPDS_FB2SAX                # Use SAX parser for FB2 (faster, more forgiving)
SOPDS_COVER_SHOW            # Extract & show book covers
SOPDS_DELETE_LOGICAL        # Soft-delete vs hard-delete
SOPDS_SCAN_START_DIRECTLY   # Force immediate scan
```

### 2. **HTTP/OPDS Server Service** (`sopds_server.py`)

**Purpose**: Serves the OPDS feeds and web interface.

**Key Features**:
- Built-in development server or WSGI gateway (Apache/Nginx)
- Configurable host/port (default: 8001)
- Daemonization support
- Logging to file (`SOPDS_SERVER_LOG`)
- Thread-based request handling

### 3. **Telegram Bot Service** (`sopds_telebot.py`)

**Purpose**: Provides instant messaging access to the library via Telegram.

**Features**:
- Search books by title, author, series, genre
- Optional user authentication (match Telegram username to SOPDS user)
- Configurable result limits
- API token-based activation

**Configuration**:
```python
SOPDS_TELEBOT_API_TOKEN   # Telegram bot API token
SOPDS_TELEBOT_AUTH        # Enable user authentication
SOPDS_TELEBOT_MAXITEMS    # Max results per message (default: 10)
```

### 4. **Utility Commands** (`sopds_util.py`)

- `clear` - Erase all book data, reset genres
- `info` - Display collection statistics
- `save_mygenres` / `load_mygenres` - Genre backup/restore
- `setconf` / `getconf` - Configuration management
- `pg_optimize` - PostgreSQL table optimization

---

## Frontend & API Architecture

### OPDS Feed System (`feeds.py`)

The system generates OPDS Atom XML feeds for e-book reader compatibility. Main feed classes:

**Feed Base Classes**:
- `AuthFeed` - Wraps authentication around feed generation
- `opdsFeed(Atom1Feed)` - Custom Atom feed with OPDS extensions
- `opdsEnclosure` - OPDS-specific link enclosures

**Main Feeds**:

1. **Navigation/Catalog Feeds**
   - `MainFeed()` - Root catalog (main entry point)
   - `CatalogsFeed()` - Directory tree navigation
   - `LangFeed()` - Language filter selection

2. **Browse Feeds**
   - `BooksFeed()` - Books by language and character
   - `AuthorsFeed()` - Authors by language and character
   - `SeriesFeed()` - Series by language and character
   - `GenresFeed()` - Genre categories

3. **Search Feeds**
   - `SearchBooksFeed()` - Full-text book search (searchtype: m=middle, b=beginning, a=author, s=series, g=genre, u=user shelf, d=..., e=...)
   - `SearchAuthorsFeed()` - Author search
   - `SearchSeriesFeed()` - Series search
   - `SearchTypesFeed()` - Disambiguate search context

4. **Support**
   - `OpenSearch` - OpenSearch 1.1 descriptor
   - `SelectSeriesFeed()` - Series selection for "author+series" searches

**Pagination**:
- Custom `OPDS_Paginator` class limits items per page (SOPDS_MAXITEMS, default 60)
- Generates prev/next links in feed
- OPDS client support for incremental browsing

### Web Interface (`sopds_web_backend`)

**URL Endpoints**:
```
/                           # Main page
/search/books/              # Book search form
/search/authors/            # Author search form
/search/series/             # Series search form
/catalog/                   # Catalog browser
/book/                      # Book detail/list
/author/                    # Author detail/list
/genre/                     # Genre browser
/series/                    # Series browser
/login/ / /logout/          # Authentication
/bs/delete/ / /bs/clear/    # Bookshelf management
```

**Key Views**:
- `SearchBooksView()` - Multi-mode book search (title, author, series, genre, bookshelf)
- `CatalogsView()` - Directory navigation
- `AuthorsView()` - Author browsing/filtering
- `GenresView()` - Genre tree navigation
- Pagination support for large result sets

**Authentication**:
- Optional Basic Auth (configurable via SOPDS_AUTH)
- Django user sessions for web interface
- Separate auth for OPDS feeds
- Per-user bookshelf tracking

---

## File Download & Conversion (`dl.py`)

**Download Handler**:
- Handles book file serving (native format or ZIP)
- Supports files in normal directories and ZIP archives
- Transparent path mapping for INPX-indexed books

**Conversion Support**:
- FB2 → EPUB conversion (via fb2toepub converter)
- FB2 → MOBI conversion (via fb2epub or fb2conv)
- On-demand conversion with caching in temp directory
- External tool integration

**Cover Handling**:
- Extracts embedded book covers
- Generates thumbnails (100x100 px)
- Fallback placeholder image (nocover.jpg)
- Cache support

---

## Configuration System (Constance)

**Dynamic Configuration** via `constance` library - allows runtime changes without restart.

**Key Parameters**:

**Library Settings**:
- `SOPDS_ROOT_LIB` - Book directory path
- `SOPDS_BOOK_EXTENSIONS` - Indexable formats
- `SOPDS_TEMP_DIR` - Temporary file location
- `SOPDS_DOUBLES_HIDE` - Hide duplicate editions

**Scanning Options**:
- `SOPDS_ZIPSCAN` - Enable ZIP scanning
- `SOPDS_ZIPCODEPAGE` - ZIP encoding (cp437, cp866, cp1251, utf-8)
- `SOPDS_FB2SAX` - Use SAX parser
- `SOPDS_INPX_ENABLE` - Use INPX indexes
- `SOPDS_INPX_SKIP_UNCHANGED` - Optimization
- `SOPDS_DELETE_LOGICAL` - Soft/hard delete
- `SOPDS_COVER_SHOW` - Extract covers

**Display Options**:
- `SOPDS_LANGUAGE` - UI language
- `SOPDS_SPLITITEMS` - Group expansion threshold (300)
- `SOPDS_MAXITEMS` - Items per page (60)
- `SOPDS_ALPHABET_MENU` - Show alphabet navigation
- `SOPDS_TITLE_AS_FILENAME` - Transliterated download names
- `SOPDS_NOCOVER_PATH` - Default cover image

**Scheduling**:
- `SOPDS_SCAN_SHED_MIN` - Scan minutes (cron format, default '0')
- `SOPDS_SCAN_SHED_HOUR` - Scan hours (default '0,12')
- `SOPDS_SCAN_SHED_DAY` - Scan days (default '*')
- `SOPDS_SCAN_SHED_DOW` - Scan day-of-week (default '*')
- `SOPDS_SCAN_START_DIRECTLY` - Force immediate scan

**Conversion**:
- `SOPDS_FB2TOEPUB` - Path to FB2→EPUB converter
- `SOPDS_FB2TOMOBI` - Path to FB2→MOBI converter

**Security**:
- `SOPDS_AUTH` - Require authentication
- `SOPDS_CACHE_TIME` - Page cache duration (seconds)

**Bot**:
- `SOPDS_TELEBOT_API_TOKEN` - Telegram token
- `SOPDS_TELEBOT_AUTH` - Telegram user matching
- `SOPDS_TELEBOT_MAXITEMS` - Results per message

**Logging**:
- `SOPDS_SERVER_LOG` - Server log file
- `SOPDS_SCANNER_LOG` - Scanner log file
- `SOPDS_SERVER_PID` - Server PID file
- `SOPDS_SCANNER_PID` - Scanner PID file

**UI Customization**:
- `SOPDS_TITLE` - App title
- `SOPDS_SUBTITLE` - App subtitle
- `SOPDS_ICON` - App icon path

---

## Book Format Support

**Supported Formats**:
- **FB2** (FictionBook) - Primary format with full metadata extraction
- **EPUB** - Standard e-book format
- **MOBI/AZW** - Amazon Kindle format
- **PDF** - Portable Document Format
- **DJVU** - Scanned document format
- **TXT** - Plain text
- **RTF** - Rich Text Format
- **DOC/DOCX** - Microsoft Word

**Format Handler Architecture** (`book_tools/format/`):
- Abstract format handler interface
- MIME type detection by file extension
- Format-specific metadata extraction
- Dummy handler for unsupported formats

**FB2 Parsing** (`fb2parse.py`):
- XML-based metadata extraction (title, authors, series, genre, annotation, cover)
- Two parsing strategies:
  - SAX parser (faster, tolerates malformed XML) - **default**
  - XPath parser (stricter, less forgiving)
- Cover image extraction (base64-encoded in FB2)

---

## Database Performance Considerations

### Indexing
- Heavy use of `db_index=True` on searchable fields
- Separate search fields (e.g., `search_title`) store uppercased data
- Composite indexes potentially needed for common queries

### Optimization Strategies
- PostgreSQL-specific: `pg_optimize` command (ALTER TABLE fillfactor=50, VACUUM FULL)
- MyISAM default for MySQL (can be upgraded to InnoDB on modern versions)
- Logical soft-delete support (avail=0) to preserve history
- Counter table caches collection statistics

### Multi-User Access
- **SQLite**: Single-user, blocking during scans (not recommended for production)
- **MySQL/MariaDB**: Multi-user, recommended (MyISAM or InnoDB)
- **PostgreSQL**: Multi-user, fully featured, best for large collections

---

## Template System

**Base Templates**: Located in `opds_web_backend/templates/`
- HTML template inheritance for consistency
- Context processors inject global data:
  - App title, version, statistics
  - Authentication status
  - User bookshelf (recent)
  - Random book recommendation
  - Configuration flags

**Localization**:
- Django i18n framework
- Translations in `locale/` directories
- Supports multiple languages (en-EN, ru-RU, etc.)

---

## Middleware & Security

**BasicAuthMiddleware** (`middleware.py`):
- Handles HTTP Basic Authentication for OPDS feeds
- Optional enforcement based on SOPDS_AUTH setting
- Fallback for non-session-based clients (e-readers)

**CSRF Protection**: Django middleware (default)

**Logging**:
- Configurable log levels (debug, info, warning, error, critical)
- Separate logs for server and scanner
- File-based logging with rotation support

---

## Extension Points & Customization

1. **New Book Formats**: Implement handler in `book_tools/format/`
2. **Custom OPDS Feeds**: Extend `AuthFeed` or `opdsFeed` in `feeds.py`
3. **Search Strategies**: Modify query construction in views/feeds
4. **Metadata Extraction**: Add parser support in `fb2parse.py` or format handlers
5. **Conversion Tools**: Configure external converters via settings
6. **Frontend**: Django template customization
7. **Database Backend**: Support for additional databases via Django ORM
8. **Authentication**: Integrate custom auth backends via Django auth system

---

## Deployment Options

### Development
```bash
python manage.py migrate
python manage.py sopds_util clear
python manage.py runserver 0.0.0.0:8000
python manage.py sopds_scanner scan  # One-time scan
```

### Production with Built-in Server
```bash
python manage.py sopds_server start --host 0.0.0.0 --port 8001 --daemon
python manage.py sopds_scanner start --daemon
```

### Production with Web Server (Recommended)
- **Apache**: WSGI module + `sopds/wsgi.py`
- **Nginx**: gunicorn/uWSGI + `sopds/wsgi.py`
- Run scanner as separate scheduled process (cron, systemd timer, or APScheduler daemon)

### Database Setup
- **SQLite**: Out-of-box (not for production)
- **MySQL**: Create database, user, update settings
- **PostgreSQL**: Create database, user, update settings, run `pg_optimize`

---

## Known Limitations

1. **SQLite**: Single-user, blocks scanner and web access simultaneously
2. **No Real-time Sync**: Scanner processes in batches, not watching for file changes
3. **Cover Extraction**: Requires valid embedded covers (fallback to placeholder)
4. **Conversion**: Depends on external tools, not built-in
5. **Search**: Simple substring/prefix matching (no full-text search engines like Elasticsearch)
6. **Concurrency**: Limited by database choice (addressed via PostgreSQL/MySQL)
7. **OPDS Limitations**: Atom-based, pagination-driven (suitable for e-readers)

---

## .NET 10 ASP.NET Core Port Considerations

When porting to .NET:

1. **Data Models** → EF Core entities with proper indexing
2. **OPDS Feed Generation** → Custom Atom XML generation (use System.Xml or library)
3. **Book Scanner** → Background service (HostedService) with APScheduler equivalent
4. **Format Support** → Implement format handlers with dependency injection
5. **Configuration** → Options pattern for dynamic config (may need custom solution)
6. **Authentication** → ASP.NET Core identity system
7. **File Handling** → System.IO + ZipFile APIs
8. **Telegram Bot** → python-telegram-bot equivalent in .NET
9. **Database**: Entity Framework Core for multi-database support
10. **Web Framework**: ASP.NET Core MVC/Razor for templates
11. **Scheduling**: Quartz.NET or similar for scanner scheduling
12. **Image Processing**: SkiaSharp or ImageSharp for cover/thumbnail handling

