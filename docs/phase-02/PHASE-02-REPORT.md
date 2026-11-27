# ParvNegar — Phase 2 Engineering Report

Date: 2026-11-27. Phase 2: offline licensing, authentication, authorization and security. Branch `feat/phase-02`, based on `main` = `853dc56` (merged Phase 1). **Stopped at the Phase 2 review gate. Phase 3 not started.**

**Status legend:**
- **Implemented + tested:** automated evidence exists.
- **Statically analysed:** build or static checks only.
- **Proposed:** designed and documented, needs review or approval.
- **NOT RUN:** no machine or evidence yet.

## 1. Executive summary

- **Offline licensing** — *implemented + tested*:
  - ES256-P1363 signed perpetual licenses over canonical JSON;
  - strict parsing that rejects duplicate keys and ambiguity;
  - build-profile trust anchors;
  - one license for Modern and Legacy, x86 and x64;
  - 2-of-3 hardware binding: SMBIOS UUID, system-disk serial, MachineGuid in the 64-bit view.

  All four builds read identical claims, and licenses verify 16/16 across editions and architectures. The verifier uses no clock.
- **Publisher-only issuer:**
  - encrypted signing-key files;
  - issuance, reissue and provisioning;
  - privacy-minimized records;
  - an isolation gate that keeps it, and any key, out of customer outputs.

  **No production key exists**: that needs the owner-approved ceremony.
- **First-run owner provisioning:**
  - a signed file bound to the license and to this PC's request;
  - a commitment to a separately delivered 160-bit bootstrap code;
  - one-time use and crash-safe staged setup.

  There are no default credentials.
- **Authentication:**
  - password (Argon2id 64 MiB), plus the DPAPI device secret, plus PNKE v2 envelopes bound to ID, revision and username, plus database-authoritative account checks;
  - stale, swapped, transplanted, disabled and deleted cases are rejected;
  - persistent progressive throttling, with no permanent lockout.
- **Sessions:**
  - 15-minute monotonic inactivity lock;
  - immediate lock on Windows lock, user switch, suspend and resume;
  - a lock closes the database, wipes keys, clears pages and invalidates queued operations;
  - single instance and a single Argon2id at a time;
  - protected ACL on a newly created data folder.
- **Accounts:** owner and delegated administrators, deny-by-default permissions enforced in services, last-owner protection.
- **Owner recovery key:**
  - shown once, confirmed by the owner, rotated after use;
  - recovery after a Windows reinstall re-binds the device.
- **Audit:** HMAC-chained audit log, bounded at 50,000 events; a pre-unlock journal.
- **UI:**
  - Persian RTL screens for every flow, in Light and Dark themes switched at runtime;
  - the owner-approved school name and logos, the publisher mark, the 2-second splash;
  - 40 screenshots per edition, captured through the real gates.
- **Evidence:**
  - clean locked build ×4;
  - 954 test executions, 0 failed (§13);
  - cross-edition 185/185;
  - Phase 1 data sets 16/16;
  - compatibility kit on host;
  - selective CI with 33 mapping cases.
- **Not proven:**
  - Windows 7 SP1, 8.1 and 10 runtime;
  - real DPI 100/150/200;
  - screen readers;
  - 2 GB PCs;
  - a GitHub CI run (it starts when the PR is opened).

## 2. Phase 1 prerequisite evidence

