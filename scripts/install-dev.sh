#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "$0")/.." && pwd)
test_data=${1:?Usage: install-dev.sh /path/to/.dev/data}
if [[ ! -f "$test_data/../.kidguard-test-server" ]]; then
  echo 'Refusing installation without the isolated test-server marker.' >&2
  exit 1
fi
mkdir -p -- "$test_data/plugins/KidGuard"
cp -- "$repo_dir/src/Jellyfin.Plugin.KidGuard/bin/Release/net10.0/Jellyfin.Plugin.KidGuard.dll" "$repo_dir/src/Jellyfin.Plugin.KidGuard/bin/Release/net10.0/KidGuard.Core.dll" "$test_data/plugins/KidGuard/"
echo 'Installed into the marked test directory. Start/restart that isolated server.'
