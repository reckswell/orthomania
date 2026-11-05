# orthomania

osu!mania where notes approach angled instead of straight down, as a patch for official osu!(lazer) rather than a copy of the game.

Starting osu! starts plain lazer. Starting `orthomania.exe` checks for updates, then starts lazer with orthomania hooked in. Neither touches the other's files or data.

![The patcher window in each of its states](docs/patcher-states.png)

## How it works

On every launch the patcher:

1. finds official lazer (`%LOCALAPPDATA%\osulazer`) and checks that orthomania's folders and data are apart from it;
2. waits until no lazer is running ("close osu! to continue");
3. updates lazer if a newer release is out, with lazer's own updater (Velopack, the same feed and `Update.exe` lazer uses);
4. gets the orthomania build made for exactly the installed lazer release, checking every file against its hash;
5. prepares the folder lazer runs from for orthomania, and waits on **Ready**;
6. on **Launch**, backs up the database before a new lazer release first opens it, starts lazer from that folder, and moves screenshots to osu!(stable) once it closes.

If there is no build for a new lazer release yet, plain lazer is updated anyway and the patcher offers the last release orthomania worked on.

| Folder | What is in it | Changed by |
| --- | --- | --- |
| `%LOCALAPPDATA%\osulazer` | Official lazer | Only lazer's own updater |
| `%APPDATA%\osu` | Plain lazer's data | Only plain lazer |
| `%LOCALAPPDATA%\orthomania\lazer\<release>` | Hard links to that release's files, plus the ruleset DLL, `0Harmony.dll`, `framework.ini` and `storage.ini` | The patcher |
| `%LOCALAPPDATA%\orthomania\builds`, `backups`, `logs` | Downloaded builds, database backups, the patcher's log | The patcher |
| `Documents\Projects\osu!\ortho-data` (or `%APPDATA%\orthomania\data`) | orthomania's game data | lazer, when the patcher starts it |

Lazer loads `osu.Game.Rulesets.*.dll` from the folder it runs from, and the DLL puts its hooks in as it loads. `framework.ini` beside the game keeps lazer's storage there, and `storage.ini` points it at orthomania's data. A hard link is a second name for the same file, so the folder costs no space and is byte for byte the official release; it also keeps an older release whole after lazer updates, to fall back on.

## Releases

Each build is a GitHub release tagged `lazer-<lazer release>-<build>` (for example `lazer-2026.1026.0-0.1.0`) with `orthomania.json` and the files it lists:

```json
{
  "format": 1,
  "lazer": "2026.1026.0",
  "build": "0.1.0",
  "files": [
    { "name": "osu.Game.Rulesets.ManiaOrtho.dll", "sha256": "…", "size": 123456 },
    { "name": "0Harmony.dll", "sha256": "…", "size": 123456 }
  ]
}
```

A build is only ever put beside the lazer release it names, and it can add files to lazer's but never replace one. The patcher itself is released as `patcher-<version>`.

## Building

Needs the .NET 10 SDK.

```
dotnet test Orthomania.sln
dotnet publish src/Orthomania.Patcher -c Release -r win-x64 --self-contained -o artifacts/patcher
```

| Project | What it is |
| --- | --- |
| `src/Orthomania.Patcher.Core` | Everything the patcher does, without its window |
| `src/Orthomania.Patcher` | The window (`orthomania.exe`), drawn with osu-framework in lazer's style |
| `tests/Orthomania.Patcher.Tests` | Tests on stand-in installs, feeds and builds, including Velopack's real update code |

The window uses lazer's Torus font, read at run time from the installed lazer's `osu.Game.Resources.dll`; lazer's resources are licensed separately, so they are never shipped here. Without lazer it falls back to Roboto.

To see a state without checking or changing anything: `orthomania --demo ready` (also `starting`, `waiting`, `updating`, `getting`, `fallback`, `notinstalled`, `problem`, `notice`, `options`). Add `--screenshot file.png` to save it and close, and `--lazer-resources path\to\osu.Game.Resources.dll` to use Torus from elsewhere.

## Status

The patcher comes first. The ruleset, its hooks and the score server move here from the fork once their own work lands; until then there are no builds to download.

osu!, osu!(lazer) and their logos are ppy's; orthomania is not affiliated with ppy.
