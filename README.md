# ParvNegar | پرونگار

Proprietary, fully offline Windows desktop application for structured educational and pastoral student records. Owned and published by OAR NOVA Games Studio.

- Product language: Persian (fa-IR), right-to-left. Engineering language: English.
- Requirements: [`docs/requirements/ParvNegar-SRS-v1.0.md`](docs/requirements/ParvNegar-SRS-v1.0.md). Phase reports: [`docs/`](docs/). Brand kit: [`assets/brand-kit/`](assets/brand-kit/).
- Status: pre-release engineering (Phase 2: offline licensing, authentication, authorization, recovery, audit, Light/Dark UI). Not production software. Nothing is released or deployed from this repository without owner approval.

## Editions and targets

| Edition | Runtime | Windows | Architectures |
|---|---|---|---|
| Modern | .NET 10 (`net10.0-windows`) | 10 (1607+), 11 | x64, x86 |
| Legacy | .NET Framework 4.8 (`net48`) | 7 SP1, 8.1 Update (and 10/11) | x64, x86 |

Windows 8 (not 8.1): users upgrade to 8.1 (owner decision D-01). Windows 10 1507/1511: not supported (D-03). Qualification status per OS is in [`docs/phase-02/PLATFORM-MATRIX.md`](docs/phase-02/PLATFORM-MATRIX.md).

## Layout

```
assets/brand-kit            approved, corrected brand kit (icon, logos, banner, identity board)
docs/requirements           SRS v1.0 (owner-approved baseline)
docs/prompts                phase agent prompts
docs/phase-00 .. phase-02   phase reports, ADRs, evidence (phase-02/PHASE-02-REPORT.md is the latest)
assets/publisher            OAR NOVA publisher mark (owner original + pixel-identical PNG)
src/ParvNegar.Domain        netstandard2.0  entities and invariants (no I/O)
src/ParvNegar.Licensing     netstandard2.0  offline license verification (ES256-P1363), formats, device binding
src/ParvNegar.Application   netstandard2.0  ports; Security/: sessions, setup, login, authorization, recovery, audit
src/ParvNegar.Security      netstandard2.0  HKDF, Argon2id, AES-256-GCM chunk streams, key envelopes, DPAPI device secret
src/ParvNegar.Storage       netstandard2.0  encrypted SQLite (SQLite3 Multiple Ciphers), migrations, PNBK backup format
src/ParvNegar.App           net10.0-windows;net48  WPF app (RTL, Light/Dark, license and login gates)
tests/*                     net10.0-windows;net48  xUnit tests, run as x64 and x86
tools/ParvNegar.DevTool     cross-edition interop, licensing signals, isolated compatibility checks (synthetic data)
tools/ParvNegar.Issuer      PUBLISHER ONLY license/provisioning issuer (never shipped to customers)
tools/ci                    CI path classifier and its mapping tests
tools/*.ps1, *.py           build, test, interop, hygiene and native-import checks; compatibility kit
```

## Build and test (Windows, .NET SDK 10.0.401, .NET Framework 4.8 targeting pack)

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -LockedRestore     # 4 combinations, sequential
powershell -ExecutionPolicy Bypass -File test.ps1 -NoBuild            # all tests x 4 combinations
powershell -ExecutionPolicy Bypass -File tools\Run-CrossEdition.ps1   # storage/backup pairs + licensing (needs the Issuer build)
powershell -ExecutionPolicy Bypass -File tools\check-issuer-isolation.ps1 # no issuer/keys in customer outputs
powershell -ExecutionPolicy Bypass -File tools\ci\Test-CiMapping.ps1   # CI trigger mapping
powershell -ExecutionPolicy Bypass -File tools\Run-UiSmoke.ps1        # Light/Dark UI evidence (needs .local dev key, clear desktop)
powershell -ExecutionPolicy Bypass -File tools\check-repo-hygiene.ps1 # privacy gate
python tools\check-native-imports.py src\ParvNegar.App\bin tools\ParvNegar.DevTool\bin
powershell -ExecutionPolicy Bypass -File tools\Build-CompatKit.ps1    # kit for real Windows 7/8.1/10 machines
```

Never commit real school data, archives, databases, backups, keys or secrets. See [SECURITY.md](SECURITY.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

Copyright © 2026 OAR NOVA Games Studio. All rights reserved. See [LICENSE](LICENSE).
