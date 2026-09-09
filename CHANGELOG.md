# Changelog

## 0.1.2.0 — 2026-09-09

- Remove KidGuard profiles whose linked Jellyfin account was deleted, on dashboard load, startup and background checks.
- Clear the deleted profile's approval and recovery state, remove its owned tags and refresh the family catalog. Failed metadata cleanup is queued durably for retry.
- Preserve uncreated drafts, all existing Jellyfin users and unrelated metadata. Account matching uses immutable IDs; reusing a username never reconnects an old profile.
- Upgrade and restart, then reopen KidGuard. No reanalysis or Apply is needed to clear profiles belonging to deleted accounts.

## 0.1.1.0 — 2026-09-09

- Age 9+ automatically recommends appropriately certified TV-Y/TV-Y7 children’s television despite gentler calibration; younger profiles retain stricter age checks. Explicit limits and overrides remain authoritative.
- Remove the blanket Review result for otherwise age-appropriate series and seasons.
- Use a clearly labeled series/season certification fallback for unrated descendants; retain episode-specific ratings and advisory constraints.
- Raise TV-PG's editorial starting point from 10 to 12. Clear above-ceiling ratings remain Block despite lower/conflicting sources or provider failures.
- Preserve explicit approval, episode exceptions, new-media review and conservative automatic-addition limits.
- Upgrade: reanalyze, inspect the proposed library and explicitly apply. Ordinary overrides expire during reanalysis; persistent overrides remain.

## 0.1.0.0 — 2026-09-08

Initial evaluated preview for Jellyfin 12.0.0 / .NET 10. Parent-reviewed profile setup, independent allowlists, calibration, nine-dimensional evidence, persistent overrides, episode exceptions, native policies with exact-snapshot checks, optional TMDb, cache/background analysis, approved-settings-only automatic additions, family catalog, audit/undo/recovery, embedded administrative UI and reproducible packaging. Includes automated and real-server test evidence; see docs/TESTING.md for scope and docs/SECURITY.md for release limitations.
