# KidGuard for Jellyfin

An installable, parent-reviewed child-library manager for **Jellyfin Server 12.0.0** and Jellyfin Web 12.0. Built with .NET 10. Version **0.1.1.0 is an evaluated preview**, with automated tests and isolated real-server access tests. It is not an independently audited parental-security product. Read [security scope](docs/SECURITY.md) before using it for a child account.

## Install

In **Dashboard → Plugins → Repositories**, add this repository URL:

```text
https://raw.githubusercontent.com/Ai-Slop-For-You/jellyfin-plugin-kidguard/main/manifest.json
```

Install **KidGuard** from the catalog and restart Jellyfin. Requires Jellyfin **12.0.0**. [Release downloads](https://github.com/Ai-Slop-For-You/jellyfin-plugin-kidguard/releases) include the plugin, corresponding source and checksums. For manual installation:

1. Back up Jellyfin's configuration and database. Start with a test server.
2. Stop Jellyfin. Extract `KidGuard_0.1.1.0.zip` into a new `KidGuard_0.1.1.0` directory inside Jellyfin's **plugins directory**. Keep both DLLs together. Do not copy Jellyfin's own dependency DLLs into the plugin directory.
3. Restart Jellyfin. Confirm KidGuard is Active under **Dashboard → Plugins**.
4. Open **Dashboard → KidGuard**. The configuration URL is `web/#/configurationpage?name=kidguard` relative to your Jellyfin server.
5. Analyze the library, create a draft child profile or select an existing non-admin user, set age/comfort preferences, and answer the calibration questions.
6. Review the library, including episodes. Click **APPROVE & APPLY CHILD PROFILE** when satisfied. New users are created at this point; existing users are untouched until approval.
7. Sign in with the child account and verify the permitted titles on your clients.

Typical plugin directories: `/var/lib/jellyfin/plugins` for Linux packages, `/config/plugins` for the official Docker image, and the `plugins` subdirectory of Jellyfin's configured data directory on other installations. The data directory in Jellyfin's startup log is authoritative.

## What is implemented

- Independent child profiles and Jellyfin user mapping; opaque profile/generation tags.
- Draft/review/apply workflow with revision checks and explicit confirmation.
- Movie, series, season and episode management; searching, sorting, filters, paginated batches, explanations, overrides and calibration.
- Nine content dimensions, age/certification starting points, bounded calibration and conservative uncertainty handling.
- Native user-policy restrictions plus an exact approved-item server filter to address inherited series tags.
- Optional TMDb certification API, local metadata/advisory provider, local cache, expiry, refresh, bounded background scanning, cancellation, retry/backoff and throttling.
- Default review of new media; opt-in automatic additions use **previously approved profile settings**, never draft permissions.
- Atomic local state, bounded audit log, one-level Undo, interrupted-apply recovery.
- Family `KidSafe` catalog with any/all profile modes and independent manual catalog decisions.
- Copy comfort settings; explicit reuse of decisions across children. No silent propagation.

## Decisions and television

Recognized series ratings now produce Allow or Block directly, instead of forcing all series into Review. For children aged **9 and older**, TV-Y/TV-Y7 shows receive an Allow recommendation even when calibration is gentler, subject to explicit content limits, parent overrides and conflicting/failed evidence. Younger children retain stricter ceiling checks: a six-year-old's default profile blocks TV-Y7. TV-PG uses an editorial starting point of **12** (not an official age minimum); profile maturity and calibration settings still apply. Animation or a children's genre alone does not grant access.

An episode or season without a recognized certification may use its closest rated TV ancestor's certification. The explanation identifies that fallback; it does not invent episode-specific advisories. An episode's own recognized rating takes precedence, explicit content constraints remain enforced, and fallback confidence is capped below the automatic-addition threshold. New-media review and explicit Apply remain in place.

After upgrading, **Reanalyze**, inspect the proposed library, then **Approve & Apply**. Existing applied permissions do not change just because the plugin was upgraded. Ordinary Allow/Block decisions expire on reanalysis; Always decisions persist.

**Allow / Block** are draft decisions that expire at the next analysis. **Always Allow / Always Block** persist through analysis. **Reset to recommendation** removes the manual decision. Every edit stays draft until Apply. Calibration answers adjust recommendations, not permanent title overrides.

A series/season choice applies to its **current analyzed descendants** at the next approval. The closest explicit choice wins: episode, then season, then series. A blocked episode stays blocked even inside an allowed series. A future episode is outside the approved snapshot. Inspect the proposed video count before applying a series choice.

A series is navigation, not a playable title: its permitted episodes determine what can actually play. The family catalog is an administrative catalog, not a child permission grant. `KidSafe` is reserved for KidGuard; use another tag for a pre-existing independently managed catalog.

## Account permissions

Apply replaces native parental allowed/blocked tags and rating/unrated filters with the approved catalog. This permits a parent's explicit approval of an unrated or higher-rated title. The original policy is saved for Undo. Existing library-folder ACLs remain in force unless explicit folder IDs are selected in Advanced settings.

Administrator privileges, deletion, metadata management, Live TV, external channels, shared-device/other-user control, public sharing, media conversion and SyncPlay are disabled for managed accounts. Remote access, downloads and transcoding are presented as explicit settings. Remuxing remains enabled. Downloads are off by default because downloaded copies cannot be revoked.

Use separate protected adult accounts. A child who knows an adult password can access that adult account; KidGuard does not add a client profile PIN or replace Jellyfin authentication. DLNA and third-party endpoints outside Jellyfin's authenticated MVC API are not supported enforcement paths.

## Providers and privacy

KidGuard works offline using Jellyfin metadata. Optional TMDb lookups require an API **Read Access Token** and explicit enablement. Only numeric TMDb IDs are queried; there is no title scraping, paywall bypass, mandatory AI, viewing-history access or transmission of child profile information. Providers necessarily see the outbound request's source IP. Tokens are stored locally in the owner-readable state file, not sent to the dashboard.

TMDb supplies certifications, **not comprehensive scene advisories**. Detailed local evidence can be entered as metadata tags: `Advisory:Fear:2`, `Advisory:Violence:1`, etc., where severity is 0–4. Supported dimensions: Violence, Fear, SexualContent, Nudity, Profanity, Substances, MatureThemes, DeathGrief, DisturbingImagery. Untagged dimensions stay unknown. Synopsis, genre and studio are displayed/stored as metadata but are not converted into invented scene facts.

Ratings are editorial starting points, not exact developmental age guarantees. Country mappings currently cover selected US, GB and DE certifications; unmapped ratings require review. Confidence is a heuristic, not a statistically calibrated probability.

This product uses the TMDB API but is not endorsed or certified by TMDB. Follow TMDb's attribution/licensing requirements when distributing a derivative UI or enabling a commercial use case. No TMDb data is bundled in this release.

## Upgrade, recovery and uninstall

See [INSTALLATION.md](docs/INSTALLATION.md), [BUILDING.md](BUILDING.md), [ARCHITECTURE.md](ARCHITECTURE.md), [research](docs/RESEARCH.md), [security scope](docs/SECURITY.md), and [test evidence](docs/TESTING.md).

**Keep managed users disabled while removing or replacing the plugin.** Native series-tag inheritance alone does not preserve KidGuard's exact episode snapshot if the plugin is absent. Undo of the initial apply restores the original policy, which may be unrestricted. Uninstall never silently restores adult access.

License: GPL-2.0-or-later. See [LICENSE](LICENSE).
