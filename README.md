# orthomania

osu!mania where notes approach angled instead of straight down, as a patch for official osu!(lazer) rather than a copy of the game.

Starting osu! starts plain lazer. Starting `orthomania.exe` checks for updates, then starts lazer with orthomania hooked in. Both use osu!'s own data, so maps, skins, scores and settings changed in one are there in the other. orthomania keeps only its own data apart, and the patcher never opens osu!'s data or its sign-in.

![The patcher window in each of its states](docs/patcher-states.png)

## How it works

On every launch the patcher:

1. finds official lazer (`%LOCALAPPDATA%\osulazer`) and checks that orthomania's folders are apart from it and that orthomania's data folder holds none of osu!'s data;
2. if it is a released `orthomania.exe` and a newer patcher is out, downloads it, checks it against the SHA-256 GitHub lists for the file, puts it in place of itself and starts it, which carries on from here (see [Releases](#releases));
3. waits until no lazer is running ("close osu! to continue");
4. updates lazer if a newer release is out, with lazer's own updater (Velopack, the same feed and `Update.exe` lazer uses);
5. downloads the newest orthomania build made for exactly the installed lazer release when it is newer than the one it has, checking every file against its hash;
6. checks the build's DLLs against the installed lazer's files: every assembly they need must be in lazer's folder (or come with the build), osu!'s and its framework's at exactly the version the build was compiled against, any other at that version or newer. The newest build that passes is used; a newer one that does not is passed over, and the patcher says so;
7. prepares the folder lazer runs from for orthomania, and waits on **Ready**;
8. on **Launch**, checks for updates again if the window has been open 10 minutes or more, checks the folder again (lazer's files, orthomania's files against their hashes, and the DLL check against the lazer files beside them), then starts lazer from that folder.

Without a connection it starts the newest build it already has that passes the checks.

### Finding osu!

The patcher looks for lazer where its installer puts it (`%LOCALAPPDATA%\osulazer`). If it is not there, it searches the computer once and remembers what it found (`found-installs.json`):

- the places osu! records for itself: lazer's uninstall entry and its `osu://` link, stable's `.osz` and `osu!` links and uninstall entry, and any `osu!.exe` running now;
- the usual folders (`%LOCALAPPDATA%\osu!`, `osu!` or `Games\osu!` at the top of each drive, `Program Files`);
- then each fixed drive and your Desktop, Documents, Downloads and Games folders, three levels down. It only lists folders and looks for `osu!.exe` in them, skips Windows' and other programs' folders, and stops after 60,000 folders or 10 seconds.

It finds osu!(stable) as well and lists it, but orthomania runs on lazer. **Find osu!** (on the main screen when lazer is not found, or **osu! installs** in Options) lists what was found, picks a lazer, searches again, or opens Windows' folder picker to choose a folder by hand. A folder chosen by hand is saved as `lazerFolder` in `settings.json`, and then it is the only place the patcher looks.

If there is no build for a new lazer release yet, plain lazer is updated anyway and orthomania waits for its build. It never starts an older release instead: osu!'s data is shared, and an older release cannot open what a newer one has written.

| Folder | What is in it | Changed by |
| --- | --- | --- |
| `%LOCALAPPDATA%\osulazer` | Official lazer | Only lazer's own updater |
| `%APPDATA%\osu` (or wherever osu! moved it) | osu!'s data: maps, skins, scores, settings, the osu! sign-in | osu!, started plainly or by orthomania. Never the patcher |
| `%LOCALAPPDATA%\orthomania\lazer\<release>` | Hard links to the installed release's files, plus the ruleset DLL, `0Harmony.dll` and `orthomania-game.json` | The patcher |
| `%LOCALAPPDATA%\orthomania\builds`, `logs`, `settings.json` | Downloaded builds, the patcher's log, the patcher's settings | The patcher |
| `Documents\Projects\osu!\ortho-data` (or `%APPDATA%\orthomania\data`) | orthomania's own data: its settings, play log, replays, plays waiting for the score server, its score server sign-in | The ruleset, inside lazer |
| `%LOCALAPPDATA%\orthomania\server` | The score server's data, when the patcher starts it (development only) | The score server |

Lazer loads `osu.Game.Rulesets.*.dll` from the folder it runs from, and the DLL puts its hooks in as it loads. There is no `framework.ini` or `storage.ini` in that folder, so lazer keeps its data exactly where plain lazer does (the patcher refuses a folder that has either). A hard link is a second name for the same file, so the folder costs no space and is byte for byte the official release. Only the installed release's folder is kept.

### What orthomania may touch

- **The patcher** reads and writes only its own folder (`%LOCALAPPDATA%\orthomania`), orthomania's data folder (where it only leaves a note) and lazer's install (which it reads, links and updates with lazer's own updater). It never opens osu!'s data folder, so it never sees osu!'s settings, database or saved sign-in. It checks orthomania's data folder is not osu!'s by path, and by the names of the files in orthomania's own folder.
- **The game side** (the ruleset DLL, inside lazer) is told by `orthomania-game.json` beside it where orthomania's data folder is, and whether it may use the osu! sign-in. It keeps everything of its own there, never in osu!'s data: its settings in `settings\` (`mania.json`, `highway.json`, `score-server.json` with the score server sign-in), and the play log, replays and plays waiting for the score server in `maniaortho\`. By default (`"osuSignIn": false`) it keeps osu! signed out while it is loaded: lazer's API is given an empty copy of osu!'s settings that lives only in memory, so it neither uses nor changes the sign-in osu! saved, and plain osu! stays signed in as it was. You can still sign in to osu! in the game; that sign-in is forgotten when the game closes. `"osuSignIn": true` in the patcher's `settings.json` allows the saved sign-in.

Builds made before this (manifest format 1) expected a copy of osu!'s data of their own, so the patcher does not start them. An orthomania data folder that still holds such a copy (`client*.realm`, `game.ini`) stops the patcher with a note to move osu!'s files out.

## Releases

Each build is a GitHub release tagged `lazer-<lazer release>-<build>` (for example `lazer-2026.1005.0-1.0.12`) with `orthomania.json` and the files it lists:

```json
{
  "format": 2,
  "lazer": "2026.1005.0",
  "build": "1.0.12",
  "commit": "…",
  "files": [
    { "name": "osu.Game.Rulesets.ManiaOrtho.dll", "sha256": "…", "size": 123456 },
    { "name": "osu.Game.Rulesets.ManiaOrtho.pdb", "sha256": "…", "size": 123456 },
    { "name": "0Harmony.dll", "sha256": "…", "size": 123456 }
  ]
}
```

The builds are made by [`lazer-builds.yml`](.github/workflows/lazer-builds.yml). Every hour it looks at lazer's newest release on each channel (lazer and tachyon). For one without a build, it:

1. downloads that release's own files from lazer's updater, the same package `Update.exe` installs, checked against the SHA-256 the updater lists;
2. builds the ruleset against exactly those files, and runs all its tests against them on Windows (including that the hooks still find what they hook, and that lazer still loads rulesets before it signs in);
3. publishes the build. orthomania.exe picks it up on its next launch.

A change to the ruleset on main, or a run by hand (**Run workflow**, optionally naming a release), builds again with a higher build number, which replaces the older build on each player's next launch. A build that fails is left as a draft release with a link to the run, so the hourly check does not try it again until the ruleset changes. NuGet's `ppy.osu.Game` is not used for releases, because ppy does not publish every lazer release there.

A build is only ever put beside the lazer release it names, and it can add files to lazer's but never replace one. Format 2 promises what [What orthomania may touch](#what-orthomania-may-touch) asks of the game side.

### The patcher

The patcher itself is released as `patcher-<version>`, with `orthomania.exe` as its one file: published for `win-x64`, the patcher is a single self-contained exe with the .NET runtime, osu-framework and its native libraries inside, so players need nothing installed and nothing beside it. On first start it unpacks the native libraries to `%TEMP%\.net\orthomania`.

To release it, run [`patcher-release.yml`](.github/workflows/patcher-release.yml) from main (**Actions**, **Patcher release**, **Run workflow**) with a version higher than the last `patcher-<version>`. It runs the patcher's tests, publishes `orthomania.exe` with that version, checks it is the only file and that it starts, and publishes the release with `orthomania.exe` and its SHA-256, marked as the repository's latest so the releases page offers it (build releases are never marked latest).

A released `orthomania.exe` updates itself: on each start it looks for a higher `patcher-<version>`, downloads its `orthomania.exe` beside itself, checks it against the SHA-256 GitHub lists for that file, renames itself to `orthomania.exe.old` (Windows lets a running program be renamed, not deleted), puts the new one in its place, and starts it. The new one waits for the old one to close, removes `orthomania.exe.old`, and carries on. If any step fails, the old one stays in place and says a newer patcher is out. `"updatePatcher": false` in the patcher's `settings.json` turns this off. An `orthomania.exe` built from a clone (`launch.bat`) never replaces itself; it only says a newer one is out.

Releases are listed newest first, a hundred at a time. A build or patcher release older than that (for an osu! release that has not changed in a while) is found by its tag.

## Launching from a clone

Double-click `launch.bat` at the top of the clone. It builds first on every run, so it always runs the clone's current code:
the patcher as one file, `artifacts\launch\orthomania.exe`, and the score server in `artifacts\launch\server`, where the
patcher's dev option looks for it (see [below](#starting-the-score-server-with-the-game-development-only)). The first time
takes a minute or two; later ones only rebuild what changed, a few seconds when nothing did. Then it opens `orthomania.exe`,
which does everything above. To release it, use the [patcher release workflow](#the-patcher) rather than this file: a
released `orthomania.exe` carries its version and updates itself, one built here does neither. It needs the
.NET 10 SDK, and says so if it is missing. If orthomania is already open, it leaves it open.

## Building

Needs the .NET 10 SDK. The patcher and the score server are built and tested apart, each from its own folder:

| Folder | What it is |
| --- | --- |
| `patcher/` | The patcher: `Orthomania.Patcher.sln` |
| `patcher/ruleset/` | The game side: the ruleset lazer loads (`osu.Game.Rulesets.ManiaOrtho`), its hooks into lazer, and its tests |
| `server/` | The score server: `OrthoServer.sln`, with its own tests, API contract and scripts ([server/README.md](server/README.md)) |

The files at the top (`global.json`, `Directory.Build.props`, `.editorconfig`, CI) are shared by all of them.

```
dotnet test patcher/Orthomania.Patcher.sln
dotnet publish patcher/src/Orthomania.Patcher -c Release -r win-x64 -o artifacts/patcher   # one file: artifacts/patcher/orthomania.exe
dotnet test patcher/ruleset/osu.Game.Rulesets.ManiaOrtho.Tests
```

The ruleset builds against the lazer release in `patcher/ruleset/lazer-version.txt`, from NuGet. To build and test it against another release's own files, as the release workflow does:

```
python patcher/ruleset/release.py fetch --tag 2026.1005.0-lazer --to artifacts/lazer
dotnet test patcher/ruleset/osu.Game.Rulesets.ManiaOrtho.Tests -p:LazerProps=%CD%\artifacts\lazer\lazer.props
```

It runs on stock lazer: the few changes it needs in lazer itself are hooks (`patcher/ruleset/osu.Game.Rulesets.ManiaOrtho/Hooks/`, with [Harmony](https://github.com/pardeike/Harmony)) put in as lazer loads the DLL. Each one is skipped, with a line in lazer's log, if a release no longer has what it hooks, and the tests fail on such a release, so it gets no build.

| Project | What it is |
| --- | --- |
| `patcher/src/Orthomania.Patcher.Core` | Everything the patcher does, without its window |
| `patcher/src/Orthomania.Patcher` | The window (`orthomania.exe`), drawn with osu-framework in lazer's style |
| `patcher/tests/Orthomania.Patcher.Tests` | Tests on stand-in installs, feeds, builds and servers, including Velopack's real update code |

The window uses lazer's Torus font, read at run time from the installed lazer's `osu.Game.Resources.dll`; lazer's resources are licensed separately, so they are never shipped here. Without lazer it falls back to Roboto.

To see a state without checking or changing anything: `orthomania --demo ready` (also `starting`, `waiting`, `updating`, `getting`, `notout`, `notinstalled`, `problem`, `notice`, `options`, `installs`). Add `--screenshot file.png` to save it and close, and `--lazer-resources path\to\osu.Game.Resources.dll` to use Torus from elsewhere.

### Starting the score server with the game (development only)

For testing, the patcher can start the score server when you press **Launch** and stop it when osu! closes. Turn it on in **Options** ("Start the score server with osu! (dev)"), or in `%LOCALAPPDATA%\orthomania\settings.json`:

```json
"devServer": {
  "enabled": true,
  "executable": null,
  "arguments": null,
  "dataFolder": null,
  "healthUrl": "http://127.0.0.1:7727/api/health",
  "startTimeoutSeconds": 30
}
```

With everything left at `null` it runs `server\OrthoServer.exe` (or `OrthoServer.dll`, with `dotnet`) beside the patcher as `OrthoServer serve --port 7727 --data %LOCALAPPDATA%\orthomania\server --parent-pid <patcher>`. In `arguments`, `{port}`, `{data}` and `{pid}` are filled in. If a server already answers at `healthUrl`, it is used and left running. The server's output goes to `logs\server.log`. The patcher only starts or waits for a server on this computer (`127.0.0.1`, `localhost` or `::1`). If the server does not start, osu! starts anyway with a notice, and the game sends its plays once a server answers.

This option goes away once the server runs on its own for public playtesting.

## Status

The patcher, the game side (`patcher/ruleset/`) and the score server (`server/`) are all here. Builds are published by the release workflow once it runs from main.

osu!, osu!(lazer) and their logos are ppy's; orthomania is not affiliated with ppy.
