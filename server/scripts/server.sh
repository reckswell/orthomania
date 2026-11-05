#!/bin/bash
# Runs the orthomania score server on this computer (see server.ps1 for what it does and does not contact).
#   server/scripts/server.sh [--data <folder>] [--port <port>] [--no-live-check]
# Data defaults to the folder .NET calls LocalApplicationData, plus orthomania/server (the patcher's dev option uses the
# same one). Open http://127.0.0.1:7727/ for the score page.
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

exec dotnet run --project "$server/OrthoServer" -c Release -- serve --data "$data" ${args[@]+"${args[@]}"}
