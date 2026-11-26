# ParvNegar Phase 0 — Engineering Discovery Report

Date: 2026-11-26. Inputs: `ParvNegar-SRS-v1.0.md`, `ParvNegar-Phase-00-Agent-Prompt.md`, final corrected brand kit, owner-supplied source archive (read locally only). Author: implementation agent. Business decisions remain with the owner.

## 1. Executive summary

- **One codebase for Modern and Legacy works on the build/runtime level.** A single C# source file, multi-targeted to .NET 10 (`net10.0-windows`) and .NET Framework 4.8 (`net48`), built and ran as x64 and x86 on the Windows 11 host. Every check passed: Persian RTL WPF rendering, the encrypted SQLite database (no plaintext in DB/WAL/journal), crypto known-answer and tamper tests, DPAPI, and an encrypted backup round-trip. The results and limits are in TEST-EVIDENCE.
- **Encrypted storage choice:** SQLite3 Multiple Ciphers (MIT) through `SQLite3MC.PCLRaw.bundle` 2.7.1 with Microsoft.Data.Sqlite.Core **10.0.x**. The free SQLCipher bundle is deprecated. Microsoft.Data.Sqlite 11 dropped .NET Framework support, so the Legacy edition is pinned to the 10.0 line. The native `sqlite3mc.dll` has a static CRT and imports only KERNEL32, so it is expected to load on Windows 7.
- **Main blocker for Windows 7 (and partly 8.1): PDF and preview natives.** Current SkiaSharp, HarfBuzz (x86), QuestPDF (x86) and PDFium binaries import Windows 8+ APIs or declare Windows 10 as minimum. On Windows 7 they are expected to fail. Encrypted storage, crypto and WPF printing are not affected. Decision D-06.
- **Persian PDF quality:** raw Skia + HarfBuzz reversed digits inside RTL text and lost `پ` in text extraction. QuestPDF was visually correct. A bidi step is required for a direct Skia approach.
- **Windows 8 (not 8.1) is BLOCKED**, not dropped: its maximum .NET Framework is 4.6.1, which Microsoft retired in 2022, and current PDF libraries need 4.6.2+. Owner decision D-01 (recommended: the free upgrade to 8.1).
- **Windows 10:** Microsoft supports .NET 10 on Windows 10 only for LTSC/Enterprise (1607, 1809, 21H2). Home/Pro 22H2 is out of OS support. Proposed floor: Windows 10 1607 for Modern; 1507/1511 BLOCKED (D-03).
- **Not yet tested on Windows 7, 8, 8.1 or 10.** A compatibility kit is ready for the owner's other machines. Until those results return, those rows stay NOT RUN.

## 2. Workspace status

| Item | Finding |
|---|---|
| Working directory | `<workspace>\ParvNegar` |
| Git | Branch `main`, **no commits**. Remote `origin` = `git@github.com:OAR-NOVA-Games/ParvNegar.git` (already configured). Not fetched, not pushed. |
| Untracked files | `ParvNegar-SRS-v1.0.md`, `ParvNegar-Phase-00-Agent-Prompt.md`, `ParvNegar-BrandKit-Final-Corrected/` (all owner-supplied), plus the new `docs/phase-00/` |
| Brand kit | App icon (ICO + PNG 16–1254), horizontal/vertical/monochrome logos, banner, identity board, symbol reference. README says the outer icon background is transparent. |
| Source archive | Found at the owner's Downloads folder (Telegram). Read locally in scratch, sanitized, extracted copy deleted. Not in the repo. |
| Tools | .NET SDK 10.0.401; .NET Framework 4.0–4.8 reference assemblies; Visual Studio 18 Community; Git, GitHub CLI, Python 3.14, Node, Docker, WinRAR. |
| Test environments | Windows 11 Home x64 host only. VMware Workstation has Linux VMs only. Hyper-V not available. Owner states Windows 7 and 8.1 are available on other systems. |
| Related repo | YariZan (same studio) uses Inno Setup, ES256-P1363 signed envelopes and device fingerprints. Read for reference only; not modified. |

