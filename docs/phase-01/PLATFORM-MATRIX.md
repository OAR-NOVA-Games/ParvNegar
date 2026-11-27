# Phase 1 — Platform Matrix

Date: 2026-11-26. Owner decisions applied: D-01 (Windows 8 users upgrade to 8.1; no Windows 8 build), D-03 (Windows 10 floor 1607; 1507/1511 not supported). Statuses are kept separate. A row is qualified only when all columns pass on that OS (AC-14). Nothing here is qualified yet.

Legend: PASS, FAIL, BLOCKED, NOT RUN, n/a. "Static" = PE import analysis only (expectation, not proof).

| # | OS / build | OS arch | Process | Edition | Build | Runtime (Phase 1 checks) | Static native | Visual | Functional acceptance | Vendor OS status (2026-11-26) |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Windows 11 (host 10.0.26300) | x64 | x64 | Modern | PASS | PASS (114 tests, kit, interop) | PASS (sqlite3mc only) | PASS (screenshots) | NOT RUN (no product features yet) | Supported |
| 2 | Windows 11 | x64 | x86 (WOW64) | Modern | PASS | PASS | PASS | PASS | NOT RUN | Supported |
| 3 | Windows 11 | x64 | x64 | Legacy | PASS | PASS | PASS | PASS | NOT RUN | Supported (.NET Framework 4.8.1) |
| 4 | Windows 11 | x64 | x86 (WOW64) | Legacy | PASS | PASS | PASS | PASS | NOT RUN | Supported |
| 5 | Windows 10 22H2 Home/Pro | x64 | x64, x86 | Modern | PASS | NOT RUN | expected OK | NOT RUN | NOT RUN | Out of support since 2025-10-14 (.NET 10 not listed for Home/Pro) |
| 6 | Windows 10 22H2 | x86 | x86 | Modern / Legacy | PASS | NOT RUN | expected OK | NOT RUN | NOT RUN | Out of support |
| 7 | Windows 10 LTSC 2021 / 2019 / LTSB 2016 (1607) | x64/x86 | both | Modern (Legacy also possible) | PASS | NOT RUN | expected OK | NOT RUN | NOT RUN | LTSC 2021 mainstream to 2027-01-12; LTSC 2019 extended to 2029-01-09 |
| 8 | Windows 10 1507 / 1511 | any | — | none | n/a | BLOCKED (owner decision D-03: not supported) | — | — | — | Out of support |
| 9 | Windows 8.1 Update | x64 | x64, x86 | Legacy | PASS | NOT RUN (kit ready) | expected OK | NOT RUN | NOT RUN | Out of support since 2023-01-10 |
| 10 | Windows 8.1 Update | x86 | x86 | Legacy | PASS | NOT RUN (kit ready) | expected OK | NOT RUN | NOT RUN | Out of support |
| 11 | Windows 8 (not 8.1) | any | — | none | n/a | BLOCKED by design (owner decision D-01: upgrade to 8.1) | — | — | — | Out of support since 2016-01-12 |
| 12 | Windows 7 SP1 | x64 | x64, x86 | Legacy | PASS | NOT RUN (kit ready) | expected OK (sqlite3mc static, subsystem 6.0) | NOT RUN | NOT RUN | Out of support since 2020-01-14 |
| 13 | Windows 7 SP1 | x86 | x86 | Legacy | PASS | NOT RUN (kit ready) | expected OK | NOT RUN | NOT RUN | Out of support |
| 14 | Low-resource profile (Win 7 x86, 2 GB, 2 cores, 1024×768) | x86 | x86 | Legacy | PASS | NOT RUN | — | Layout fits 1024×768 logical (test + screenshot) | NOT RUN | — |

Notes:

- Rows 1–4 prove the binaries and formats; they do not prove older Windows versions. WOW64 is not a 32-bit OS.
- The Phase 1 App loads no PDF or preview native code. Only the storage native DLL matters for Phase 1 rows. Phase 0 static findings for Skia/HarfBuzz/QuestPDF/PDFium remain open under D-06.
- Windows 7 SP1 prerequisite: .NET Framework 4.8 offline installer. Whether SHA-2 code-signing updates (KB4474419/KB4490628) are needed depends on the Phase 7 signing method and will be verified on a real Windows 7 SP1 machine.
- Kit (rebuilt in the 2026-11-27 follow-up; the earlier 22,193,863-byte kit `0a11838d…d932` is superseded): `artifacts\ParvNegar-Phase1-CompatKit.zip` (23,275,653 bytes, SHA-256 `88617c7c72cdc881a134c5cce406962ea2d64d55401dfb3a1526ecad074fc19c`). It records startup time, peak private memory and Argon2id timing, plus an interop data set to bring back.
