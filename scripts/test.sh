#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
"${DOTNET:-dotnet}" test KidGuard.sln -c Release -p:RestoreLockedMode=true -m:1
