# Score server contract

What the game (the maniaortho ruleset, `ScoreServer/`) and the score server (`server/OrthoServer`) agree on. Both test
suites read these files, so a change on one side that the other does not follow fails a test. Until the ruleset moves into
this repository, the osu! fork (`reckswell/osu`) keeps a copy in `ortho-server/contract`: change it here first, then copy it there.

| File | What |
|---|---|
| `Messages/` | The upload (`PlayUpload`, `BeatmapUpload`), answer (`PlayResult`) and protocol (`ScoreServerProtocol`) types, one per file, compiled into both the game's ruleset and the server. |
| `play-upload.example.json` | The `play` part of `POST /api/plays` (multipart: `play` JSON, `beatmap` the `.osu` file, `replay` the `.osr`, optional). |
| `play-result.example.json` | The server's answer. |
| `pp-parity.json` | Star rating and pp of synthetic plays. The game's calculators and osu!'s published ones must both give these. |
| `SyntheticManiaMap.cs` | The synthetic maps those plays are on (compiled into both test projects). |
