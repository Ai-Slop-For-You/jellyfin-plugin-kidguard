#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
server_binary=${1:?Usage: dev-server.sh /absolute/path/to/jellyfin /absolute/path/to/jellyfin-web}
web_directory=${2:?Specify the matching Jellyfin web directory}
command -v ffmpeg >/dev/null
mkdir -p .dev/{data,config,cache,logs,media/movies,media/tv}
touch .dev/.kidguard-test-server
if [[ ! -f .dev/config/network.xml ]]; then
cat > .dev/config/network.xml <<'XML'
<?xml version="1.0" encoding="utf-8"?><NetworkConfiguration><BaseUrl /><EnableHttps>false</EnableHttps><RequireHttps>false</RequireHttps><InternalHttpPort>18096</InternalHttpPort><PublicHttpPort>18096</PublicHttpPort><EnableIPv4>true</EnableIPv4><EnableIPv6>false</EnableIPv6><EnableRemoteAccess>false</EnableRemoteAccess><LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses><EnableUPnP>false</EnableUPnP></NetworkConfiguration>
XML
fi
./scripts/install-dev.sh "$PWD/.dev/data"
exec "$server_binary" --datadir "$PWD/.dev/data" --configdir "$PWD/.dev/config" --cachedir "$PWD/.dev/cache" --logdir "$PWD/.dev/logs" --webdir "$web_directory" --ffmpeg "$(command -v ffmpeg)"
