# .NET OPDS by CHDS

A self-hosted e-book library server: scan your book folders, browse and read them in a modern web app, and serve them to e-readers over **OPDS**.

Built with **ASP.NET Core (.NET 10)**, **PostgreSQL** and a **React + TypeScript** single-page app, served together from one process.

> Inspired by [SimpleOPDS (sopds)](https://github.com/mitshel/sopds) by Dmitry Shelepnev. The OPDS URL layout is kept compatible, so existing reader bookmarks keep working.

<!-- Screenshot placeholder: add docs/screenshot.png and uncomment
![.NET OPDS](docs/screenshot.png)
-->

## Features

**Library**
- Multiple libraries, each with its own folder, schedule and scan options
- FB2, EPUB, MOBI/AZW3, PDF, DJVU, CBZ, TXT, RTF, DOC/DOCX
- Books inside ZIP archives, and **INPX** indexes (MyHomeLib/LibRusEc collections)
- **Incremental scanning**: unchanged files and archives are skipped (size + mtime); parsing runs in parallel with a batched single writer
- Cron schedule per library, optional **folder watching** (partial rescans of changed folders), live progress in the admin UI
- **Smart duplicates**: editions of the same work (normalized title + authors) collapse into one entry, with a preferred format order; optional content hashing for exact duplicates
- Soft or hard delete of books whose files disappear, with automatic restore
- Covers extracted on demand and cached as WebP thumbnails

**Reading & downloads**
- In-browser reader (foliate-js) for EPUB, FB2, MOBI/AZW3 and CBZ with saved reading position
- Built-in **FB2 → EPUB 3** converter (no Java or Python tools), plus configurable external converters (e.g. `ebook-convert`, `kepubify`) with an LRU cache
- Downloads as original, zipped or converted, with real UTF-8 file names

**Clients**
- **OPDS 1.2** (Atom) and **OPDS 2.0** (JSON) catalogs: by folders, titles, authors, series, genres, new books, bookshelf, search (OpenSearch), duplicate facets
- Readers authenticate with HTTP Basic or a **personal feed link** (`/opds/t/<token>/`) for apps without login support
- **KOReader progress sync** (kosync-compatible)
- Optional **Telegram bot**: search, book cards, file delivery

**Web app**
- Browse by alphabet (Cyrillic/Latin/digits), authors, series, genres, folders; full search
- Bookshelf with "continue reading"
- UI in **Ukrainian, English, Polish, German**, following the system language by default (Russian and Belarusian fall back to Ukrainian, others to English)
- Light and dark themes, installable **PWA** with offline covers and recently opened books
- Admin: libraries (with a server folder picker), scans, settings, users with per-library access, logs

**Security**
- JWT access tokens (short-lived, in memory) plus rotating refresh tokens in an httpOnly cookie, with reuse detection
- ASP.NET Identity password hashing, lockout, rate-limited login
- Optional **single sign-on** with Authentik (or any OpenID Connect provider): accounts are created on first sign-in and, by default, wait for admin approval

## Quick start (Docker)

```bash
git clone https://github.com/jncchds/nopds.git
cd nopds
NOPDS_BOOKS=/path/to/your/books NOPDS_ADMIN_PASSWORD=choose-a-password docker compose up -d --build
```

Open <http://localhost:8080>, sign in as `admin`, go to **Administration → Libraries → Add library** and choose `/books` (the folder mounted from `NOPDS_BOOKS`). Start a scan.

The compose file runs two containers:

| Service    | Purpose                                                                    |
|------------|----------------------------------------------------------------------------|
| `nopds`    | Web app, API, OPDS, scanner, bot. Data (keys, logs, cache) in the `nopds-data` volume |
| `postgres` | PostgreSQL 17, data in the `nopds-db` volume                                |

Mount more folders (read-only) in `docker-compose.yml` to add more libraries.

## Connecting readers

| Client                     | URL                                                          |
|----------------------------|--------------------------------------------------------------|
| OPDS 1.2 with login        | `http://<host>:8080/opds/`                                   |
| OPDS 1.2 personal link     | `http://<host>:8080/opds/t/<token>/` (see **Settings**, also as QR code) |
| OPDS 2.0                   | `http://<host>:8080/opds/v2/` or `…/opds/t/<token>/v2/`      |
| One library only           | add `l/<libraryId>/`, e.g. `/opds/l/2/`                      |
| KOReader progress sync     | Custom sync server `http://<host>:8080/kosync`, your user name and the sync password set in **Settings** |

Any OPDS 1.2 client should work (KOReader, Moon+ Reader, FBReader, Librera, …); OPDS 2.0 is supported by newer apps such as Thorium and Readest.

## Configuration

Host settings come from `appsettings.json` or environment variables (`__` separates sections):

| Setting                         | Default                   | Description                                        |
|---------------------------------|---------------------------|----------------------------------------------------|
| `ConnectionStrings__Nopds`      | local PostgreSQL          | Npgsql connection string                           |
| `Nopds__DataDir`                | `data`                    | Signing keys, data-protection keys, logs          |
| `Nopds__CacheDir`               | `data/cache`              | Covers, thumbnails, converted books               |
| `Nopds__AdminUser` / `Nopds__AdminPassword` | `admin` / –   | First admin, created only when no users exist     |
| `Nopds__AdminForce`             | `false`                   | Recovery: on every start, create `AdminUser` if missing, reset its password to `AdminPassword`, restore admin rights, approve and unlock it |
| `Nopds__AutoMigrate`            | `true`                    | Apply database migrations on start                |
| `Nopds__Jwt__Key`               | generated                 | HMAC key (≥ 32 bytes); generated into `DataDir` when empty |
| `Nopds__Jwt__AccessTokenMinutes`| `15`                      | Access token lifetime                              |
| `Nopds__Jwt__RefreshTokenDays`  | `30`                      | Refresh token lifetime                             |
| `Nopds__Oidc__Authority`        | –                         | OpenID Connect issuer URL; single sign-on is off when empty |
| `Nopds__Oidc__ClientId` / `Nopds__Oidc__ClientSecret` | –   | OAuth2 client credentials                          |
| `Nopds__Oidc__DisplayName`      | `Authentik`               | Provider name on the login button                  |
| `Nopds__Oidc__Scopes__0…`       | `openid profile email`    | Requested scopes                                   |

Everything else (site title, public/private access, page sizes, duplicate handling, covers, converters, Telegram bot, SSO approval) is edited at runtime in **Administration → Settings**. Library options (extensions, ZIP code page, INPX, schedule, watching, soft delete, hashing) are per library.

Behind a reverse proxy, forward `X-Forwarded-Proto` and `X-Forwarded-Host` so OPDS links use the public address.

### Single sign-on with Authentik

1. In Authentik, create an **OAuth2/OpenID Provider** (confidential client, scopes `openid`, `profile`, `email`) with the redirect URI `https://<your-nopds-host>/signin-oidc`, and an **Application** using it (slug e.g. `nopds`).
2. Configure NOPDS:
   ```yaml
   Nopds__Oidc__Authority: "https://auth.example.com/application/o/nopds/"
   Nopds__Oidc__ClientId: "<client id>"
   Nopds__Oidc__ClientSecret: "<client secret>"
   ```
3. The login page gets a **Sign in with Authentik** button. The first sign-in creates a local account (named after `preferred_username`; a number is appended if a local user already has that name — existing accounts are never taken over). By default the account waits until an admin approves it in **Administration → Users**, where admins also grant or revoke admin rights; turn this off in **Administration → Settings → Single sign-on**.

NOPDS must be served over HTTPS for the sign-in round trip (the OIDC correlation cookies are `Secure`). E-readers cannot use SSO: SSO users open their personal feed link from **Settings**, or set a local password there for HTTP Basic auth.

## Development

Requirements: .NET 10 SDK, Node.js 22, Docker.

```bash
docker compose -f docker-compose.dev.yml up -d        # PostgreSQL on localhost:5432
dotnet tool restore                                    # dotnet-ef
dotnet run --project src/Nopds.Web                     # API on http://localhost:5249 (admin / admin123 in Development)
cd src/Nopds.Web/ClientApp && npm install && npm run dev   # SPA on http://localhost:5173 (proxies to the API)
```

Useful commands:

```bash
dotnet test                                            # unit + integration tests (Testcontainers PostgreSQL)
cd src/Nopds.Web/ClientApp && npm run lint && npm run lint:i18n && npm run build
dotnet ef migrations add <Name> -p src/Nopds.Infrastructure -s src/Nopds.Infrastructure -o Data/Migrations
dotnet publish src/Nopds.Web -c Release                # also builds the SPA into wwwroot
```

### Project structure

```
src/Nopds.Domain          Entities and text helpers (language codes, transliteration, normalization)
src/Nopds.Infrastructure  EF Core/PostgreSQL, migrations, Identity user, settings, genre catalog, catalog queries
src/Nopds.Formats         FB2/EPUB/MOBI/CBZ parsers, book storage, covers, hashing
src/Nopds.Scanner         Incremental scanner, INPX reader, scheduler, folder watcher
src/Nopds.Conversion      FB2 → EPUB 3 converter, external converters, cache
src/Nopds.Opds            OPDS 1.2 Atom and OPDS 2.0 JSON writers
src/Nopds.Telegram        Telegram bot
src/Nopds.Web             ASP.NET Core host: API, auth, OPDS/kosync endpoints, SignalR; ClientApp/ = React SPA
tests/Nopds.Tests         Parser, converter and integration tests
docs/                     Notes on the original SimpleOPDS architecture
```

## Acknowledgements

- [SimpleOPDS](https://github.com/mitshel/sopds) by Dmitry Shelepnev — the original project this one is modelled on (catalog structure, OPDS URL scheme, INPX handling, genre list)
- [foliate-js](https://github.com/johnfactotum/foliate-js) — in-browser e-book rendering
- [KOReader](https://github.com/koreader/koreader) — the sync protocol

## License

[GPL-3.0](LICENSE). This project ports logic and data (language codes, transliteration, INPX parsing, the FB2 genre list) from SimpleOPDS, which is licensed under GPLv3.
