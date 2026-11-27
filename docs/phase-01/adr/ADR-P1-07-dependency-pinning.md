# ADR-P1-07 — Exact pins, lock files, sequential builds

Status: Accepted (2026-11-26).

Decision:
- Central package management with exact version ranges (`[x.y.z]`).
- `packages.lock.json` committed for all 10 projects; `RestoreLockedMode` in `build.ps1 -LockedRestore`.
- Both RIDs declared for every project.
- NuGet audit enabled, plus `dotnet list package --vulnerable` per phase.
- `tools/check-native-imports.py` runs on every native DLL change.
- Builds and tests are sequential and memory-conscious.
