# Git workflow for the learning fork

## Purpose and remotes

Use a lightweight GitFlow model for this fork, with a separate upstream contribution
path. Branch names organize work; small diffs, review and reproducible validation provide
the actual quality controls. Do not introduce a release branch for every change.

| Remote | Repository | Purpose |
| --- | --- | --- |
| `origin` | `JuLiAnChz/albion-local-server` | Publish and review the fork's work |
| `upstream` | `MrGreenCheezz/albion-local-server` | Read original changes and target upstream proposals |

`main` is the fork's stable branch, not a permanent mirror of the original repository.
`upstream/main` remains the reference for original code. A release of the fork is not an
official release by the original author.

## Branch lifecycle

| Branch | Start from | Merge into | Lifetime |
| --- | --- | --- | --- |
| `main` | Existing stable history | — | Permanent |
| `develop` | Current integrated development baseline | `main` when release-ready | Permanent |
| `feature/<topic>` | `develop` | `develop` | One coherent feature |
| `fix/<topic>` | `develop` | `develop` | One ordinary bug fix |
| `chore/<topic>` or `docs/<topic>` | `develop` | `develop` | One maintenance or documentation change |
| `hotfix/<topic>` | `main` | `main`, then propagate to `develop` | One urgent released-version fix |
| `release/<version>` | `develop` | `main`, then propagate to `develop` | Optional release stabilization |
| `contribution/<topic>` | Current `upstream/main` | Original repository's target branch through a PR | One upstream proposal |

Use lowercase English names, such as `feature/journal-handover` or
`fix/journal-object-state`. A newly discovered issue during feature development is
normally a `fix/`, not a `hotfix/`. Reserve `hotfix/` for an urgent defect in a released
version. `release/` branches accept stabilization and release documentation, not new
features.

Prefer squash merges for small topic PRs when their individual commits add no review
value. Preserve ancestry when promoting `develop` or a `release/` branch into `main`,
and when merging release or hotfix changes back. Do not squash long-lived branch
promotions: it makes later synchronization harder. Do not rewrite published `main`,
`develop`, release tags or contribution history under review without explicit agreement.

## Local repository layout

Use a single working checkout, `albion-local-server`, with `develop` active between
focused tasks. Branches preserve alternative histories without requiring a separate
folder for each branch. Switch branches only with a clean working tree; preserve local
saves and settings in ignored storage. Additional worktrees are optional and should be
created only when explicitly needed.

The local organization completed on 2026-10-23 preserves:

- `main`: the original stable baseline at `bdb2c60`; no local fork release is declared.
- `develop`: the integrated tutorial work, pending laboratory checkpoint and workflow
  documentation.
- `contribution/tutorial-island`: commit `2013a16`, kept unchanged for the existing
  upstream PR. Its separate working folder has been removed.
- `contribution/sqlite-native-security`: commit `ea91393`, preserved from the previous
  `fix/sqlite-native-security` branch. Its separate working folder has been removed.

The former `feature/tutorial-definitions`, `educational-lab` and temporary workflow
branches are removed locally after their histories are integrated into `develop`.
Remote branches are left unchanged. The old `albion-local-server-main` source snapshot
is preserved under ignored `data/lab/local-archives/albion-local-server-main`.
The usual launchers still refer to the single active checkout. No save database is
removed by this organization.

`main` and `develop` do not need remote tracking until the owner publishes them.
The original author's branch remains available as `upstream/main`. Local organization
performs no push, upstream PR update, remote branch deletion or release publication.

When ready to publish, inspect the current remote state and publish permanent branches
to the fork with `git push -u origin main` and `git push -u origin develop`. If a push is
refused, inspect divergence; do not force it. In the fork's GitHub settings, `develop`
can be the default branch for day-to-day PRs. Protect `main` and `develop` against force
pushes and deletion, and require PRs before future integration. Add required status
checks only after those checks actually exist and pass. Real-client tests require
locally available GameData; a solo maintainer need not require an independent approval
they cannot obtain.

Do not promote the tutorial work to `main` or attach a release tag merely to complete
the local branch organization. Its remaining validation items still apply.

## Normal development

Start from a clean, synchronized `develop` and create a topic branch. Make one coherent
change, add meaningful regression coverage, and open a fork PR targeting `develop`.
Before review, inspect both staged changes and the complete PR diff. Describe results
in English; personal explanations and local learning notes may remain in Spanish.

Sync upstream changes in a dedicated integration branch based on `develop`, review
conflicts and run validation before merging to `develop`. Synchronizing upstream does
not automatically promote development changes to `main`.

## Upstream contributions

Start a fresh `contribution/<topic>` from current `upstream/main` in the same checkout,
after committing or safely preserving current work. Select only the needed changes from the fork. If commits mix local notes and
implementation, curate them before creating the contribution commit; do not blindly
merge `develop` into the contribution branch.

Inspect `git diff --name-status upstream/main...HEAD` and then the full diff. New shared
comments, documentation, commits and PR text must be in English. Include implementation,
tests and necessary configuration/dependency files. Exclude personal scripts, Spanish
session notes, local paths, saves and generated client analysis. Preserve existing
upstream files in other languages unless the actual task requires changing them.

Validate the clean contribution checkout, where local-only tooling and references
may be absent. Publish the contribution branch to `origin` and open the PR against the
original author's repository. The owner performs both actions.

The existing tutorial PR is already under review on `contribution/tutorial-island`.
Keep that branch; its history is preserved without a separate folder. This documentation is fork
governance and should not be added to that PR. Once upstream accepts changes, integrate
the actual accepted upstream history through the normal synchronization process rather
than assuming the fork and upstream commit IDs are identical.

## Releases and tags

Use an independent fork version such as `v0.1.0` when the first release is ready. This
is an example, not an assigned release. Do not copy the Albion client version into the
server release version. State the tested client build separately in release notes.

Follow Semantic Versioning conventions. While the project is `0.x`, describe compatibility
changes explicitly; before claiming `1.0.0`, define the supported public contracts
(configuration, persistence/migrations and extension interfaces). Include save compatibility
and known gaps in release notes. Never require deleting real databases to make a release
pass validation.

For a short release, validate `develop` and promote it to `main` through a release PR.
When stabilization must continue alongside new development, create an optional
`release/<version>` branch. After validation and merge, create an annotated tag on the
exact released `main` commit. Pre-release tags may use `v0.1.0-rc.1`. Final tags are
immutable; publish a new version for corrections.

For a hotfix, branch from released `main`, validate the correction, merge to `main`,
tag a patch release and propagate the correction to `develop` and any active release
branch. Avoid duplicate application if the branches already contain the fix.

Release notes record the code version, tested client build, passed/failed/skipped counts,
real-client verification, save compatibility, known limitations and upstream PR links.
The owner creates and pushes tags and publishes releases.

## References

- [GitHub flow](https://docs.github.com/en/get-started/using-github/github-flow)
- [Fork remotes](https://docs.github.com/en/pull-requests/how-tos/work-with-forks/configuring-a-remote-repository-for-a-fork)
- [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html)
