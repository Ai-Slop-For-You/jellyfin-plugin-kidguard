# Security scope and release limitations

KidGuard 0.1.2.0 is an installable evaluated preview for Jellyfin 12.0.0. It has automated tests and live access checks, but no independent audit or complete cross-client certification. Do not interpret a heuristic recommendation as a guarantee of age appropriateness.

## Supported boundary

The boundary is an authenticated, non-admin Jellyfin user mapped to an applied KidGuard profile, using Jellyfin 12.0's MVC APIs while the plugin is loaded. A native allowed-tag policy handles ordinary catalog restrictions. A server-side filter enforces the exact approved movie/episode IDs despite inherited series tags. Unknown new IDs are absent from the snapshot. The guard also inspects common item-ID query/body fields and prunes nested JSON item references.

KidGuard administrative routes require Jellyfin's `RequiresElevation` policy. The API rejects mapping an administrator, duplicate user mappings, stale approval revisions, and apply without explicit confirmation. UI content is escaped before HTML insertion. External lookups use a fixed HTTPS host and numeric provider IDs; redirects are disabled, and child data is never included in requests. API tokens are never returned by dashboard endpoints.

## Tested evidence

See TESTING.md and the machine-readable reports. Live tests use synthetic videos on a loopback-only Jellyfin 12.0 server, with actual child session tokens and a positive control that confirms approved video bytes are streamed. Unit tests inject metadata-write failure, restart from the durable journal, recover the previous policy, and prove that automatic approval cannot apply draft download permissions or a draft-only opt-in.

The provided assertions establish their tested conditions only. They are not a proof that every current or future Jellyfin endpoint is covered.

## Boundaries that still require deployment attention

- **Plugin absence:** native series tags are inherited. If KidGuard is disabled, missing or incompatible, exact episode protection is lost. Disable child users while upgrading/removing the plugin; confirm it is Active before re-enabling them.
- **Other authority:** administrator credentials, server API keys, another adult account, filesystem/NFS/SMB access, and reverse-proxy bypasses are outside a child's token boundary. Protect them separately.
- **DLNA/other plugins:** unauthenticated DLNA and third-party streaming middleware may bypass MVC and do not provide a reliable per-child user identity. Disable these access paths for protected deployments. KidGuard does not reconfigure unrelated plugins.
- **Already delivered media:** downloads, previously buffered streams and bytes already sent cannot be recalled. New direct/HLS requests are checked, but a long-running stream opened before a policy change may continue. Stop active playback before changing a live child's access and use the native session controls if immediate revocation is necessary.
- **Anonymous artwork and metadata side channels:** Jellyfin has public image/system endpoints. KidGuard is focused on authenticated item/playback access and does not claim to eliminate every artwork, count, title, timing or websocket metadata side channel.
- **Client matrix:** exhaustive HLS/transcoding, casting, offline-client and third-party-client validation is not complete. Native Live TV, external channels, shared control, public sharing and SyncPlay are disabled on apply.
- **Folders:** existing native folder ACLs remain effective. A manual allow cannot grant access outside those ACLs unless the administrator explicitly changes the profile's folder settings.
- **Data integrity:** state and Jellyfin's database must be backed up together. File replacement is atomic on supported local filesystems; a corrupt state is not silently reset. Missing state files after manual deletion cannot reconstruct the exact historical allowlist.
- **Concurrency:** in-flight requests may have started under the previous snapshot. Filesystem, metadata-refresh and user-policy edits made by other plugins/admins are not distributed transactions with KidGuard. Recheck permissions after such changes.

## Failure and recovery

Apply writes a journal before metadata/policy mutation and disables the affected user during the transition. Any incomplete mutation leaves the account disabled. Restart detects a pending journal; the administrator must use Undo/Recover. Read-only draft work does not alter the account. A completed apply can still report a later cleanup failure; obsolete generation tags are inert and the audit/snapshot identifies the committed state.

An unexpected security issue should be reproduced with synthetic media, without publishing tokens, passwords, private library paths or child information. This repository is local and has no hosted issue tracker configured.
