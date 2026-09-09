# Installation, hosting, upgrade and removal

## Manual installation

Use Jellyfin **12.0.0** with its matching web client. This release is not compatible with 10.11.x. Stop the server, create `plugins/KidGuard_0.1.2.0` under its data directory, and extract the release ZIP there. Its top level should contain `Jellyfin.Plugin.KidGuard.dll`, `KidGuard.Core.dll`, and `meta.json`. Restart. The server log should show both assemblies loaded and the plugin Active.

Open Dashboard → KidGuard. Analyze first; then set up a draft. Initial setup never applies without the explicit Apply button. Existing administrator accounts are rejected, and a Jellyfin user cannot be mapped to two child profiles. New profiles use an age, not a birthdate. Profile labels remain local; metadata tag IDs contain no child name.

Optional TMDb: create an API Read Access Token using TMDb's official account/API process, enable the provider and select its certification country. Blank token on save keeps the existing secret; Remove stored token clears it. Run analysis after changing providers. The plugin remains functional without an external provider.

## Host a plugin repository

Run `python3 scripts/package.py --base-url https://YOUR-HOST.example/kidguard/`. Upload `repository.json`, the named ZIP, its checksums and corresponding source to that location using your hosting service. In Jellyfin, add the `repository.json` HTTPS URL under Dashboard → Plugins → Repositories, then find KidGuard in the catalog. The public GitHub catalog is `https://raw.githubusercontent.com/Ai-Slop-For-You/jellyfin-plugin-kidguard/main/manifest.json`. The generic package template remains intentionally non-routable until a hosting URL is supplied.

The repository entry contains plugin GUID `f2247450-a459-4c15-9ee2-9e56c8737ce1`, version `0.1.2.0`, target ABI `12.0.0.0`, source ZIP URL, timestamp and MD5 (Jellyfin's manifest format). Check the supplied SHA-256 separately if installing manually.

## Upgrade

1. Disable managed child accounts during the maintenance window. Stop Jellyfin.
2. Back up Jellyfin's database/configuration plus `<data>/kidguard/state.json` together.
3. Keep one installed KidGuard version. Replace both DLLs and metadata with the matching package; never mix Core and Plugin versions.
4. Restart; confirm plugin Active and no recovery warning. Check a denied and an allowed title with a child account before re-enabling ordinary access.
5. Reanalyze when changing providers/rating algorithms, review new recommendations and explicitly apply.

Do not upgrade Jellyfin's major/minor API target without a matching tested plugin build. An API mismatch can prevent plugin loading; inherited tags alone cannot reproduce exact snapshot security.

## Undo and interrupted apply

Use the profile's **Undo last apply / Recover** button. A pending journal leaves its account disabled; this button restores the prior policy and removes the staged tags. The audit record confirms restoration. One undo level per profile is stored.

Undoing a profile's first apply restores its original user policy. For an existing user this may restore broad access; the UI explicitly confirms that consequence. A newly created account's previous policy is disabled, so initial Undo leaves it disabled. Undo restores the pre-apply draft and approved access, not an unlimited history of every edit.

For unrecoverable filesystem/database errors, keep child users disabled, stop Jellyfin, restore the paired Jellyfin and KidGuard backups, restart and verify access. Do not delete state to clear a warning.

## Uninstall

First disable affected child users. If the goal is to revert the last apply, use Undo and review the restored policy. Otherwise manually configure replacement native restrictions while the users remain disabled. Stop Jellyfin and remove only the KidGuard plugin directory. Retain state/backups until satisfied with the replacement controls. The plugin does not delete users or silently restore unrestricted policies on removal.

Opaque KidGuard tags may be removed later through normal metadata editing, after replacement permissions are configured. The `KidSafe` family tag is informational. Existing media files and ordinary metadata tags are not deleted by KidGuard.

## GitHub Releases hosting

The included `.github/workflows/release.yml` builds and tests a version tag, uploads the plugin ZIP, corresponding source, checksums and test results as a GitHub preview release, then updates `manifest.json` on the default branch. Existing version entries are preserved. The manifest is published only after the release upload succeeds. GitHub Pages is not required.

1. Create an empty **public** GitHub repository (suggested name: `jellyfin-plugin-kidguard`) and push this source to its default branch.
2. For a first release, run **Publish Jellyfin plugin → Run workflow** with tag `v0.1.2.0`, or push that tag. Actions must be enabled and allowed to write repository contents. Default-branch protection must permit the workflow's manifest update; otherwise the release succeeds but the catalog update needs a reviewed PR.
3. Wait for **Publish Jellyfin plugin** to succeed.
4. Add `https://raw.githubusercontent.com/OWNER/jellyfin-plugin-kidguard/BRANCH/manifest.json` to Jellyfin's plugin repositories, replacing OWNER and BRANCH with the actual account and default branch. Install KidGuard from the catalog and restart Jellyfin.

The manifest deliberately lists the tested preview, even though GitHub labels its release as a prerelease. Compatibility remains Jellyfin 12.0.0. Hosting here does not add the plugin to Jellyfin's official repository.

For subsequent versions, update the package version in `scripts/package.py`, `Directory.Build.props` and `build.yaml`, then push the matching four-component version tag. Published version tags and assets should be immutable: use a new version to fix a release. If a workflow fails after uploading assets, fix the catalog problem and run `scripts/publish-manifest.py` with the generated manifest and an appropriately authorized `GH_TOKEN`; do not replace already published binaries.
