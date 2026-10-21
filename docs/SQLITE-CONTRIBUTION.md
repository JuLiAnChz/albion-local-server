# Proposed contribution: native SQLite security update

Branch: `fix/sqlite-native-security`; commit: `ea91393`; upstream base: `bdb2c60`.
Local checkout: sibling directory `sqlite-security`. No upstream pull request has been published.

## Suggested title

Update native SQLite bundle and audit transitive dependencies

## Suggested description

Microsoft.Data.Sqlite 10.0.8 resolves SQLitePCLRaw.lib.e_sqlite3 2.1.11, which is flagged for
[CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). Reference SQLitePCLRaw.bundle_e_sqlite3
2.1.14 explicitly while retaining Microsoft.Data.Sqlite 10.0.8. Enable transitive NuGet auditing in the server
project and add a persistence check that the SQLite library actually loaded is at least 3.50.2.

Validation on the isolated branch, 21 October 2026: solution build completed with zero warnings and errors;
all six persistence checks passed, including existing save rollback and retry checks; the runtime reported
SQLite 3.54.0. The solution-wide transitive dependency audit reported no known vulnerable packages.
These checks did not use the user's game database.

## Reproduce

```powershell
dotnet build AlbionLocal.slnx --nologo -m:1 -nr:false -p:UseSharedCompilation=false
dotnet tests/AlbionLocal.SelfTest/bin/Debug/net10.0/AlbionLocal.SelfTest.dll --only=persistence:
dotnet list AlbionLocal.slnx package --vulnerable --include-transitive
```

Only the server project file and `PersistenceTest.cs` differ from upstream. Laboratory scripts, client launch
instructions, GameData, generated output and the persistence architecture changes are excluded.
