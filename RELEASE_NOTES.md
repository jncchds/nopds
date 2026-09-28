# Release Notes

## v0.1.0 — 2026-09-28

- feat: first public release — library scanning (FB2, EPUB, MOBI/AZW3, PDF, DJVU, CBZ, ZIP archives, INPX), OPDS catalog compatible with SimpleOPDS URLs, React web app with in-browser reader, FB2 → EPUB conversion, KOReader sync, optional Telegram bot
- ci: GitHub Actions workflow runs the tests, then builds a multi-arch (amd64 + arm64) image and pushes it to Docker Hub as `jncchds/nopds` tagged `latest`, the version and `sha-<commit>`; the Docker Hub description is synced from the README
- build: `VERSION` at the repo root is the single source of truth for the app version (assembly version, UI, image tags)
