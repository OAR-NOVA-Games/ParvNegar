# Phase 2 — Build evidence

**Host:** Windows 11 Home x64 10.0.26300, i7-12700H. .NET SDK 10.0.401, .NET 10.0.12 runtimes (x64 and x86), .NET Framework 4.8.1. Date: 2026-11-27.

## Commands and results

| Gate | Command | Result |
|---|---|---|
| Clean locked build ×4 | All `bin`/`obj` deleted, then `build.ps1 -LockedRestore` (log: `artifacts/logs/clean-build.log`) | `BUILD_OK`, exit 0, 0 warnings (warnings are errors), 82 s. Per combination: net10 x64 22 s, net10 x86 20 s, net48 x64 15 s, net48 x86 14 s |
| Issuer | `dotnet build tools/ParvNegar.Issuer -c Release --no-restore` | Succeeded |
| Dependency audit | `dotnet list ParvNegar.sln package --vulnerable --include-transitive` | 14 projects, 0 with vulnerable packages |
| Native imports | `python tools/check-native-imports.py src/ParvNegar.App/bin/Release tools/ParvNegar.DevTool/bin/Release` | `WINDOWS7_BLOCKERS=0`. The only native DLL is `sqlite3mc.dll` |
| Hygiene | `tools/check-repo-hygiene.ps1` | `HYGIENE_OK` |
| Isolation | `tools/check-issuer-isolation.ps1` (after build and kit) | `ISOLATION_OK`. A planted `.pnsk` was detected (negative test) |

**No new packages** were added in Phase 2. New projects (Licensing, Application.Tests, Licensing.Tests, Issuer) use the existing pinned versions and have committed lock files.

## Defect found during the gates

The first full build appeared to fail (exit −1). The cause was the diagnostic command: `Select-Object -First 20` closed the pipeline and stopped the child PowerShell. The script was re-run from a clean tree with its output written to a log, and it passed. The command pattern is recorded here so it is not misread again.

## Artifact hashes (SHA-256, first 16 hex digits)

Release build at commit `c751f11`.

| Combination | ParvNegar.exe | ParvNegar.Licensing.dll | ParvNegar.Application.dll | ParvNegar.Security.dll | ParvNegar.Storage.dll |
|---|---|---|---|---|---|
| net10 x64 | `14020a51e510a606` (ParvNegar.dll `9f140a9303fed14c`) | `001b1f0735435966` | `b83313067a1809d9` | `e49047cbf277e58a` | `36972e806b4ca694` |
| net10 x86 | `658c41f659fe6aae` (ParvNegar.dll `16f9660612dd3377`) | `f9b6ede5fda3ab97` | `e6f54576700987d7` | `972ed8146864a394` | `11b64ad85e813398` |
| net48 x64 | `d3e87991823ec8ce` | `001b1f0735435966` | `b83313067a1809d9` | `e49047cbf277e58a` | `36972e806b4ca694` |
| net48 x86 | `d1f5ce43f3edeecf` | `f9b6ede5fda3ab97` | `e6f54576700987d7` | `972ed8146864a394` | `11b64ad85e813398` |

The netstandard2.0 libraries are byte-identical between Modern and Legacy for the same architecture, as expected from deterministic builds.

**Compatibility kit:** `artifacts/ParvNegar-Phase1-CompatKit.zip` (the name is kept for continuity; the contents are Phase 2), 40,261,998 bytes, SHA-256 `af93fc15626f29b05ccb08ad145c2f86b9da8e4e395df0bd215f852b825acab0`. It is larger than the Phase 1 kit because the app now includes storage, licensing and the SQLite natives.
