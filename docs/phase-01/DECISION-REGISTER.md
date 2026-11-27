# Phase 1 — Decision Register

Date: 2026-11-26. Continues `docs/phase-00/DECISION-REGISTER.md`. Status: **Approved** (owner, with date), **Recorded engineering decision** (agent, reviewable), **Open**.

## 1. Owner approvals recorded for Phase 1 (2026-11-26, in the working session)

| ID | Question | Owner answer | Effect |
|---|---|---|---|
| G-P1 | Start Phase 1 implementation | **Approved** | Phase 1 scope as in the Phase 1 prompt |
| D-01 | Windows 8 (not 8.1) disposition | **Approved: Windows 8 users apply the free in-place upgrade to Windows 8.1 and use the Legacy edition.** No separate Windows 8 build | net461 target removed; row BLOCKED by design |
| D-03 | Windows 10 minimum version | **Approved: Windows 10 1607 minimum; 1507/1511 not supported** | Modern on 10 1607+; Legacy also usable on supported 10 builds |
| G-GIT | First commit, branch, remote | **Approved: initial commit on `main` pushed to the empty private remote, then all Phase 1 work on `feat/phase-01` pushed to the remote; no force-push, no merge to main** | See §3 for what actually happened |
| L-01 / D-06 | QuestPDF eligibility / PDF engine | **Approved: PDF-engine selection deferred beyond Phase 1** | No PDF package in Phase 1; ports only |
| O-10 (part) | Theme | **Approved: Light theme only; Dark prepared structurally, not implemented** | `Themes/Light.xaml` |
| UI-SPLASH | Startup splash | **Requested by the owner 2026-11-27: splash screen with the studio (OAR NOVA) logo for 2 seconds** | `SplashWindow`, PHASE-01-REVIEW-FOLLOWUP §3 |
| N-02 | OAR NOVA publisher mark | **Supplied by the owner 2026-11-26** (`ParvNegar-School-Publisher-Assets.zip`) | `assets/publisher/`; school logos in the same zip wait for N-01 |

## 2. Engineering decisions recorded in Phase 1 (reviewable)

| ID | Decision | Reference |
|---|---|---|
| E-01 | Shared libraries target netstandard2.0; host/tests/tools multi-target net10.0-windows;net48 × win-x64;win-x86 | ADR-P1-01 |
| E-02 | SQLite3MC 2.7.1 ChaCha20-Poly1305, explicit pragmas, provider proof, pooling off | ADR-P1-02 |
| E-03 | Nonce scheme and chunk format (resolves the Phase 0 inconsistency) | ADR-P1-03, STORAGE-FORMAT §2 |
| E-04 | Layered unlock: Argon2id(password) ‖ DPAPI LocalMachine device secret; DPAPI never wraps the LMK | ADR-P1-04 |
| E-05 | Thin ADO.NET instead of Dapper | ADR-P1-02 |
| E-06 | PNBK v1 backup format; licenses/device secrets refused as entries | STORAGE-FORMAT §4 |
| E-07 | Argon2id bounds 19–64 MiB, t 2–10, p 1–4; default 64 MiB/3/1 | STORAGE-FORMAT §5 |
| E-08 | School name in dev config taken from the approved title wording until the owner gives the official name | UI-SHELL-REPORT §5 |

## 3. Git and remote status

| Item | Status |
|---|---|
| Remote `origin` = `git@github.com:OAR-NOVA-Games/ParvNegar.git` | Verified empty with `git ls-remote` before any change |
| Local `main` | One baseline commit (`chore: add requirements baseline, phase 0 documents and brand kit`) |
| Local `feat/phase-01` | Phase 1 commits on top of `main` |
| Push | Done 2026-11-26: the owner pushed `main` and `feat/phase-01` (the agent push had been blocked by the session safety classifier); later feature-branch pushes by the agent with owner-granted permission |
| Merge to main | Not done; needs separate owner approval |
| Repository visibility | **PUBLIC** (verified 2026-11-27 with `gh repo view`), contrary to the "private remote" assumption. Owner decision required (PHASE-01-REVIEW-FOLLOWUP §5) |
| CI | Not enabled (minutes not approved) |

## 4. Open decisions carried forward

| ID | Topic | Needed by | Recommendation |
|---|---|---|---|
| O-03 | Permission model details, initial owner provisioning, owner recovery (recovery key yes/no; publisher-assisted reset rules) | Phase 2 | Owner + delegates; signed provisioning artifact; owner-held recovery key; publisher reset never decrypts data |
| O-04 | License term, hardware binding tolerance, reissue | Phase 2 | Perpetual, one device, publisher-approved reissue |
| O-05 | File types/limits, Office preview, export | Phase 5 | Allowlist PDF/JPEG/PNG/TXT; Office stored only |
| O-06 | Backup passphrase policy, schedule, retention, account handling on restore | Phase 6 | Manual backup; restore re-creates envelopes for the owner, delegates re-enrolled |
| O-07 | Required fields, target-group options, `اولویت ایثارگری` values, subjects per grade, national-ID check-digit enforcement | Phase 3 | — |
| O-08 | Year closure exceptions, copy rules, purge policy | Phase 3 | — |
| O-02 / O-09 | Assessment periods and print layout; reports | Phase 4/6 | — |
| D-06 | PDF engine and Legacy PDF/preview route (QuestPDF eligibility, Windows 7 natives, bidi) | Before Phase 6 | Decide after Windows 7/8.1 kit results |
| N-01 | Official Persian school name text and school logo use | Phase 3 | Owner supplies |
| N-02 | Publisher mark placement in About/footer (mark supplied 2026-11-26; used on the splash) | Phase 2D | — |
| G-VIS | Repository visibility (currently public) | Immediately | Make the repository private |
| N-03 | Windows 10 test machine (22H2 and/or LTSC) | Before release | Owner provides |
