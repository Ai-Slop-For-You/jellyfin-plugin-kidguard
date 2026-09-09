# Research and API findings

Sources inspected 2026-09-08. Decisions below distinguish official behavior from KidGuard's own design.

## Jellyfin target and APIs

The [official release API](https://api.github.com/repos/jellyfin/jellyfin/releases/latest) identified `v12.0`; the [tagged server project](https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Server/Jellyfin.Server.csproj) targets net10.0, and the [NuGet Controller index](https://api.nuget.org/v3-flatcontainer/jellyfin.controller/index.json) includes 12.0.0. The official [plugin template](https://github.com/jellyfin/jellyfin-plugin-template) still targeted 10.11.5/net9.0 at inspection; its versions were not copied blindly. The [Playback Reporting plugin](https://github.com/jellyfin/jellyfin-plugin-playbackreporting) demonstrates hosted-service registration through `IPluginServiceRegistrator`.

[User documentation](https://jellyfin.org/docs/general/server/users/adding-managing-users/) describes rating/tag/folder controls. The authoritative [12.0 BaseItem implementation](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Entities/BaseItem.cs) shows `GetInheritedTags`, `IsVisibleViaTags`, and `IsParentalAllowed`: inherited item/ancestor/library tags, normalized matching, blocked-first checks, allowed-tag intersection, then rating/custom-rating and unrated handling. Container exceptions also exist. This rules out treating a series approval tag as an exact episode allowlist.

The [user manager interface](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Library/IUserManager.cs) exposes user creation, password changes, policy updates and DTO retrieval. [UserPolicy](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Model/Users/UserPolicy.cs) contains native allowed/blocked tags, ratings, folders and permissions. [ILibraryManager](https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Library/ILibraryManager.cs) provides paged item queries, change events and asynchronous metadata updates. Movie/Series/Season/Episode entities are separate. KidGuard scans and snapshots episodes independently.

The live browser test identified the current web client's ES-module loader. [viewContainer source](https://github.com/jellyfin/jellyfin-web/tree/v12.0/src/components) and the actual 12.0 bundle resolve plugin `data-controller` URLs dynamically. The implementation uses `export default`, not the legacy AMD `define` wrapper.

## Streaming UX comparison

| Service | Official behavior relevant here | KidGuard adaptation |
|---|---|---|
| Netflix | Per-profile maturity limits and explicit blocked titles; changing restrictions requires identity verification. [Help](https://help.netflix.com/en/node/114276) | Individual profiles, explicit overrides, administrator-only editing. |
| Disney+ | Content ratings affect browsing/search; Junior Mode, profile PINs, profile-creation restrictions, and explicit handling of some unrated programming. [Help](https://help.disneyplus.com/article/disneyplus-en-de-parental-controls) | Clear basic settings, conservative unknowns, server-enforced browsing/playback. Adult-account protection is documented; no pretend client PIN. |
| Prime Video | PIN-protected profiles and purchase/viewing restrictions; account PIN privileges matter across profiles. [Help](https://www.primevideo.com/-/de/help/?nodeId=T2VYxsiqT2CP35ChTf) | Keep child accounts separate from administrator authority and surface account permissions. |
| Max | Separate Kids/Adult profiles, per-profile settings and ratings, adult profile PINs and kid-proof exit controls. [Help](https://help.max.com/DO/Answer/Detail/000002539) | Simple per-child dashboard with persistent configuration and protected adult accounts. |
| Hulu | Kids profiles and PIN Protection provide a child-oriented streaming space. [Help](https://help.hulu.com/article/hulu-restrict-content) | Guided onboarding and a clear review step, with more granular title/episode decisions for a local library. |

KidGuard does not claim to reproduce these services' licensed curation or proprietary scoring. Calibration and explicit title decisions are its own transparent approach.

## Advisory evidence and legitimate providers

[Common Sense Media's methodology](https://www.commonsensemedia.org/about-us/our-mission/about-our-ratings) separates age/developmental guidance from content categories such as violence/scariness, sex/nudity and language. [BBFC guidance](https://www.bbfc.co.uk/what-we-do/classification-guidelines) and its [threat/horror guide](https://www.bbfc.co.uk/parents-guide-age-ratings/bbfc-guide-threat-and-horror) likewise distinguish content issues and context. These inform the multidimensional model. Their individual reviews are not scraped, redistributed or fabricated; no entitlement to a commercial review API is assumed.

TMDb offers structured [movie release certifications](https://developer.themoviedb.org/reference/movie-release-dates) and [TV content ratings](https://developer.themoviedb.org/reference/tv-series-content-ratings). Its [rate-limit guidance](https://developer.themoviedb.org/docs/rate-limiting) requires clients to handle rate limiting even though its old fixed limit was removed. KidGuard uses a fixed official API host, numeric IDs, bounded requests, timeout, backoff, throttling, cache and explicit failure evidence. These endpoints are not a source of comprehensive violence/fear/sex descriptors.

OMDb and TVDb are potential future adapters; neither is implemented or represented as a source in current assessments. LLM analysis is omitted because there is no necessary trusted, licensed advisory corpus or reason to make private child-profile data depend on a hosted model. Local structured metadata is the implemented extension point for detailed advisories.
