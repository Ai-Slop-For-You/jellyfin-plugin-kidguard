# Validation evidence

Target: official **Jellyfin Server 12.0.0**, Jellyfin Web 12.0, .NET SDK 10.0.401, Linux x64. No production Jellyfin server was used. The integration server bound only to 127.0.0.1:18096 and used generated two-second blue videos and synthetic NFO metadata.

## Automated suite

`dotnet test` covers:

- Age/certification boundaries, conservative/unknown defaults and independent children.
- All nine dimensions, missing explicit-dimension evidence and conflicting/failed providers.
- Calibration limits, contradictions, locally selected examples and override precedence.
- Current series/season descendants, episode exceptions and unknown future IDs.
- Tag removal/idempotence and preservation of unrelated metadata.
- Atomic state round trip and corruption handling; full policy/rollback serialization.
- Mocked TMDb success, failure, missing ratings, disabled provider, request privacy, episode isolation and caching.
- Native policy construction and nested JSON result filtering.
- Draft policy isolation; injected metadata failure, restart and durable recovery; native apply/undo.
- Automatic additions use approved settings, do not publish draft permissions, require an approved opt-in and reject unknown new items.
- Ordinary vs persistent override behavior.
- A 10,000-item recommendation/approval planner run (not a full production server load test).

The release directory includes the actual `unit-tests.trx` emitted during packaging. Test counts and outcomes in that file are authoritative.

## Live-server suite

`integration-results.json` records 30 assertions using actual admin and child authentication tokens. Positive and negative controls cover draft/no-user creation, explicit approval, admin API exclusion, native policy installation, direct item lookup, playback info, direct video bytes, episode exceptions, cross-user requests, recursive browsing, search, recent items, Next Up, recommendations, Live TV/download rejection, tag preservation, new episodes before/after analysis, independent profiles, stale revisions, revocation and undo.

Fixtures were built by setup-test.py. A live-fixture issue (new locked episodes did not populate title/index metadata immediately) was corrected in the test harness by identifying additions by new item IDs rather than trusting metadata timing. Assertions were then rerun successfully.

## Browser QA

Jellyfin Web 12.0 was opened in a real browser. The first check found and corrected an incompatible AMD controller wrapper; the embedded controller now uses an ES-module default export. The rendered dashboard, native plugin navigation entry, library table, server-side search filter and source/unknown-dimension explanations were inspected. UI actions are serialized while asynchronous saves are pending. Profile setup, calibration and family/settings screens are included in the browser checklist below.

- [x] Plugin appears in Jellyfin dashboard navigation.
- [x] Dashboard loads profiles and audit records with no initialization exception.
- [x] Library table displays actual server results; search narrows to the expected item.
- [x] Details distinguish missing metadata from actual advisory evidence.
- [x] Profile form and calibration saved; final rebuilt batch-save refreshed the decision; family/settings screens rendered.

Final browser verification also found and fixed JSON parsing of successful empty POST responses. The packaged fix was verified by saving an AlwaysBlock decision and observing the refreshed table. See SECURITY.md for unsupported paths and the limits of these results. The GitHub Actions workflow does not automatically run the standalone server suite; it runs the automated suite and produces artifacts.
