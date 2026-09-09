# KidGuard implementation checklist
- [x] Inspect official Jellyfin 12.0 release, template, user APIs, inherited tag behavior.
- [x] Research streaming profile UX and legitimate content-advisory sources.
- [x] Core models, calibration, recommendation tests.
- [x] Native policy/tag application, exact snapshot guard, durable rollback.
- [x] Providers, cache, background scans and new media workflow.
- [x] Admin UI: setup, calibration, library management, family catalog, history.
- [x] Compile against Jellyfin 12.0.0 and fix all build errors.
- [x] Isolated live Jellyfin smoke/access tests and UI verification.
- [x] Documentation, release ZIP, checksums, repository manifest generator.

Release status: tested preview. Production assurance limits and unsupported paths are documented in docs/SECURITY.md.
