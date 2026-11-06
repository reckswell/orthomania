#!/bin/bash
# Sets up signing in with osu! on your score server (see setup-osu-sign-in.ps1).
#   server/scripts/setup-osu-sign-in.sh [--data <folder>] [--port <port>]
set -euo pipefail
server="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [ "$(uname)" = Darwin ]; then
  data="$HOME/Library/Application Support/orthomania/server"
else
  data="${XDG_DATA_HOME:-$HOME/.local/share}/orthomania/server"
fi

args=()
while [ $# -gt 0 ]; do
  case "$1" in
    --data) data="$2"; shift ;;
    *) args+=("$1") ;;
  esac
  shift
done

exec dotnet run --project "$server/OrthoServer" -c Release -- setup-osu-sign-in --data "$data" ${args[@]+"${args[@]}"}
