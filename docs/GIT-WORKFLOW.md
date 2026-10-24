# Branch and contribution workflow

Keep two permanent branches in the fork:

- `main`: the validated stable baseline. Branch organization does not promote development to stable.
- `develop`: integrated development work, including offline exercises and regression tests.

`origin` points to the fork. `upstream` points to the original project; keep its `main`
and `modifications` references separate. Do not implicitly integrate the optional modifications line.

Use short-lived feature/fix branches when useful. Before deleting one, check that its behavior
is integrated and preserve unique work. Historical backups can retain old branch tips privately.

Start an upstream `contribution/<topic>` from current `upstream/main`. Transfer only the selected
implementation, tests and English operational documentation. Review the complete diff and every
new commit. Do not propose the fork's complete development history as one contribution.

Keep private configuration and actual launch settings ignored. Personal plans and session notes
live outside the repository. Never commit saves, logs, private captures, client data or local launchers.

Validate on the actual contribution branch with isolated build outputs and test databases.
For a broad proposal, run the full available suite. Report passed, failed and skipped separately
and distinguish automated evidence from real-client observations. Do not interrupt an active session.

Publish the reviewed contribution branch to the fork; the owner creates the upstream PR.
Retain it while the PR is open, then remove it after integration and preservation of its changes.
Never rewrite public history or force-push shared branches without explicit agreement.
