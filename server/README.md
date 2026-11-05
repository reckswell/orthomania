# orthomania score server

Your own score server for orthomania, on your own computer. The game sends it each play you pass or fail (once you turn
on **Settings > maniaortho > Send plays to my score server**), and it keeps them, rates them with osu!'s own osu!mania pp
and shows an unofficial total at <http://127.0.0.1:7727/>. You link the game to your account by signing in with your osu!
account, on osu!'s own page. It is not osu!: it is not a copy of osu!'s server and takes no osu! scores. Of osu!'s API it
uses only the public sign-in, to learn who you are.

## Run it

```powershell
powershell -ExecutionPolicy Bypass -File server\scripts\server.ps1          # Windows
server/scripts/server.sh                                                     # Linux/macOS
```

The patcher can also start it with osu! and stop it afterwards, for development: see "Starting the score server with the
game" in the [repository's README](../README.md).

It listens on `127.0.0.1:7727` (this computer only). Options: `-Port`, `-Data`, `-NoLiveCheck` (`--port`, `--data`,
`--no-live-check` for the shell script; the server itself also takes `--parent-pid <pid>` to stop once that process has
ended). Data lives in `%LOCALAPPDATA%\orthomania\server` (`~/.local/share/orthomania/server` on Linux), the folder the
patcher's dev option uses too:

| Path | What |
|---|---|
| `plays.db` | Every play (SQLite): judgements, mods, map, the highway settings it was played with, its rating. Accounts and linked games. |
| `beatmaps/<md5>.osu` | The map file of each play, so plays can be rated again later. |
| `replays/<id>.osr` | Each play's replay (osu!mania format, opens in osu!mania). |
| `osu-oauth.json` | The server's OAuth application on osu! (client id and secret), once set up. Never commit or share it. |

Every play is first written to `ortho-data\maniaortho\outbox` and leaves it only once the server has stored it. If the
server is not running, the address is wrong or the server fails, the play waits there and goes, in order, with a later
play. A play the server refuses (or fails on five times) is kept in `outbox refused` with the reason in `error.txt`;
move its folder back to `outbox` to send it again. The game never follows a redirect and never contacts a `ppy.sh`
address, whatever the setting says.

## Sign in with osu!

Your account on the server is your osu! account: the game links itself to it through osu!'s own sign-in page, so you
never make a second account and never type your password anywhere but osu.ppy.sh.

