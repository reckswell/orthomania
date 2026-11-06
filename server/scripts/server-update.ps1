# Moves the score server to the osu!mania calculator version osu! runs live (after a pp rework), then checks it.
#   powershell -ExecutionPolicy Bypass -File server\scripts\server-update.ps1
# Restart the server afterwards: on start it rates every stored play again with the new version, and your total follows.
#
# It reads the newest release of ppy/osu-queue-score-statistics (osu!'s score processor) on GitHub, writes the version of
# osu!mania's calculator it uses into server\calculator-version.txt, rebuilds the server with that version and runs its
# tests. A failing pp parity test then only means the new version rates differently from the game's own copy: the game's
# in-game pp (and its toast) will differ from the server's until the game takes the same osu! update.
$ErrorActionPreference = "Stop"
$server = Split-Path $PSScriptRoot -Parent

$file = Join-Path $server "calculator-version.txt"
$before = (Get-Content $file -Raw).Trim()

& dotnet run --project (Join-Path $server "OrthoServer") -c Release -- check-live --write $file
if ($LASTEXITCODE -ne 0) { Write-Host "Could not read osu!'s live version (offline?). Nothing changed."; exit 1 }

$after = (Get-Content $file -Raw).Trim()
if ($after -eq $before) { Write-Host "Already on osu!'s live version ($after)."; exit 0 }

Write-Host "== $before -> $after: rebuilding the server and running its tests"
& dotnet test (Join-Path $server "OrthoServer.Tests") -c Release -nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Some tests failed (see above). If only 'Parity:' tests failed, osu! now rates differently from the game's copy of the calculators; the server is right, the game's own pp is behind."
}
Write-Host "Restart the server to rate your plays again with $after."
