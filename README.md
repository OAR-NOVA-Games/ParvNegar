# ParvNegar | پرونگار

Proprietary, fully offline Windows desktop application for structured educational and pastoral student records. Owned and published by OAR NOVA Games Studio.

- Product language: Persian (fa-IR), right-to-left. Engineering language: English.
- Requirements: [`docs/requirements/ParvNegar-SRS-v1.0.md`](docs/requirements/ParvNegar-SRS-v1.0.md). Phase reports: [`docs/`](docs/). Brand kit: [`assets/brand-kit/`](assets/brand-kit/).
- Status: pre-release engineering (Phase 1). Not production software. Nothing is released or deployed from this repository without owner approval.

## Editions and targets

| Edition | Runtime | Windows | Architectures |
|---|---|---|---|
| Modern | .NET 10 (`net10.0-windows`) | 10 (1607+), 11 | x64, x86 |
| Legacy | .NET Framework 4.8 (`net48`) | 7 SP1, 8.1 Update (and 10/11) | x64, x86 |

Windows 8 (not 8.1): users upgrade to 8.1 (owner decision D-01). Windows 10 1507/1511: not supported (D-03). Qualification status per OS is in [`docs/phase-01/PLATFORM-MATRIX.md`](docs/phase-01/PLATFORM-MATRIX.md).

## Layout

```
assets/brand-kit            approved, corrected brand kit (icon, logos, banner, identity board)
docs/requirements           SRS v1.0 (owner-approved baseline)
docs/prompts                phase agent prompts
docs/phase-00, phase-01     phase reports, ADRs, evidence
src/ParvNegar.Domain        netstandard2.0  entities and invariants (no I/O)
src/ParvNegar.Application   netstandard2.0  ports (school profile, report renderer, document preview)
src/ParvNegar.Security      netstandard2.0  HKDF, Argon2id, AES-256-GCM chunk streams, key envelopes, DPAPI device secret
src/ParvNegar.Storage       netstandard2.0  encrypted SQLite (SQLite3 Multiple Ciphers), migrations, PNBK backup format
src/ParvNegar.App           net10.0-windows;net48  WPF shell (RTL, Vazirmatn, brand assets)
tests/*                     net10.0-windows;net48  xUnit tests, run as x64 and x86
tools/ParvNegar.DevTool     cross-edition interop and isolated compatibility checks (synthetic data)
tools/*.ps1, *.py           build, test, interop, hygiene and native-import checks; compatibility kit
```

## Build and test (Windows, .NET SDK 10.0.401, .NET Framework 4.8 targeting pack)

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -LockedRestore     # 4 combinations, sequential
powershell -ExecutionPolicy Bypass -File test.ps1 -NoBuild            # all tests x 4 combinations
powershell -ExecutionPolicy Bypass -File tools\Run-CrossEdition.ps1   # 16 producer/consumer pairs
powershell -ExecutionPolicy Bypass -File tools\check-repo-hygiene.ps1 # privacy gate
python tools\check-native-imports.py src\ParvNegar.App\bin tools\ParvNegar.DevTool\bin
powershell -ExecutionPolicy Bypass -File tools\Build-CompatKit.ps1    # kit for real Windows 7/8.1/10 machines
```

Never commit real school data, archives, databases, backups, keys or secrets. See [SECURITY.md](SECURITY.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

Copyright © 2026 OAR NOVA Games Studio. All rights reserved. See [LICENSE](LICENSE).