Spike source, tools, results and the kit are kept outside the repository at `<workspace>\ParvNegar-phase0-scratch\`.

## 3. Recommendation vs alternatives

| Area | Recommended | Alternatives | Why |
|---|---|---|---|
| Runtime | Multi-target `net10.0-windows` + `net48`, one solution | net48 only; two hosts; EF Core | Proven by spike; same domain, XAML, storage and crypto source |
| Storage | SQLite3MC (ChaCha20-Poly1305 page cipher), Microsoft.Data.Sqlite.Core 10.0.x, Dapper, SQL migrations | SQLCipher commercial; SEE; System.Data.SQLite | MIT, works on both TFMs/arches, WAL/journal encrypted in test |
| Crypto | BouncyCastle 2.7.0 (Argon2id, AES-GCM, HKDF, Ed25519) on both editions; platform ECDSA/SHA-2 | Platform-only (no AesGcm/Argon2 on net48) | Byte-identical formats across editions; KATs pass |
| Keys | LMK wrapped per account by Argon2id, plus DPAPI binding, plus an owner recovery key (O-03) | DPAPI only | DPAPI does not isolate same-user processes (THREAT-MODEL §5) |
| License | Offline signed artifact, ECDSA P-256 P1363 (YariZan profile) or Ed25519; issuer tool separate | Online activation | SRS forbids network dependency |
| PDF | Open (D-06). Preferred: one engine with a bidi step, proven on Windows 7 | QuestPDF (license, Windows 7 x86 natives); WPF XPS print fallback | Findings §1 |
| Preview | PDFium in-memory on Modern; Legacy pending D-06; WIC images | Shell open (forbidden) | No plaintext temp files; PDFium 160 needs Windows 10 |
| Installer | Inno Setup 6.x, per edition × architecture, offline prerequisites | WiX (maintenance fee), MSIX (no Windows 7) | Windows 7 minimum in 6.3+; already used by the studio |

## 4. Blockers and risks

| ID | Blocker / risk | Affects | Status | Resolution path |
|---|---|---|---|---|
| B-01 | Windows 8 max .NET Framework 4.6.1 (retired) | Row 12 | BLOCKED | D-01 owner decision |
| B-02 | Windows 10 1507/1511 max .NET Framework 4.6.2, .NET 10 not listed | Row 9 | BLOCKED | D-03 owner decision |
| B-03 | Skia/HarfBuzz/QuestPDF/PDFium natives expected to fail on Windows 7; PDFium on 8.1 | REP-01, DOC-04 on Legacy | Expected FAIL (static) | Kit run, then D-06 |
| B-04 | Persian bidi in direct Skia PDF | REP-01 everywhere | FAIL (host) | Bidi step or QuestPDF; text extraction test in CI |
| B-05 | No Windows 7/8/8.1/10 evidence | PLAT-04, AC-14 | NOT RUN | Owner runs kit; Windows 10 machine needed |
| R-01 | Microsoft.Data.Sqlite capped at 10.0.x for Legacy | Storage | Risk | Serviced with .NET 10 LTS; own thin wrapper later if needed |
| R-02 | Argon2id cost on x86 low-end PCs | Login time | Risk | Measure on Windows 7 x86; parameters stored per wrap |
| R-03 | Legacy OSes receive no security updates | All Legacy users | Accepted by owner's target choice; warn in UI | THREAT-MODEL T21 |
| R-04 | Single-maintainer SQLite3MC | Storage | Risk | Can build from source (MIT); fallback commercial SQLCipher/SEE |
| R-05 | Org GitHub Actions minutes previously blocked | CI | Risk | Local scripts until owner approves CI |

## 5. Evidence

All in TEST-EVIDENCE: environment, commands, exit codes, per-check results for six builds, SHA-256 of binaries, encrypted DB scans, PDF visual/extraction review, static PE analysis, and the list of untested rows. Nothing was marked PASS from compilation alone. One FAIL on the first run (PBKDF2 test vector on .NET Framework) is recorded with its cause.

## 6. Deliverables

| File | Content |
|---|---|
| `docs/phase-00/DISCOVERY-REPORT.md` | This report |
| `docs/phase-00/PLATFORM-MATRIX.md` | Per OS/build/arch status, vendor status, prerequisites |
| `docs/phase-00/DEPENDENCY-MATRIX.md` | Packages, TFMs, licenses, natives, maintenance, references |
| `docs/phase-00/ARCHITECTURE-PROPOSAL.md` | Solution layout, ADR candidates, data/backup contracts, build/installer/CI |
| `docs/phase-00/THREAT-MODEL.md` | Assets, actors, boundaries, threats, key hierarchy, residual risk |
| `docs/phase-00/SOURCE-FIELD-MAP.md` | Sanitized field map, categories, promotion invariants |
| `docs/phase-00/DECISION-REGISTER.md` | D-01..D-06, O-01..O-10, L-01..L-06 |
| `docs/phase-00/TEST-EVIDENCE.md` | Reproducible commands and results |
| `docs/phase-00/PHASE-01-PLAN.md` | Proposed Phase 1 scope |
| `ParvNegar-phase0-scratch/ParvNegar-Phase0-CompatKit.zip` (outside repo) | Kit for Windows 7/8/8.1/10 |

Nothing was committed, pushed, published, deployed or licensed. No production code was written.

## 7. Next approval gate

The owner reviews this report and decides:

1. D-01 Windows 8 disposition; D-03 Windows 10 floor (1607) and 1507/1511 disposition.
2. L-01 QuestPDF eligibility (revenue < USD 1M) or not.
3. Permission for Phase 1 to create the first commit on a feature branch and push to the private remote.
4. Run the kit on the Windows 7 SP1 and 8.1 machines (x86 and x64 where available) and return the `results-<COMPUTERNAME>` folders. Provide or name a Windows 10 machine.
5. Approve the Phase 1 scope in PHASE-01-PLAN (storage, crypto, shell, tests; no licensing, forms or installer).

Phase 1 does not start until the owner approves.
