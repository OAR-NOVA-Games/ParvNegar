# Contributing

This is a private, proprietary repository. Contributions come from the owner and authorized implementation agents.

1. Branch from `main` (`feat/...`, `fix/...`, `docs/...`). Never push to `main` directly.
2. Keep changes inside the approved phase scope (see the latest `docs/phase-*/` plan).
3. Before committing:
   - `build.ps1 -LockedRestore` succeeds for all four combinations,
   - `test.ps1 -NoBuild` reports `FAILED_RUNS=0`,
   - `tools/check-repo-hygiene.ps1` reports `HYGIENE_OK`,
   - format changes also pass `tools/Run-CrossEdition.ps1`.
4. Dependency changes: edit `Directory.Packages.props` (exact versions), regenerate lock files, run the vulnerability scan and `tools/check-native-imports.py`, and update `DEPENDENCY-LOCK-REPORT.md`.
5. Commit messages are one conventional subject line. Explanations go into the pull request description.
6. Pull requests need owner review. CI is not enabled until the owner approves runner minutes.