**Set it up once** (the server's owner): register an OAuth application on osu! and give the server its id and secret.

```powershell
powershell -ExecutionPolicy Bypass -File server\scripts\setup-osu-sign-in.ps1   # or server/scripts/setup-osu-sign-in.sh
```

It says what to do: on <https://osu.ppy.sh/home/account/edit#new-oauth-application> make a "New OAuth Application" with
the callback URL `http://127.0.0.1:7727/auth/osu/callback`, then paste its Client ID and Client Secret. They are kept in
`osu-oauth.json` in the data folder (readable by you only on Linux and macOS); the server reads it on each sign-in, so no
restart is needed.

**Link the game** (each player): in **Settings > maniaortho**, press **Link with osu!**. The browser opens on osu!'s
page; sign in there if you are not already, and allow it. The browser then says "Signed in as ..." with the same code the
game shows, and the game's plays go to your account from then on. **Unlink** in the same place ends it.

What the server gets and keeps:

- **Your password never reaches it.** You type it on osu.ppy.sh. osu! hands the browser back a one-time code, which the
  server swaps for a token with its client secret, as osu!'s API documents (authorization code grant).
- **Only your public profile.** The token is for the `identify` scope, the smallest osu! has: it reads `/api/v2/me` (your
  id, username, profile picture and country) and nothing else. The server then revokes the token at once and keeps none of
  it: no token, no password, no e-mail address. It never calls any other part of osu!'s API, never follows a redirect
  from osu!, and talks only to `https://osu.ppy.sh`.
- **The game gets the server's own token, not osu!'s.** It is random, starts with `ortho_`, is stored only as a hash, and
  ends when you unlink or after 180 days without a play.
- **A sign-in finishes only in the browser that started it.** Each one has its own state, used once and gone after 10
  minutes, and a cookie ties it to that browser; the game alone holds the code that collects the result. Web pages cannot
  start a link (they cannot send the game's header) or change anything else here.
- **The same osu! account always signs in to the same account here**, renamed when you rename on osu!. An account made
  with a password here is never taken over by an osu! account of the same name.

## The game's sign-in panel (osu! fork only)

Builds of the osu! fork (`reckswell/osu`) have **Settings > Online > Server**, which signs lazer itself in to this
server with an account and password made here. Stock lazer, which the patcher runs, has no such setting; use **Link with
osu!** there.

1. Start the server (above).
2. In the game: **Settings > Online > Server > Server address**: `http://127.0.0.1:7727` (or just `127.0.0.1:7727`).
   Restart the game; the note under the address says when it is connected.
3. Open the sign-in panel (top right). It says "on the server at http://127.0.0.1:7727". Press **Register** once to
   make your account (username 3-15 characters, password at least 8; the email is optional and never used), then sign
   in as you would on osu!. "Stay signed in" works as usual.
4. Turn on **Settings > maniaortho > Send plays to my score server**. Plays now go under your account.

Your profile (click your name) shows the server's pp, rank, accuracy, grades, best plays with their weights and recent
plays. Lazer's profile only has the four osu! rulesets, so your maniaortho figures appear under **osu!mania** (4K and 7K
as osu!mania's variants). Leave the address empty to go back to osu!'s own server; osu!'s addresses (`ppy.sh`) are never
taken there.

- **Your account is on this server only.** It has nothing to do with your osu! account; use a different password. The
  password is stored as a PBKDF2 hash, sign-in tokens as hashes (a day for the access token, 90 days to stay signed in).
- **Plain http only on this computer.** Lazer sends everything over https; it uses plain http only to a server on a
  loopback address (127.0.0.1, localhost, ::1), so a password never crosses a network unencrypted. A server on another
  computer is reached over https, whatever the address says.
- **Use the same address in both settings.** Plays carry your sign-in only to the exact address the game signed in to
  (`localhost` and `127.0.0.1` count as different). The note under the score server address says whose account plays go
  to. A play sent with a sign-in the server no longer accepts (expired, signed out) waits in the outbox for the next send.
- **A sign-in stays with its server.** Switching servers signs you out; a saved osu! sign-in is never sent to your
  server, nor your server's to osu!.
- **The first account owns the plays sent before it existed**, and plays sent while no one is signed in. The score page
  at <http://127.0.0.1:7727/> shows that account.
- **Not served**: multiplayer, spectating and live updates (lazer's SignalR hubs; it retries them quietly), chat, beatmap
  listings and downloads, and beatmap leaderboards (lazer shows online leaderboards only for its own four rulesets).
  osu!mania scores sent by lazer's normal submission are refused ("This server rates maniaortho plays only"); they stay
  in your local scores.

## How plays are rated

Exactly as osu! rates an osu!mania score made in lazer, with osu!'s published calculators (NuGet
`ppy.osu.Game.Rulesets.Mania`) at the version osu!'s live score processor uses
(`ppy/osu-queue-score-statistics`, newest release). The version is in `calculator-version.txt`.

- **Which plays award pp**: passes on maps that are Ranked or Approved (as the game knows the map's status), with only
  mods osu! awards pp for at their default settings (DT at 1.5x, not 1.3x). Classic does not count (lazer scores),
  Loved and Qualified maps do not. Other plays are still rated ("80pp if it counted") and listed.
- **Total pp**: per map and mod combination, the best-pp play and the best-score play are kept (osu! deletes the
  others); the best 1000 by pp; per map only the best counts; weighted 0.95^n; plus the bonus
  `(417 - 1/3) * (1 - 0.995^n)` over the kept plays. Accuracy is weighted the same way.
- **Small differences from osu!**: osu! stores stars and difficulty attributes as floats from a separate calculator
  deploy, so its pp can differ from the server's in the second decimal; a few maps on osu!'s pp blacklist would award
  pp here; osu! deletes a play the preserve rule drops for good, the server keeps it and applies the rule again after
  each rework; the map's status is whatever your game last saw (a newly ranked map counts once the game knows it).
- **The game's pp and the server's**: the game shows pp from its own copy of the calculators. `contract/pp-parity.json`
  pins both to the same values on synthetic plays (4K and 7K, DT, HT, NF, EZ, HD, MR); the toast after a play says if
  they ever differ.

## When osu! changes pp

The server checks osu!'s live version at start and every 12 hours (it reads the score processor's newest release from
GitHub; besides osu!'s sign-in, that is its only outside request). When it differs, the page shows a banner and the toast after each play says so.
To move:

```powershell
powershell -ExecutionPolicy Bypass -File server\scripts\server-update.ps1   # or server/scripts/server-update.sh
```

It writes the new version into `calculator-version.txt`, rebuilds the server and runs its tests. Restart the server: it
rates every stored play again with the new version before it starts serving, and your total follows. If only the
`Parity:` tests fail, osu! now rates differently from the game's copy of the calculators: the server is right, the
game's in-game pp is behind until the game takes the same osu! update.

## API (for the game; all JSON fields are snake_case)

| Method | Path | What |
|---|---|---|
| `GET` | `/` | The score page. |
| `GET` | `/api/health` | `{"status": "ok", "version", "calculator_version"}`, reading nothing: for whatever starts the server to wait on. |
| `GET` | `/api/status` | Calculator version, osu!'s live version, number of plays, whether osu! sign-in is set up. |
| `GET` | `/api/profile` | Total pp, accuracy, bonus, best plays with weights. |
| `GET` | `/api/plays?limit=50` | Latest plays. |
| `POST` | `/api/plays` | Multipart: `play` (`contract/play-upload.example.json`), `beatmap` (the `.osu` file), `replay` (optional). Answers `contract/play-result.example.json`, or 400 `{"error": ...}`. The same `score_id` twice is stored once. |
| `POST` | `/api/recalculate` | Rate every play again. |
| `POST` | `/api/link` | Start linking the game: `contract/link-start.example.json` (a device code for the game, a user code, the address to open). 503 until osu! sign-in is set up. |
| `POST` | `/api/link/poll` | Form field `device_code`: `contract/link-status.example.json`, `status` `pending`, `linked` (with the game's `token`, once), `denied` or `expired`. |
| `DELETE` | `/api/link` | Unlink the game whose token it carries (`Authorization: Bearer`). |
| `GET` | `/auth/osu/start?link=` | The browser, opened by the game: on to osu!'s sign-in page. |
| `GET` | `/auth/osu/callback` | The browser, sent back by osu!: reads the public profile, links the game, says so. |

Every `POST` under `/api` (not `/api/v2`) needs an `X-ManiaOrtho` header (any value); without it the server answers 403.
Browsers cannot add it without asking first, so a web page you visit cannot post plays to your server. Any request that
changes something and comes from a web page (it carries `Origin` or `Sec-Fetch-Site`, which the game never sends) is
refused, on every path. A play sent with the game's token (`Authorization: Bearer`) goes to that account.

What lazer asks for after it signs in (`OrthoServer/Lazer`), in the shapes of lazer's own types (`LazerApiTest`
deserializes each answer with them):

| Method | Path | What |
|---|---|---|
| `POST` | `/oauth/token` | Sign in (`grant_type=password`, username or email) or stay signed in (`refresh_token`). Any client id. |
| `POST` | `/users` | Register: `user[username]`, `user[user_email]`, `user[password]`; 422 with `form_error.user` on a problem. |
| `DELETE` | `/api/v2/oauth/tokens/current` | Sign out. |
| `GET` | `/api/v2/me/{ruleset?}` | You, with statistics (pp, rank, accuracy, grades, level, 4K/7K) for osu!mania. |
| `GET` | `/api/v2/users/{id or name}/{ruleset?}`, `/api/v2/users?ids[]=` | Profiles. |
| `GET` | `/api/v2/users/{id}/scores/{best,recent}`, `/api/v2/users/{id}/beatmapsets/most_played` | Best plays with weights, recent plays (`include_fails=1` for fails), most played maps. |
| `GET` | friends, blocks, favourites, kudosu, recent activity, chat, notifications, seasonal backgrounds | Empty answers, so lazer's screens load. `notifications` points to a websocket that needs the sign-in. |
| `POST` | `/api/v2/beatmaps/{id}/solo/scores` | Refused (403): osu! scores are not taken. |

Anything else answers 404 `{"error": "This server does not do that."}` and is logged once per path.

`OrthoServer recalculate --data <folder>` and `OrthoServer check-live [--write calculator-version.txt]` do the same from
the command line.

## Layout and tests

| Path | What |
|---|---|
| `OrthoServer/` | The server (ASP.NET Core minimal API, SQLite). Builds against NuGet `ppy.osu.Game*` at `calculator-version.txt`, not against the game. |
| `OrthoServer.Tests/` | Profile maths, pp rules, the HTTP API on a real port, signing in with osu! against a stand-in for osu!'s OAuth (`OsuSignInTest`), the fork's sign-in and lazer's API (`LazerApiTest`), recalculation, the live-version check, stopping with `--parent-pid`, parity. |
| `contract/` | What the game and the server agree on: the upload and answer types, their examples, the synthetic maps and `pp-parity.json`. |
| `scripts/` | Start the server (`server.ps1`, `server.sh`), set up osu! sign-in (`setup-osu-sign-in.*`) and move the server to osu!'s live calculator version (`server-update.*`). |

```
dotnet test server/OrthoServer.sln
dotnet publish server/OrthoServer -c Release -r win-x64 --self-contained -o artifacts/server
```

CI runs the tests on Windows and Linux. The published folder is what the patcher's dev option starts (`server\OrthoServer.exe`
beside `orthomania.exe`).

Until the ruleset moves into this repository, the game keeps its own copy of `contract/` in the osu! fork
(`reckswell/osu`, `ortho-server/contract`): change it here first, then copy it there. To regenerate `pp-parity.json` from
the game's calculators (only when a calculator change is intended), run there `ORTHO_WRITE_PP_PARITY=1 dotnet test
osu.Game.Rulesets.ManiaOrtho.Tests --filter ScoreServerParityTest` and copy the file back.
