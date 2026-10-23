# Server configuration

The server reads five required JSONC (JSON with comments) files from `config` in the current working directory.
These `.jsonc` files support comments and trailing commas; they are not standard JSON.
There is no main configuration file or manifest. `albionlocal.json` is no longer read; move any private
settings to their owning files manually before starting. Keep all five files when selecting another directory.

| File | Options |
|---|---|
| `common.jsonc` | `Preset`, network bind/public addresses, GameData/content paths and logging |
| `login.jsonc` | Login port, accounts, registration and maintenance |
| `world.jsonc` | Game port, tutorial, character progression, rates and other gameplay rules |
| `chat.jsonc` | Chat port and communication settings |
| `persistence.jsonc` | `Persistence:Provider` and `Paths:Saves` |

Individual options may be omitted to use preset defaults. The distributed files select `official`,
rates ×1 and the tutorial for new characters. The preset name does not establish complete fidelity
to the original server. Existing characters keep their progress. SQLite is the only supported provider;
the database remains `albionlocal.db` inside `Paths:Saves`.

## Select and check settings

```powershell
dotnet run --project src/AlbionLocal.Server --no-launch-profile -- --config-dir ./config --check-config
dotnet run --project src/AlbionLocal.Server --no-launch-profile -- --config-dir=./config --Rates:Fame=2
```

Relative operational paths are resolved against the parent of the selected configuration directory.
The startup summary prints absolute paths and explicit argument overrides. Precedence is preset,
file settings, explicit arguments, then temporary runtime commands. Unknown, duplicate or misplaced
options, invalid values and conflicting ports stop startup before services or saves are opened.
`--check-config` validates without opening listeners, databases or log files, and does not load GameData.

## Visual Studio: F5

1. Open `AlbionLocal.slnx` in Visual Studio and set `AlbionLocal.Server` as the startup project.
2. In the launch-profile selector next to the start button, select **Check configuration** and press
   **F5** to validate the five files without starting services or opening saves.
3. Select **Albion Local Server** and press **F5** to start the emulator. This starts the server;
   launch the game client separately using its existing launcher.

Both profiles use the repository root as their working directory and read `config/*.jsonc` directly.
F5 does not read `lab.local.json` and does not require the laboratory launcher.
They do not override GameData, save paths, rates or tutorial settings. Before starting the server,
set `Paths:ClientGameData` in `common.jsonc` to your installed client's GameData folder, and verify
`Paths:Saves` in `persistence.jsonc` points to the save folder you intend to use.

The profiles in `Properties/launchSettings.json` target Visual Studio, which expands MSBuild properties
such as `$(MSBuildProjectDirectory)`. The .NET CLI interprets launch profiles differently and may not
expand this property in the installed SDK. For command-line use, run the commands above from the
repository root with `--no-launch-profile`, or use the laboratory launcher below. See the official
[Visual Studio launch-profile documentation](https://github.com/dotnet/project-system/blob/main/docs/launch-profiles.md)
and [.NET CLI launch-profile behavior](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-run#visual-studio-and-debugger-extensions).

The **Check configuration** profile has also been verified with `dotnet run` from the repository root:

```powershell
dotnet run --project src/AlbionLocal.Server --launch-profile "Check configuration"
```

For this CLI invocation, start in the repository root explicitly; do not rely on Visual Studio's
working-directory property to relocate the command-line process.

## Rates and future administration

Edit `Rates` in `world.jsonc` for permanent changes. Combat fame uses `Fame × CombatFame`, gathering uses
`Fame × GatheringFame`, and crafting uses `Fame × CraftingFame`; existing game bonuses and rounding remain.
The general fame multiplier also applies to existing sources such as tomes.

`.rates` displays the active rates. An Admin can use `.rates fame 2` to change the entire running world;
logging out does not reset it. `.rates reset` restores effective startup values. Restarting discards
temporary changes. The `save` argument is rejected without changing values. Old `rates.json` files
are preserved but ignored, with a warning.

Files are read at startup in this version. Rates are classified for future reload support; other options
require restart. There is no reload command, file watcher or administrative panel yet. The configuration
model and in-memory validation are shared extension points for those features. Already awarded rewards
and scheduled timers are not recalculated by a rate change.

## Laboratory launcher

```powershell
./scripts/lab.ps1 -Mode config -ConfigDir ./config -ArtifactsPath data/lab/server-configuration-build
./scripts/lab.ps1 -Mode server -ConfigDir ./config
./scripts/lab.ps1 -Mode server -ConfigDir ./config -OpeningTutorial
```

`-ConfigDir` defaults to the repository's `config` folder. A supplied relative directory is resolved
from the caller's directory before the launcher enters the repository. Server and configuration-check
modes build the selected output before running it. Configuration checks may use a nonexistent GameData
path because they validate settings without loading the data.

The normal laboratory server explicitly overrides saves/logs to `data/lab/server` under the repository;
`-OpeningTutorial` uses `data/lab/tutorial-server`, enables the tutorial and selects an isolated build
output by default. It does not change the preset. The launcher prints its overrides. All other values,
including tutorial settings in normal mode, come from the selected files. Content and tutorial checks
do not override the selected saves/log paths.
