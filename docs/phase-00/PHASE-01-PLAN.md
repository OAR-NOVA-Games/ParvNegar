# ParvNegar — Phase 1 Plan (proposed, not started)

Date: 2026-11-26. SRS §14 item 1: repository/bootstrap, solution architecture, test/CI baseline, app shell/RTL and branding, secure storage/provider proof of concept. Phase 1 starts only after the owner approves the Phase 0 report and the decisions marked "Phase 1" in DECISION-REGISTER. No timing promise.

## 1. Entry conditions

| ID | Condition | Source |
|---|---|---|
| E1 | Owner approves Phase 0 report | Prompt §6 |
| E2 | D-01 (Windows 8 disposition) decided, or explicitly deferred with Windows 8 kept BLOCKED | DECISION-REGISTER |
| E3 | D-02 multi-target approach accepted | ARCHITECTURE-PROPOSAL ADR-C01 |
| E4 | Windows 7 SP1 and 8.1 Compat smoke results returned from the owner's other machines (see TEST-EVIDENCE §5). Phase 1 may start without them, but ADR-C02/C09/C10 stay provisional until they pass. | PLAT-04 |
| E5 | L-01 QuestPDF eligibility answered, or SkiaSharp path accepted | DECISION-REGISTER |
| E6 | Permission to create the first commit on a feature branch and to push to the private remote | Workspace safety |

## 2. Scope (in)

| Task | Description | SRS / AC mapping | Depends on |
|---|---|---|---|
| P1-01 | Repository bootstrap: `.gitignore` (blocks `*.db`, `*.pnbk`, `*.rar`, `*.docx`, `*.doc`, `*.pptx`, `secrets/`), `.gitattributes`, `Directory.Build.props`, central package management with lock files, `global.json`, README, LICENSE (proprietary) | §15, NFR-02 | E6 |
| P1-02 | Solution skeleton per ARCHITECTURE-PROPOSAL §3, multi-target net10.0-windows;net48; x86/x64 configurations | NFR-01, PLAT-02 | E3 |
| P1-03 | Domain model v0 (SchoolProfile, AcademicYear, Grade, Classroom, Student, Enrollment) with invariants and unit tests (no UI) | §10, AY-03 invariants 1–5 | P1-02 |
| P1-04 | `ParvNegar.Security`: Argon2id, HKDF, AES-GCM chunk stream, key hierarchy skeleton, DPAPI adapter; KAT + tamper tests on both TFMs and both architectures | DOC-03, AUTH-04, ADR-C04/05 | P1-02 |
| P1-05 | `ParvNegar.Storage`: SQLite3MC connection factory (key on open, WAL, temp_store=MEMORY), migration runner with checksums, plaintext canary scan test, wrong-key test | DOC-03, PLAT-03, AC-12 (partial) | P1-04 |
| P1-06 | App shell: WPF host, RTL `FlowDirection`, Vazirmatn, fa-IR resource dictionary, light theme, brand icon/logo from the corrected brand kit, home screen showing the configured school title from settings (not hard-coded), Start/About/Exit, window min/max/restore | UI-01, UI-02, SCH-01, AC-04 | P1-02 |
| P1-07 | About dialog: product `پرونگار`, publisher OAR NOVA, school identity separate | UI-01, AC-04 | P1-06 |
| P1-08 | Compat smoke exe (productized spike) included in test kit; native import checker step | PLAT-04, AC-14 (partial) | P1-04, P1-05 |
| P1-09 | Test/CI baseline: local scripts `build.ps1`, `test.ps1` (net48 + net10, x86 + x64). CI workflow file committed but **disabled** until owner approves minutes | §12 | P1-02 |
| P1-10 | Synthetic data generator (Persian names from a synthetic list, random valid-format national IDs that are not real) | §15 | P1-03 |
| P1-11 | Phase 1 report with evidence | §15 | all |

## 3. Scope (out)

Licensing and issuer tool (Phase 2), accounts/login (Phase 2), forms (Phase 4), vault UI (Phase 5), print/PDF product features (Phase 6), installer (Phase 7). Phase 1 shell shows no student data and has no login, so it must not be distributed.

## 4. Acceptance for Phase 1

1. `build.ps1` builds all four combinations (net48/net10 × x86/x64) with zero warnings-as-errors failures.
2. Unit + security tests pass on all four combinations; results attached.
3. Encrypted DB canary scan finds no plaintext in DB/WAL/journal; wrong key fails.
4. App shell renders RTL home with the exact configured title (string-equality test against SRS §1 bytes).
5. No real data in repo: a pre-commit/CI check scans for `.docx/.doc/.pptx/.rar/.db` and for 10-digit national-ID-like strings in fixtures.
6. Compat smoke runs on Windows 11 x64 (x64 and x86 processes); other rows reported honestly as PASS/FAIL/NOT RUN.

## 5. Risks

| Risk | Effect | Mitigation |
|---|---|---|
| SQLite3MC native DLL fails on Windows 7/8.1 | Legacy edition storage blocked | Build SQLite3MC from source with static CRT and Windows 7 target (D-04 B) |
| Skia/PDFium natives fail on Windows 7 | No PDF/preview on Legacy | WPF XPS print path as fallback; older pinned native builds; owner disposition |
| Microsoft.Data.Sqlite 11+ dropped netstandard2.0 | Legacy stuck on 10.0.x line | 10.0 is LTS-aligned (supported with .NET 10); plan a thin own ADO wrapper over SQLitePCLRaw if needed |
| SQLitePCLRaw.core 2.x vs 3.x mismatch (M.D.Sqlite 10 expects 2.1.x; SQLite3MC 2.7.1 pulls 3.0.2) | Runtime break | Spike tests the resolved graph; pin explicitly; add a test |
| x86 memory with Argon2id 64 MiB on 2 GB PCs | Login slow/failure | Measure on low-resource profile; parameters stored per wrap |
| No Windows 7/8.1/10 VMs on dev host | Late discovery | Owner runs test kit on other machines; get VM images |
| CI minutes blocked in org | No automated gate | Local scripts + owner-approved CI |

## 6. External dependencies

.NET SDK 10.0.x; .NET Framework 4.8 targeting pack; NuGet packages in DEPENDENCY-MATRIX; Vazirmatn font; Inno Setup (Phase 7); Windows 7 SP1 / 8.1 / 10 test machines; code-signing certificate (Phase 7).
