# Educational lab contribution notes

Upstream: https://github.com/MrGreenCheezz/albion-local-server

Working priorities and acceptance criteria: [Hoja de trabajo y contribuciones](HOJA-DE-RUTA.es.md).
The first proposed review unit is the native SQLite dependency fix. Extract and validate each unit against
current upstream before submitting it; the integration branch is not itself a single upstream proposal.

Keep the MIT notice and attribution. No client files, GameData exports, private captures, account credentials,
local settings or generated runtime reports belong in a contribution.

Suggested review units:

1. **Portable self-tests and honest results:** optional `--client-gamedata`, explicit skipped golden comparisons,
   JSON report, and a nonzero exit for an empty selection. Preserve the historical GameData path by default.
2. **Educational protocol lab:** four opt-in stages reusing existing transport/crypto/login checks, synthetic
   examples, metadata-only tracing and tests proving that unknown/nested values and response tokens remain hidden.
3. **Reproducible onboarding:** PowerShell entrypoint, ignored local settings and the Spanish walkthrough.

The current branch assembles these units into a runnable lab. They can be extracted for separate upstream reviews;
the runner's lab stage selection depends on the exercise definitions, so keep those together when extracting.
No upstream issue or pull request is opened automatically by these scripts.

Validation before offering a change:

```powershell
./scripts/lab.ps1 -Mode build
./scripts/lab.ps1 -Mode learn -Trace
./scripts/lab.ps1 -Mode test
./scripts/lab.ps1 -Mode content
```

Report passed, failed and skipped counts separately and identify the exact client build. Confirm invalid arguments
and unmatched filters return 2. Missing private golden references must remain skipped; do not generate a reference
from the same implementation merely to obtain a passing comparison. Never claim real-client interoperability
based only on the in-process client. Maintainer acceptance is independent of local validation.
