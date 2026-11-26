# ParvNegar — Phase 1 Engineering Report

Date: 2026-11-26. Phase: 1 (project foundation, architecture, secure storage). Branch: `feat/phase-01` (local). Review gate: **stop here; Phase 2 not started.**

## 1. Executive summary

- One codebase now builds and runs as Modern (.NET 10) and Legacy (.NET Framework 4.8), x64 and x86. Domain, Security and Storage are shared netstandard2.0 libraries; one XAML shell serves both runtimes.
- Encrypted storage works on all four combinations:
  - SQLite3 Multiple Ciphers with ChaCha20-Poly1305;
  - the provider is proven before any key is sent;
  - no plaintext in the DB, WAL, SHM or journal files, and no SQLite temp files during a large sort (`temp_store=MEMORY`);
  - wrong and missing keys fail; page tampering is detected; a crash image recovers;
  - migrations are transactional and checksummed.
- Security architecture corrected: the normal unlock needs Argon2id(password) **and** the DPAPI LocalMachine device secret. DPAPI never wraps the master key, so no DPAPI-only path to data exists. The nonce scheme is now unambiguous (resolves the Phase 0 inconsistency).
- Cross-edition interoperability proven on this host: 16 producer→consumer pairs (Modern/Legacy × x64/x86) × 10 checks = 164/164 PASS. Covered: database, attachment stream, backup, and the negative cases.
- Tests: 114 tests × 4 combinations = 456 executions, 0 failures. The process architecture is asserted in every run.
- RTL WPF shell with the approved brand assets, embedded Vazirmatn and the exact school title from configuration. Real-window screenshots are in `screenshots/`.
- **Not yet proven on Windows 7, 8.1 or 10**: no such machines on this host. The compatibility kit is rebuilt (22 MB) and ready for the owner's machines.
- **Pushed**: `main` and `feat/phase-01` are on the private remote (owner pushed on 2026-11-26 after the agent push was blocked by the safety classifier). No pull request or merge yet.

## 2. Repository and branch status