| Gate (prompt §3) | Evidence |
|---|---|
| SHAs | Before Phase 2: `main` = `origin/main` = `853dc56` (merge of PR #1); Phase 1 head `2b67406` |
| Repository privacy | **Public.** Owner decision 2026-11-27: stays public for now (Actions billing). Recorded as G-VIS |
| Follow-up document | `docs/phase-01/PHASE-01-REVIEW-FOLLOWUP.md`; findings F-1 to F-8 fixed (plaintext LMK in backup tooling, path validation, source mutation, staging cleanup, entry validation, window sizing, screenshot naming, capture privacy) |
| Pre-authentication review | Same document §4 (P-1 to P-10). Every item is implemented or documented in Phase 2 (AUTHENTICATION, SESSION-SECURITY) |
| Regression gates | Re-run in the follow-up and again for Phase 2 |
| Merge with owner approval | Owner approved 2026-11-27; PR #1 merged as a merge commit (no squash, no force) |

## 3. Repository, branch, commit and remote status

- **Branch:** `feat/phase-02` from `853dc56`.
- **Commits:** focused conventional commits, listed with `git log main..feat/phase-02`. The final docs commit closes the phase.
- **Remote:** pushed only with the owner's Git authorization. No push to `main`, no force-push, no merge.
- **Working tree:** clean after the final commit. `artifacts/` and `.local/` are ignored.

## 4. Owner decisions implemented

See DECISION-REGISTER §1. Highlights:
- the perpetual, one-device, tolerant and reissuable license;
- the independent signing key, owner recovery key, account model and password and session policies;
- **Light and Dark themes**, superseding Light-only;
- the official logos, selective CI and offline runtime;
- the school name and logos, and the publisher splash.

No new commercial policy was invented.

## 5. Architecture changes

ARCHITECTURE.md:
- new `ParvNegar.Licensing` (netstandard2.0);
- `ParvNegar.Application` security services;
- the publisher-only `tools/ParvNegar.Issuer` (.NET 10);
- the gated WPF shell.

No new NuGet packages, and no new cryptographic primitives: ECDSA, SHA-256, HMAC, HKDF, Argon2id, AES-GCM and DPAPI only. ADR-P2-01 to ADR-P2-05.

## 6. Offline licensing implementation

LICENSING-ARCHITECTURE.md and LICENSE-FORMAT.md. Tested cases:
- valid; wrong product; invalid signature; tampered claims; cross-domain signature; high-S;
- unknown, revoked and retired keys; environment and prefix mismatches;
- unsupported format, policy and expiry; unknown, missing and duplicate fields; non-canonical payload;
- malformed envelopes; strict base64url;
- edition and architecture limits;
- perpetual behavior under past and future `issued_at`, and no clock parameter.

## 7. Device-binding policy and test evidence

DEVICE-BINDING.md and ADR-P2-02 (*proposed*, for review):
- one changed signal is tolerated: disk, Windows reinstall or mainboard;
- two changed signals mean a reissue;
- placeholder and missing identifiers are unusable, and the threshold is never lowered;
- requests refuse fewer than 2 usable signals;
- raw identifiers are never exported;
- on this host every build reads 3/3 good signals with identical digests.

## 8. Publisher issuer and signing-key separation

SIGNING-KEY-CUSTODY.md and LICENSE-REISSUE-POLICY.md:
- PNSK v1 encrypted key files; production generation is refused without the ceremony flag and always refused in CI;
- every artifact is self-verified after signing;
- records contain no bootstrap codes and no raw hardware data;
- the isolation gate passes, and a negative test shows it detects a planted key;
- the development key is only in ignored `.local/`; test keys are ephemeral.

## 9. Authentication and authorization

AUTHENTICATION-ARCHITECTURE.md, AUTHORIZATION-MATRIX.md and PASSWORD-POLICY.md.
- **Every prompt §25 case is covered**, except the explicitly listed gaps (TEST-EVIDENCE §2).
- **Explicit limits:**
  - service-level authorization is not cryptographic isolation: all accounts share the LMK;
  - a password change does not revoke old envelope copies without LMK rotation;
  - a full-disk rollback by an administrator is not detected.

## 10. Recovery and session security

RECOVERY-POLICY.md and SESSION-SECURITY.md:
- recovery key: generation, display once, confirmation, rotation;
- same-device recovery and re-binding recovery;
- the state machine, inactivity, Windows and power events, lock effects, throttling, journal, single instance and ACL.

## 11. Storage and audit changes

STORAGE-CONTRACT-CHANGES.md and AUDIT-LOG-FORMAT.md:
- schema 2 is a new migration, and migration 1 is unchanged;
- PNKE v2 is a new format, and v1 is unchanged;
- PNBK v1 is unchanged on the wire;
- the audit chain is bounded, with tamper detection tested and its limits stated.

**Phase 1 storage contracts re-validated:**
- encrypted creation fails closed;
- pooling stays off;
- no keys in connection strings or logs;
- restore keeps key entries in memory;
- backups exclude licenses, requests, provisioning and device secrets;
- cross-edition 185/185.

## 12. Light and Dark UI screenshots

`docs/phase-02/screenshots/`: 20 states × Light and Dark, Modern x64; the Legacy x86 set was produced too. Index: UI-UX-REPORT §3. Highlights:
- `01-home-*`, `02-license-required-*`, `03-license-valid-*`;
- `05-setup-*`, `07-recovery-key-*`;
- `09-workspace-*`, `10-accounts-*`, `11-security-audit-*`;
- `14-about-window-*`, `15-lock-inactivity-*`, `17-delegate-must-change-password-*`, `20-owner-recovery-*`.

## 13. Complete build and test results

| Gate | Command | Result |
|---|---|---|
| Clean locked build ×4 | `build.ps1 -LockedRestore` after deleting `bin`/`obj` | BUILD_OK, 0 warnings, 82 s |
| Tests ×4 (final run, after the ACL test) | `test.ps1 -NoBuild` (log `artifacts/logs/final-tests.log`) | **950 executions, 0 failed, 0 skipped**: Domain 27, Security 59, Storage 52, Licensing 51/48, Application 35, App 15 per combination. Then the added schema 1→2 upgrade test: Storage 53/53 ×4. **Total 954, 0 failed** |
| Cross-edition | `tools/Run-CrossEdition.ps1` | 185/185 PASS |
| Phase 1 data sets | `interop-consume` ×16 | 16/16 PASS |
| Database migration | Application, Storage and interop (schema 1 → 2) | PASS |
| Vulnerability audit | `dotnet list … --vulnerable --include-transitive` | 0 of 14 projects |
| Native imports | `check-native-imports.py` | `WINDOWS7_BLOCKERS=0` |
| Hygiene and secrets | `check-repo-hygiene.ps1` | `HYGIENE_OK` |
| License and issuer isolation | `check-issuer-isolation.ps1` | `ISOLATION_OK` (+ negative test) |
| CI trigger mapping | `tools/ci/Test-CiMapping.ps1` | `CI_MAPPING_OK cases=33` |
| Light/Dark UI flow | `tools/Run-UiSmoke.ps1` (Modern x64, Legacy x86) | 20/20 steps, 40 screenshots each |
| Shell smoke ×4 | `ParvNegar.exe --smoke` | PASS ×4, splash ≥ 2 s, layout failures 0 |
| Compatibility kit (host) | `Build-CompatKit.ps1`, then `RUN-COMPAT.cmd` | 24/24 checks, 4/4 interop sets, 4/4 signal checks, 4/4 shell smokes (Legacy smokes re-run after refused captures); zip 40,261,998 bytes, SHA-256 `af93fc15…acab0` |

**Measurements on the host:**
- startup to main window, including the 2 s splash: 3.5–5.0 s;
- one failed login: 0.53 s (Modern x64), 0.82 s (Legacy x86);
- setup: 1.2–1.9 s;
- Argon2id: 0.33–0.95 s;
- peak private memory: 130–374 MB;
- theme switch: immediate (no restart).

Details: BUILD-EVIDENCE.md, TEST-EVIDENCE.md.

## 14. Cross-edition interoperability

CROSS-EDITION-EVIDENCE.md:
- storage and backup in 16 pairs at schema 2;
- the Phase 1 data sets;
- identical binding claims in all builds;
- 16/16 cross-build license verification.

## 15. Platform compatibility matrix

PLATFORM-MATRIX.md:
- **Windows 11:** PASS for all four builds.
- **Windows 7 SP1, 8.1, 10:** **NOT RUN**. This is a release qualification blocker.
- **Windows 8 and Windows 10 1507/1511:** BLOCKED by owner decision.

## 16. CI implementation and execution evidence

CI-STRATEGY.md:
- the workflow, classification and aggregate `ci-result` check are implemented;
- the mapping is tested locally;
- **no GitHub run has happened yet**: it starts on the Phase 2 PR;
- cost: free while the repository is public.

## 17. Privacy and security review

- PRIVACY-CHECKLIST.md and THREAT-MODEL.md.
- **Self-review:**
  - found and fixed synthetic smoke credentials compiled into the app;
  - found and fixed the missing data-folder ACL, plus the defects in TEST-EVIDENCE §5.
- **An independent security review is recommended** before release, especially for:
  - device binding;
  - the provisioning and recovery flows;
  - the trust-anchor process.

## 18. Confirmed defects, fixes and residual risks

- **Defects:** TEST-EVIDENCE §5, 13 items, all fixed.
- **Residual risks:**

| ID | Risk |
|---|---|
| R-1 | No Windows 7, 8.1 or 10 runtime evidence |
| R-2 | Device-binding behavior on real older hardware, VMs and clones |
| R-3 | Revocation limits of offline licensing |
| R-4 | LMK rotation UI not built: former insiders keep a copied LMK |
| R-5 | Small password blocklist |
| R-6 | Administrators and malware can reset throttling and roll back the data folder |
| R-7 | Managed-memory copies of secrets |
| R-8 | Interrupted recovery and re-binding not fault-injection tested |
| R-9 | Public repository |
| R-10 | CI not yet executed on GitHub |
| R-11 | Accessibility and screen readers not tested |

## 19. Outstanding owner decisions

1. **Review of Phase 2:** approve or reject the PR merge of `feat/phase-02`, after the first CI run.
2. **O-KEY-CER and O-PROD-ANCHOR:** schedule the production signing-key ceremony and approve the custody locations (SIGNING-KEY-CUSTODY §4).
3. **O-ACL:** which Windows accounts may use ParvNegar on shared PCs.
4. **O-PWLIST:** approve a larger offline password blocklist source.
5. **O-DARKLOGO (optional):** a product logo for dark surfaces.
6. **Commercial reissue rules** (who and how often) for LICENSE-REISSUE-POLICY.
7. **Kit runs:** run the compatibility kit on Windows 7 SP1, 8.1 and 10; provide a 2 GB test PC.
8. **Phase 3 inputs:** O-07 and O-08.

## 20. Recommended Phase 3 scope

PHASE-03-PLAN.md:
- the school profile in the database;
- academic years and classrooms;
- students, with create/view/edit/soft-delete and Persian search;
- enrollment and promotion, with permissions enforced in services;
- carried-forward security work: LMK rotation, fault injection, real-event checks;
- qualification runs on older Windows.

**Stopped at the Phase 2 review gate. No merge, release, production license or Phase 3 work without separate owner approval.**
