# Build and develop

Prerequisites: .NET SDK 10, Python 3, and network access to NuGet on first restore. No Node build is required; the UI is an embedded ES module.

```sh
dotnet restore KidGuard.sln --locked-mode
./scripts/build.sh
./scripts/test.sh
python3 scripts/package.py
```

Set `DOTNET=/absolute/path/to/dotnet` if the SDK is not on PATH. The package script builds Release, runs tests, creates the installable ZIP, SHA-256 checksums, and a repository manifest template in `artifacts/`. It does not publish or install anything.

```sh
python3 scripts/package.py --base-url https://YOUR-HOST.example/kidguard/
```

This produces `repository.json` with the actual ZIP URL and Jellyfin's required MD5 checksum. Host it, the ZIP and corresponding source together. The default template uses `example.invalid` deliberately and is not a usable repository URL. SHA-256 is also supplied for manual verification.

## Isolated real-server tests

Use the official Jellyfin 12.0 runtime and ffmpeg. The scripts never select an existing system service or default production data directory.

```sh
./scripts/dev-server.sh /absolute/path/to/jellyfin /absolute/path/to/jellyfin-web
# In another terminal, after startup:
python3 scripts/setup-test.py
python3 scripts/integration.py --credentials .dev/credentials.json --media .dev/media --report .dev/integration-results.json
```

`dev-server.sh` creates a `.dev/.kidguard-test-server` marker and binds to `127.0.0.1:18096`. Setup refuses a completed server or a missing marker. Fixtures are generated blue two-second videos, not copyrighted media. Random passwords/tokens remain in owner-readable `.dev/credentials.json`, which is gitignored. Only disposable fixture accounts are created. The test script refuses the wrong server ID, an incompatible version or a missing fixture library. Do not reuse test credentials elsewhere.

Stop with Ctrl-C. The standalone runtime must be stopped before replacing the plugin:

```sh
./scripts/install-dev.sh .dev/data
```

The install helper requires the test marker and copies only the two built DLLs. The real-server test suite is separate from `dotnet test`; unit tests mock API providers and failure paths and need no running Jellyfin.

## CI

The included GitHub Actions workflow builds/tests/packages on .NET 10 and uploads release files as CI artifacts. It does not publish a public release. Server and NuGet API packages are pinned to 12.0.0; changing that target requires re-reading tag/authorization behavior and rerunning the live access matrix.
