# ParvNegar Phase 0 — Platform Matrix

Date: 2026-11-26. SRS: PLAT-01..04, O-01. Status values: **PASS**, **FAIL**, **BLOCKED**, **NOT RUN**. Vendor support status is separate from app compatibility. A PASS here means the Phase 0 spike passed on that row; it does not mean the product is qualified (AC-14).

## 1. Test environments available

| Environment | Where | Use |
|---|---|---|
| Windows 11 Home x64, 10.0.26300 (26H2 line per registry), i7-12700H, 15.6 GB RAM | Developer workstation (this host) | Modern + Legacy builds, x64 and x86 (WOW64) processes |
| Windows 7 SP1, Windows 8.1 | Owner's other machines (stated by owner 2026-11-26) | Test kit to be run by owner; results not yet returned |
| Windows 10 | Not identified | Needed: 22H2 Home/Pro x64 and x86, plus 1607 LTSB if available |
| Windows 8 (not 8.1) | Not identified | Needed only if D-01 option A |
| VMware Workstation on host | Ubuntu and Fedora VMs only; no Windows images | Could host Windows VMs if the owner provides licensed media |
| Hyper-V | Not available (Windows 11 Home) | — |

## 2. Matrix

Columns: Vendor = Microsoft lifecycle for the OS on 2026-11-26. Runtime = route and prerequisites. Spike = Phase 0 smoke result on that exact row.

| # | OS / build | OS arch | App process | Edition route | Vendor status (OS) | Runtime vendor status | Prerequisites (offline) | Spike status | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Windows 11 (host: 10.0.26300, 26H2 line) | x64 | x64 | Modern (net10) | Supported | .NET 10 supported | none if self-contained | **PASS** (spike) | Primary. Host is a newer build than 24H2/25H2 |
| 2 | Windows 11 | x64 | x86 (WOW64) | Modern (net10) | Supported | .NET 10 x86 supported | none if self-contained | **PASS** (spike) | |
| 3 | Windows 11 | x64 | x64 / x86 | Legacy (net48) | Supported | .NET Framework 4.8.1 in-box | none | **PASS** (spike) | Proves Legacy binaries, not legacy OS |
| 4 | Windows 10 22H2 Home/Pro | x64 | x64, x86 | Modern | **Out of support** since 2025-10-14 (ESU year 1 for commercial ended 2026-10-13) | .NET 10 not listed for Home/Pro | self-contained | NOT RUN | No Windows 10 machine yet |
| 5 | Windows 10 22H2 Home/Pro | x86 | x86 | Modern | Out of support | as above | self-contained | NOT RUN | |
| 6 | Windows 10 Enterprise LTSC 2021 (21H2) | x64/x86 | x64/x86 | Modern | Supported (mainstream to 2027-01-12) | .NET 10 supported | self-contained | NOT RUN | |
| 7 | Windows 10 Enterprise LTSC 2019 (1809) | x64/x86 | x64/x86 | Modern | Supported (extended to 2029-01-09) | .NET 10 supported | self-contained | NOT RUN | |
| 8 | Windows 10 LTSB 2016 (1607) | x64/x86 | x64/x86 | Modern | per LTSB lifecycle (not checked) | .NET 10 lists 1607 (E) | self-contained | NOT RUN | Proposed Windows 10 floor |
| 9 | Windows 10 1507 / 1511 | x64/x86 | x64/x86 | none proven | Out of support | .NET Framework max 4.6.2; .NET 10 not listed | — | **BLOCKED** | D-03; same class of problem as Windows 8 |
| 10 | Windows 8.1 Update (KB2919355) | x64 | x64, x86 | Legacy (net48) | Out of support (2023-01-10) | .NET Framework 4.8 installable; listed as out-of-support OS | .NET Framework 4.8 offline installer | NOT RUN (kit ready) | Static analysis: PDFium FAIL (§4) |
| 11 | Windows 8.1 Update | x86 | x86 | Legacy | as above | as above | as above | NOT RUN (kit ready) | |
| 12 | Windows 8 (not 8.1) | x64/x86 | x64/x86 | Legacy-461 candidate | Out of support (2016-01-12) | Max .NET Framework 4.6.1, retired 2022-04-26 | 4.6.1 offline installer | **BLOCKED** | D-01. net461 spike build only; PDF stack excluded |
| 13 | Windows 7 SP1 | x64 | x64, x86 | Legacy (net48) | Out of support (2020-01-14; ESU ended 2023-01-10) | .NET Framework 4.8 installable | .NET Framework 4.8 offline installer; SHA-2 signing updates expected | NOT RUN (kit ready) | Static analysis: PDFium FAIL, QuestPDF x86 FAIL (§4) |
| 14 | Windows 7 SP1 | x86 | x86 | Legacy | as above | as above | as above | NOT RUN (kit ready) | Low-resource profile row (2 GB, 2 cores, 1024×768) |
| 15 | Windows 7 RTM (no SP1) | any | — | — | — | .NET Framework 4.8 needs SP1 | — | Not a candidate (SRS PLAT-01) | |
| 16 | Windows 11 Arm64 | arm64 | x64/x86 emulated | Modern | Supported | .NET supports x64/x86 emulation on Arm64 | — | NOT RUN | Not in scope (no ARM64 native requirement) |

