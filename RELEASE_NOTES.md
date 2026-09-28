# Release Notes

## v0.1.1 — 2026-09-28

- fix: scanning an INPX index that lists the same book more than once (or a ZIP with a repeated entry name) no longer fails whole write batches with `duplicate key … ix_books_library_id_rel_path_entry_name`; repeats are skipped and counted in one log line
- feat: single sign-on with Authentik (any OpenID Connect provider) via `Nopds__Oidc__*`; users are created on first sign-in and wait for admin approval (switchable in Settings → Single sign-on); pending accounts are blocked everywhere (web, OPDS, KOReader sync, Telegram) and SSO users can set a local password for e-reader Basic auth
- feat: `Nopds__AdminForce=true` repairs the main admin on start — creates `Nopds__AdminUser` if missing, resets its password to `Nopds__AdminPassword` (ending its sessions only when the password actually changed) and restores admin rights, approval and unlock

## v0.1.0 — 2026-09-28

- feat: first public release — library scanning (FB2, EPUB, MOBI/AZW3, PDF, DJVU, CBZ, ZIP archives, INPX), OPDS catalog compatible with SimpleOPDS URLs, React web app with in-browser reader, FB2 → EPUB conversion, KOReader sync, optional Telegram bot
- ci: GitHub Actions workflow runs the tests, then builds a multi-arch (amd64 + arm64) image and pushes it to Docker Hub as `jncchds/nopds` tagged `latest`, the version and `sha-<commit>`; the Docker Hub description is synced from the README
- build: `VERSION` at the repo root is the single source of truth for the app version (assembly version, UI, image tags)
