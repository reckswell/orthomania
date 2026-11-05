# Sets up signing in with osu! on your score server (once): asks for the client id and secret of an OAuth application you
# register on osu!, and keeps them in the server's data folder, on this computer only.
#   powershell -ExecutionPolicy Bypass -File server\scripts\setup-osu-sign-in.ps1
#   powershell -ExecutionPolicy Bypass -File server\scripts\setup-osu-sign-in.ps1 -Port 7728 -Data <folder>
# Players then sign in on osu!'s own page; the server reads only their public profile and never sees a password.
param(
    [string]$Data,
    [int]$Port = 7727
)
$ErrorActionPreference = "Stop"
$server = Split-Path $PSScriptRoot -Parent

if (-not $Data) { $Data = Join-Path $env:LOCALAPPDATA "orthomania\server" }
$Data = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Data)

& dotnet run --project (Join-Path $server "OrthoServer") -c Release -- setup-osu-sign-in --data $Data --port $Port
exit $LASTEXITCODE