## 3. Host results (Windows 11 x64)

Rows 1–3 above, Phase 0 spike (details in TEST-EVIDENCE §3):

| Build | Process | Result | Notes |
|---|---|---|---|
| net10.0-windows | x64 | **PASS** 18/18 | Rows 1 |
| net10.0-windows | x86 (WOW64) | **PASS** 18/18 | Row 2 |
| net48 | x64 | **PASS** 17/17 (after test-vector fix; first run 1 FAIL recorded) | Row 3 |
| net48 | x86 (WOW64) | **PASS** 17/17 (same note) | Row 3 |
| net461 | x64 / x86 | **PASS** 14, NOT RUN 3 (PDF excluded) | Ran on the 4.8 runtime; not Windows 8 evidence |

Quality findings that are not shown by PASS counts: Skia+HarfBuzz PDF reverses digits inside RTL text and loses `پ` in text extraction (**FAIL** for Persian PDF correctness). QuestPDF output is visually correct. WPF RTL rendering is correct. Physical printing NOT RUN.

## 4. Static native-binary compatibility (all legacy rows)

A Windows PE file whose `MajorSubsystemVersion` is higher than the running OS is rejected by the loader. Imports of functions that do not exist on the OS fail at load time. Analysis of the packages' `runtimes/win-x86|x64/native/*.dll` (method in TEST-EVIDENCE §4):

| Component | Windows 7 SP1 x64 | Windows 7 SP1 x86 | Windows 8.1 x64/x86 | Windows 10/11 |
|---|---|---|---|---|
| Encrypted DB (`sqlite3mc.dll`) | expected OK | expected OK | expected OK | PASS (host) |
| PDF via SkiaSharp 4.x | expected FAIL (`WaitOnAddress`) | expected FAIL | expected OK | PASS (host) |
| HarfBuzzSharp 14.x | expected OK | expected FAIL (`GetSystemTimePreciseAsFileTime`) | expected OK | PASS (host) |
| QuestPDF 2026.9 | needs UCRT | expected FAIL | expected OK (UCRT needed on x64) | PASS (host) |
| PDFium 160 preview | expected FAIL (subsystem 10.0) | expected FAIL | expected FAIL | PASS (host) |
| WPF RTL, XPS print, WIC images | expected OK (in-box) | expected OK | expected OK | PASS (host) |
| BouncyCastle crypto, DPAPI, ECDSA | expected OK (managed / CNG on Windows 7) | expected OK | expected OK | PASS (host) |

"Expected" = static analysis only; runtime status stays NOT RUN until the kit runs on that machine.

## 5. Low-resource profile

Provisional (SRS PLAT-04): Windows 7 SP1 x86, 2 GB RAM, dual-core CPU, 1024×768. Measures to collect from the kit run: startup time, Argon2id 64 MiB time, private bytes, PDF time. Not a promised minimum.
