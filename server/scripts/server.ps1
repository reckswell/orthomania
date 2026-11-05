# Runs the orthomania score server on this computer.
#   powershell -ExecutionPolicy Bypass -File server\scripts\server.ps1
#   powershell -ExecutionPolicy Bypass -File server\scripts\server.ps1 -Port 7728 -NoLiveCheck
# Then open http://127.0.0.1:7727/ for your unofficial pp, best plays and latest plays. Ctrl+C stops it.
#
# It listens on this computer only (127.0.0.1) and rates plays with osu!'s published osu!mania calculators, at the version
# osu!'s live score processor uses (server\calculator-version.txt). Its only outside requests: which version osu! runs, read
# from GitHub (ppy/osu-queue-score-statistics, public) at start and every 12 hours (-NoLiveCheck turns that off), and, once
# osu! sign-in is set up, osu!'s public sign-in API (osu.ppy.sh) when you sign in. When osu! moves to a new calculator
# version, run server-update.ps1.
# Its data (plays.db, the map files and replays of your plays) is kept in %LOCALAPPDATA%\orthomania\server, the folder the
# patcher's dev option uses too.
param(
    [string]$Data,
    [int]$Port = 7727,
    [switch]$NoLiveCheck,
    [switch]$NoBuild
)
$ErrorActionPreference = "Stop"
$server = Split-Path $PSScriptRoot -Parent

if (-not $Data) { $Data = Join-Path $env:LOCALAPPDATA "orthomania\server" }
$Data = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Data)

$arguments = @("run", "--project", (Join-Path $server "OrthoServer"), "-c", "Release")
if ($NoBuild) { $arguments += "--no-build" }
$arguments += @("--", "serve", "--data", $Data, "--port", $Port)
if ($NoLiveCheck) { $arguments += "--no-live-check" }

& dotnet @arguments
exit $LASTEXITCODE
