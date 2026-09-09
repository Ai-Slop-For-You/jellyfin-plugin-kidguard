#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
"${DOTNET:-dotnet}" build KidGuard.sln -c Release -p:RestoreLockedMode=true -m:1
