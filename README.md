# albion-local-server

An educational server emulator, written in C# (.NET 10), for studying how an MMO client and server talk to each other
(Photon UDP transport, Protocol18 serialization, encrypted handshakes, world simulation). It is designed to run on your
own machine or LAN, for one player or a few friends.

## Disclaimer — please read

- **Educational project.** This is a personal learning and research project about network protocols and game-server
  architecture. It is not a commercial product and is not meant to replace, compete with or harm any official service.
- **No affiliation.** This project has nothing to do with Albion Online, Sandbox Interactive GmbH or any of their
  partners. It is not endorsed, sponsored or supported by them. "Albion Online" is a trademark of its owner and is
  mentioned only to describe what the code interoperates with.
- **No client, no client patches.** This repository does **not** contain, distribute or link to the game client, any
  client file, any modified client, any client patch or any tool that modifies the client. It contains no game assets
  and no game data files. At startup the server reads the GameData files of a client installation that
  *you* already have; nothing is copied into this repository.
- **No anti-cheat circumvention.** Nothing here bypasses, disables or interferes with any anti-cheat or protection
  system, and such contributions will not be accepted.
- **Never use it against official servers.** The code only *answers* a client as a server. It does not connect to,
  automate or interfere with official servers or other players.
- **Your responsibility.** How (and whether) you point a client at a local server is entirely your own decision and
  responsibility; check the terms of service that apply to you. The authors provide no support for that.

## What works

The server implements a large part of what a PvE player meets in the game, as a local, self-contained emulation:

- **Core:** login and game servers over Photon UDP (reliable channels, fragmentation, DH/AES encryption), accounts
  with PBKDF2 passwords, several characters per account, GM levels and a chat command system, SQLite persistence with
  schema migrations.
- **World:** cluster loading from the client's GameData, visibility, cluster travel, paid fast travel by cargo, per-city
  banks and markets, islands with farming and animals, stables and mounts, crafting and refining stations in every city
  with city bonuses, enchanting, quality rolls.
- **Combat:** auto-attacks, spells with conditions and effects, buffs and crowd control, mob AI with path finding and
  line of sight, boss mechanics (spawn/death casts, dashes, knockbacks), knock-down and party revive, PvP flags and
  duels (the optional PvE preset limits PvP to consent).
- **PvE content:** open-world camps and chests, elite mobs, world bosses, journals, on-demand invasions, expeditions,
  random dungeons with the client's own layout generator and shrines, static dungeons, corrupted dungeons, hellgates,
  Roads of Avalon, the Mists, season goals and bounties.
- **Social:** chat server (whispers, guild, party, world), friends, mail, trading, guilds with ranks, contracts and goals,
  emotes, loadouts, map pings.
- **Bots:** simple player bots (city walkers, gatherers, patrols).

The detailed, per-feature reference (English) is in [docs/REFERENCE.md](docs/REFERENCE.md); the architecture notes
(Russian) are in [docs/ARCHITECTURE.ru.md](docs/ARCHITECTURE.ru.md). Both mention a private analysis workspace
(`../analysis`, `../captures`) that is not part of this repository.

## Repository layout

| Path | Contents |
|---|---|
| `src/AlbionLocal.Photon` | Photon UDP transport, Protocol18 serializer, encryption, message layer |
| `src/AlbionLocal.Protocol` | Message classes (generated) and wire rules |
| `src/AlbionLocal.Server` | Login (5055), game (5056) and chat (5057) servers, world simulation, systems, persistence |
| `tests/AlbionLocal.SelfTest` | Offline self-test with an in-process client (no network, no real client needed to run it, but it reads the GameData files) |
| `tools/AlbionLocal.CapDecode` | Decoder for packet captures of your own sessions |
| `config/*.jsonc` | Five required configuration files by area (JSONC: JSON with comments; not standard JSON) |
| `content/` | Server-side content: spawns, kits, events, dungeons, goals, bots and other tunables |

## Offline protocol exercises