| Item | Status |
|---|---|
| Remote | `OAR-NOVA-Games/ParvNegar`, verified empty before work |
| `main` (local) | `dfc3125` baseline: SRS, Phase 0 prompt, sanitized Phase 0 docs, corrected brand kit, `.gitignore`, `.gitattributes`, README, LICENSE |
| `feat/phase-01` (local) | Phase 1 commits on top of `main` (conventional one-line messages, author = owner's Git identity, no agent attribution) |
| Push | Done 2026-11-26 (`main`, `feat/phase-01`) |
| Merge | Not done; needs separate approval |
| CI | Not enabled |
| Uncommitted | none after the final commit; `artifacts/` (test results, interop sets, kit) is ignored |

## 3. Architecture decisions

ADR-P1-01 … ADR-P1-07 (`adr/`). Key points:

- netstandard2.0 shared libraries with multi-targeted host, tests and tools.
- SQLite3MC 2.7.1 with Microsoft.Data.Sqlite.Core 10.0.15 (the last line with netstandard2.0), thin ADO.NET, SQL migrations.
- BouncyCastle for every shared format.
- Layered unlock with DPAPI LocalMachine.
- PDF and preview behind optional ports; engine deferred (owner).
- Exact pins and committed lock files.

Owner decisions recorded: Phase 1 approved, D-01 (Windows 8 → upgrade to 8.1), D-03 (Windows 10 ≥ 1607), Git setup, PDF engine deferred, Light theme only (DECISION-REGISTER.md).

## 4. Implementation summary

| Component | Delivered |
|---|---|
| Repository foundation | README, AGENTS, SECURITY, CONTRIBUTING, CHANGELOG, LICENSE (proprietary), .gitignore (blocks data, archives, DBs, backups, keys), .gitattributes, global.json, Directory.Build.props/.targets, Directory.Packages.props, ParvNegar.sln, build.ps1, test.ps1 |
| Domain | SchoolProfile (exact title), AcademicCalendar/AcademicYear (Mehr–Khordad, states, closure), GradeLevel (1–6, no grade 7), Classroom, Student (stable ID), Enrollment + snapshot + transfer, NationalId (digits, leading zeros, masked), PromotionPlanner (preview, conflicts, idempotent), PersianText search keys |
| Application | Ports for school profile, report renderer, document previewer, with unavailable defaults; ProductIdentity |
| Security | CryptoRandom, HKDF, Argon2id with bounds, AES-256-GCM, ChunkedAead, LocalMasterKey, KeyEnvelope (PNKE kinds 1/2), DeviceSecret + DPAPI LocalMachine |
| Storage | EncryptedDatabase (create/open/rekey/checkpoint), MigrationRunner + schema 1 (STRICT tables, FKs, unique enrollment), BackupArchive (PNBK v1) |
| Shell | Home, Start placeholder and About; RTL; Vazirmatn; brand palette; theme resources; keyboard; DPI manifest; smoke mode with timing, memory and screen capture |
| Tools | DevTool (interop produce/consume, isolated compat checks), Run-CrossEdition.ps1, check-native-imports.py, check-repo-hygiene.ps1, Build-CompatKit.ps1 + RUN-COMPAT.cmd |

## 5. Build and test evidence

| Gate | Result |
|---|---|
| Clean build ×4 (locked restore) | PASS, 0 warnings, 36 s (BUILD-EVIDENCE.md) |
| Tests ×4 | 456/456 PASS (TEST-EVIDENCE.md) |
| Cross-edition matrix | 164/164 PASS |
| Kit on host (isolated checks, interop sets, shell smoke) | 24/24 checks + 4 data sets + 4 smokes PASS |
| Vulnerability scan | No known vulnerable packages |
| Native import check | `WINDOWS7_BLOCKERS=0` (only `sqlite3mc.dll`) |
| Hygiene | HYGIENE_OK |

Failures found and fixed during the phase are listed in TEST-EVIDENCE §6 and BUILD-EVIDENCE. No test was skipped, excluded or weakened.

## 6. Security review (self-review; independent review recommended)

| Topic | Result |
|---|---|
| Plaintext DB creation on encryption failure | Prevented: provider proof, `PRAGMA hexkey` must return `ok`, schema read must succeed, header checked before rename and on every open |
| Keys in strings/logs | Keys only in pragmas and byte arrays; connection strings never contain keys; one generic error for authentication failures |
| Nonce uniqueness | Single-use KEKs for envelopes; per-set random keys with unique (stream, chunk) pairs for streams; tests pin the layout |
| Malformed input | Bounds before work (KDF memory, lengths, counts, sizes); path containment; staging-only restore |
| DPAPI limits | Documented (SECURITY-ARCHITECTURE §2.2); password factor required |
| Revocation and memory | Documented limits (KEY-LIFECYCLE, SECURITY-ARCHITECTURE §5) |
| Residual risks | Local admins/malware in an unlocked session; weak passwords offline; unsupported OS (Windows 7/8.1/10 Home); single-maintainer SQLite3MC; SQLitePCLRaw 3.x under Microsoft.Data.Sqlite 10 proven only by our tests |

## 7. Cross-edition interoperability

All required directions pass (Modern x64↔Legacy x86, Modern x86↔Legacy x64, same edition x86↔x64): database format, encryption, backup envelope, manifest/file integrity, wrong passphrase, tamper, truncation, unsupported format and schema (CROSS-EDITION-EVIDENCE.md). Re-binding on a licensed destination is documented in KEY-LIFECYCLE.md.

## 8. Platform compatibility matrix

Windows 11 x64 (x64 and WOW64 x86, both editions): build, runtime, static and visual PASS. Windows 10, 8.1 and 7 SP1: build PASS, static expectation OK for storage, runtime **NOT RUN**. Windows 8 and Windows 10 1507/1511: BLOCKED by owner decision (PLATFORM-MATRIX.md).

## 9. UI screenshots

`screenshots/home-1024x768.png`, `home-1366x768.png`, `home-800x600.png`, `start-placeholder-1024x768.png`, `about.png`. Real screen captures at 125% DPI with synthetic configuration only. Modern x64 and Legacy x86 output was byte-identical.

## 10. Outstanding risks and blockers

| ID | Item | Type |
|---|---|---|
| R-1 | No runtime evidence on Windows 7 SP1, 8.1, 10 (kit ready; owner machines needed; no Windows 10 machine identified) | Blocker for qualification |
| R-2 | Resolved: branches pushed to the private remote on 2026-11-26 | Closed |
| R-3 | PDF/preview on Legacy: Windows 7 natives, PDFium on 8.1, Persian bidi, glyph extraction, searchable PDF, QuestPDF license, physical A4, temp-file leakage | Open (D-06), outside Phase 1 |
| R-4 | SQLitePCLRaw.core 3.0.2 with Microsoft.Data.Sqlite 10 is a combination proven only by our tests | Risk, gated by tests |
| R-5 | Low-resource performance (Argon2id x86 ≈ 0.7 s on a fast host; slower on 2 GB/dual-core) | Measure |
| R-6 | Official school name and logo, publisher mark not supplied | Owner input |

## 11. Owner decisions required

1. Review the Phase 1 pull request (`feat/phase-01` → `main`) and approve or reject the merge. Approve CI minutes, or keep local gates only.
2. Run the Phase 1 kit on Windows 7 SP1 and 8.1 (x86 and x64 where available). Provide a Windows 10 machine.
3. O-03 and O-04 (permissions, provisioning, recovery, license term, binding, reissue), plus signing-key custody, password policy and lock timeout: entry gates for Phase 2.
4. N-01/N-02: official Persian school name and logo; OAR NOVA publisher mark.
5. Review and accept or adjust ADR-P1-04 (DPAPI LocalMachine and layered unlock).

## 12. Proposed Phase 2 scope

Offline signed licensing (format, device binding, request/response files), a publisher-only issuer and provisioning tool, first-run setup (license → device secret → LMK → encrypted DB → owner account), login/session/lock/throttling, owner-managed delegates and permissions enforced in services, the recovery path per O-03, an audit log for auth, accounts and licensing, Persian setup/login/account screens, and the AC-02/AC-03 tests. Details: PHASE-02-PLAN.md.

**Stopped at the Phase 1 review gate.**
