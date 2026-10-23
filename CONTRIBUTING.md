# Contributing

This fork develops and validates changes for the original
[albion-local-server repository](https://github.com/MrGreenCheezz/albion-local-server).
Keep the MIT license, copyright notice and attribution.

## Language and scope

Use English for new shared code comments, documentation, commit messages, branch names,
pull request titles and pull request descriptions. Conversations and personal learning
notes may remain in Spanish. Existing upstream documents in other languages are not
renamed or translated as part of an unrelated change.

An upstream pull request should contain one coherent change: implementation, relevant
tests, required configuration or dependency changes, and English documentation explaining
the behavior. Do not submit the fork's complete development history as one proposal.

Keep personal launchers, machine-specific paths, local settings, save databases, logs,
private captures, client binaries, GameData exports and generated analysis out of upstream
contributions. Personal Spanish walkthroughs and session notes stay in the learning fork
or ignored local storage. A reusable tool may be proposed separately with portable
defaults and English documentation.

## Branches and pull requests

Follow [the fork's Git workflow](docs/GIT-WORKFLOW.md):

- `main`: stable releases of this fork.
- `develop`: integration of reviewed development work.
- `feature/<topic>` and `fix/<topic>`: focused work based on `develop`.
- `hotfix/<topic>`: urgent corrections based on `main`, also integrated into `develop`.
- `release/<version>`: optional stabilization based on `develop`.
- `contribution/<topic>`: a curated upstream proposal based on current `upstream/main`.

Fork pull requests normally target `develop`. Upstream proposals target the original
repository's `main`, unless its maintainer requests another base. Keep an existing
published contribution branch while its pull request is under review.

## Validation

Build from the repository root with the .NET 10 SDK:

```powershell
dotnet build AlbionLocal.slnx --nologo -m:1 -nr:false -p:UseSharedCompilation=false -p:NuGetAudit=true -p:NuGetAuditMode=all
```

For the self-tests, point to a client installation you own. Replace the example path:

```powershell
dotnet tests/AlbionLocal.SelfTest/bin/Debug/net10.0/AlbionLocal.SelfTest.dll --client-gamedata="C:/path/to/Albion-Online_Data/StreamingAssets/GameData" --report="data/lab/selftest.json"
```

Use `--only=<text>` for a relevant subset during development. Before proposing a release
or a broad contribution, run the available full suite and inspect the final diff against
the actual target branch. Report passed, failed and skipped counts separately, identify
the tested client build, and distinguish automated checks from real-client observations.
Missing private references remain explicit skips; do not manufacture golden references
from the implementation under test. Use temporary test databases and preserve real saves.

For configuration changes, keep the five area files and their ownership consistent; see
[configuration](docs/CONFIGURATION.md). Validate with `--check-config` and cover file parsing,
in-memory validation and command-line precedence. Configuration validation must not open services,
logs or save databases. Never use a real player's database as a test fixture.

## Review and publication

Describe the problem, the resulting behavior, the validation and any remaining limitations.
Include persistence or migration implications when applicable. Avoid unrelated formatting
and dependency changes. Use descriptive English commits such as
`fix: initialize journal NPC state on arrival`.

For this collaboration, the repository owner normally performs commits, pushes, pull
request creation, merges and release publication, unless a specific action is explicitly
delegated. Authorization to organize or commit local work does not authorize a push,
upstream PR update or release publication.
