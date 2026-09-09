# Architecture

## Target and extension points

The implementation targets the verified official Jellyfin 12.0 release, `Jellyfin.Controller` and `Jellyfin.Model` 12.0.0, and `net10.0`. The server owns its framework/API assemblies; they are excluded from the plugin's runtime package. `KidGuard.Core.dll` is the only additional packaged library.

`Plugin : BasePlugin<BasePluginConfiguration>, IHasWebPages` exposes the embedded HTML and ES-module administrative controller. `IPluginServiceRegistrator` installs singleton services, a hosted monitor and a global MVC action filter. Administrative endpoints use Jellyfin's `RequiresElevation` policy, not a UI-only administrator check.

## Data and responsibility boundaries

- `KidGuard.Core`: models, nine dimensions, rating mapping, calibration, recommendation evaluation, approval planning, atomic JSON persistence.
- `LibraryAdapter`: paged `ILibraryManager.GetItemList`, metadata updates via `UpdateItemAsync`, `IUserManager` user creation/policy updates.
- `ContentAnalysis`: local provider and optional TMDb provider, cache fingerprint/TTL, two-worker pipeline and single-flight remote requests.
- `Manager`: draft revisions, profile mapping, separate approved settings, apply/undo journal, snapshots, audit and family catalog.
- `SnapshotGuard`: server-side validation of item IDs and pruning of returned item references for managed child tokens.
- `LibraryMonitor`: item-added signals coalesced every 30 seconds and five-minute reconciliation, outside startup's synchronous path.
- `KidGuardController`: administrator-only API for all UI operations.

## Persistence

`<Jellyfin data path>/kidguard/state.json` contains schema version 1, profile settings, calibration, manual overrides, recommendations, approved IDs, navigation IDs, previously approved settings, content cache, family overrides, up to 500 audit entries, one undo snapshot per profile and an optional pending transaction.

The file is flushed before atomic replacement, with owner read/write permissions on Unix. A single mutation semaphore serializes scans' commits, edits, apply and undo. Reads are consistent snapshots. Cache entries contain item/provider IDs, fingerprint, sources/evidence, retrieval timestamp and analysis version. Cache fingerprint excludes KidGuard's own tags to prevent feedback loops.

A failed disk write reloads the last durable state. Corrupt/unsupported state fails initialization; it is never interpreted as an empty safe catalog. This is a single-server design, not a shared-file multi-writer database. Atomic rename is required; network filesystems without reliable atomic replacement are unsupported. Directory fsync/power-loss guarantees depend on the host filesystem; back up state with the Jellyfin database.

## Recommendation rules

1. Explicit per-item parent override.
2. Provider failures or significant rating conflicts → Review.
3. Known dimensions above explicit or calibrated tolerances → Block.
4. Missing/unmapped certification → Review.
5. Certification above age + bounded maturity/approach/calibration ceiling → Block.
6. Missing evidence for an explicit advanced tolerance → Review.
7. Series/season uncertainty or insufficient detail for young children → Review.
8. Otherwise Allow with heuristic confidence and explicit unknowns.

Calibration uses up to ten locally rated titles across rating bands, with clearly hypothetical fallback scenarios when fewer than six are available. Yes/No changes the age ceiling by at most ±3 years; contradictory boundaries cannot increase it. Known dimension evidence in calibration can adjust implicit tolerance by at most one severity step. Explicit tolerances win. No medical/developmental claims or trained behavioral profiling are made.

Ordinary Allow/Block decisions expire on analysis; Always decisions persist. Approval planning resolves the most specific explicit episode/season/series decision and constructs playable and navigation sets. These are replaced atomically after a successful apply. A parent's current-container approval is bounded to the scanned snapshot.

## Native enforcement and exact snapshots

Jellyfin 12.0's `BaseItem.GetInheritedTags` gathers item, ancestor and collection-folder tags. `IsVisibleViaTags` normalizes tags, checks blocked tags first, then allowed tags; ratings are an additional intersection. Therefore an allowed series tag also admits future episodes in native tag logic.

KidGuard creates opaque `KidGuard:<profile UUID>:<generation UUID>` tags. Only approved playable leaves and series/season navigation nodes receive them; library roots never do. The corresponding user receives that single AllowedTags value. The immutable approved playable set adds the missing exact-episode check. Route/query/body item references are checked before the action, and returned JSON item references are pruned. Native folder authorization and native download/live-TV permissions remain effective.

The guard is not an independent streaming server or a replacement for Jellyfin authentication. Its tested API scope and exclusions are documented in SECURITY.md. Third-party middleware, unauthenticated DLNA and direct filesystem shares are outside the supported boundary.

## Apply and rollback

1. Validate profile revision, administrator exclusion and analysis completion.
2. On explicit initial approval only, create a user with a random temporary name, disable it, set its password, then rename it.
3. Write a pending journal containing the original full policy, pre-apply draft/access, approved settings, staged tag and intended tag targets.
4. Disable the user. Pending recovery also causes the guard to deny managed requests.
5. Stage the new generation's tags, then update native policy.
6. Commit the exact playable/navigation sets, approved settings and undo journal durably.
7. Remove the now-inert prior generation tags and reconcile the family catalog.

A failed pre-commit mutation leaves the user disabled and the journal available. Startup does no internet lookup; interrupted-apply recovery disables the affected user. Administrator Undo reconstructs the prior generation, restores the full policy/profile snapshot, removes staged tags and commits recovery. Ordinary Undo restores the state before the most recent apply, including its pre-apply draft. Only one undo level is retained.

Completed applies are not rolled back because of a later tag-cleanup failure. Inert tags may remain; they grant nothing without the corresponding user-policy tag. Downloaded or already buffered media cannot be recalled.

## New media

New IDs remain outside approved snapshots. Review/Block profiles record pending new items accordingly. Automatic additions require an already-approved opt-in setting, confidence ≥90 (configurable higher), eligible movie/episode evidence, and no conflicting manual/container decision. They evaluate using the **last approved settings**, retain the current native permissions, and never publish unrelated draft edits. Reanalysis can reconsider previously uncertain new items after better metadata arrives. Initially configured profiles always require parent approval.

## Tradeoffs

The local JSON snapshot keeps installation simple. A 10,000-item planner test runs in the suite, but full-library serialization and DTO post-filtering should be load-tested against a representative production catalog. Native pagination counts can be approximate when the final guard removes inherited-tag matches. Episode clients and transcoding/HLS variants need broader compatibility coverage before a stable security release. A future server-native exact item authorization extension would eliminate this guard and inherited-tag workaround.