The self-test accepts `--client-gamedata=<folder>`, `--only=<text>`, `--report=<file.json>`
and `--lab=all|serialization|transport|session|messages` for repeatable offline checks.
Missing private golden references are reported as SKIP, separately from PASS and FAIL.
`--protocol-trace` enables parameter-key/type traces without parameter values.
SQLite is the only implemented persistence backend; repository contracts allow future extensions.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Windows, Linux or macOS (developed on Windows)
- The GameData folder of a client installation you own (`<client>/Albion-Online_Data/StreamingAssets/GameData`).
  The server will not start without it.

## Client version

The server is built against client build **1.32.030.345319** (`albiononline-win32-full-1.32.030.345319`, the first
line of the client's `version.txt`). The protocol (operation and event codes, message fields) and the GameData formats
follow that build.

Other builds are not supported: an update can renumber operations and events or change the data files, and the server
may then fail to start or misbehave in game. At each start the server logs the build and a SHA-256 of every GameData
file it reads, keeps them in `data/clientdata.json`, and warns when they differ from the previous start.

## Build and run

```bash
dotnet build
```

Tell the server where the GameData folder is, either in `config/common.jsonc` (`Paths:ClientGameData`) or on the
command line, then start it from the repository root (it keeps its database and log in `./data`):

```bash
dotnet run --project src/AlbionLocal.Server --no-launch-profile -- -v --Paths:ClientGameData="C:/Games/AlbionOnline/game/Albion-Online_Data/StreamingAssets/GameData"
```

On start it prints the database schema version and the three listening ports. By default it listens on `127.0.0.1`
only; see the "second PC" part of [docs/REFERENCE.md](docs/REFERENCE.md) for LAN settings (`Network:BindAddress`,
`Network:PublicAddress`) and the firewall ports.

Run the self-test. By default it reads `../game-local/Albion-Online_Data/StreamingAssets/GameData` next to the repository.
Alternatively, pass your installation's GameData folder directly (no directory link is needed):

```bash
dotnet run --project tests/AlbionLocal.SelfTest
```

```powershell
dotnet run --project tests/AlbionLocal.SelfTest -- '--client-gamedata=C:/Games/AlbionOnline/game/Albion-Online_Data/StreamingAssets/GameData' --report=data/lab/selftest.json
```

Comparisons against generated "golden" data files are reported as SKIP, because those files are not published.
They do not count as passed tests. Exit code 0 means no selected test failed; inspect the skip count for coverage.

## Configuration

- `config/common.jsonc`, `login.jsonc`, `world.jsonc`, `chat.jsonc`, `persistence.jsonc` — five required files.
  Distributed settings use `official`, rates ×1 and the opening tutorial for new characters.
  See [configuration](docs/CONFIGURATION.md) for file ownership, validation, paths and temporary GM rates.
  Any key can be overridden on the command line, e.g. `--Log:Level=Debug`.
- `content/*.json` — gameplay content the server adds or tunes (events, bosses, dungeons, goals, bots, kits…).
- Accounts: unknown logins are rejected when `Accounts:AutoRegister` is false. Register through the client
  or use `.account create <name> <password>` on the server console; see [configuration](docs/CONFIGURATION.md).

With the opening tutorial enabled, the Lighthouse (`1008`) is private to each character.
The Cove (`1011`), forest (`1009`) and fort (`1010`) are shared: nearby players can see one another,
while quest progress and tutorial obstacles remain individual. Reconnecting preserves the authored
cluster and saved quest state. Tutorial merchant offers show one item and remain available after purchase.

Timed crafting delivers the whole batch and its fame together at the end of the progress bar.
Cancelling or logging out settles only the portion completed by elapsed crafting time and refunds
the uncompleted portion; crafting still dismounts a rider before starting.

The Destiny Board HUD automatically tracks a node from the latest fame gain, preferring
activity-specific nodes over general fame. The selection persists across map changes and reconnects.
This selection policy has automated coverage; its exact match to the official game's policy remains unverified.

## Status

Work in progress and built for learning: expect gaps, guesses (marked in the docs as inferences) and breaking changes.
Issues about protocol details or server architecture are welcome; requests involving the official game, its servers,
its client or anti-cheat are not.

## License

MIT (see [LICENSE](LICENSE)): you may use, change and share the code freely, as long as you keep the copyright
notice, i.e. credit the original source (https://github.com/MrGreenCheezz/albion-local-server). The license covers this
repository's own code only; it grants nothing about the game, its client, its data or its trademarks.
