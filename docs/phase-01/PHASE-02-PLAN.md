# Phase 2 Plan (proposed, not started) — Offline Licensing and Local Account Security

Date: 2026-11-26. SRS §14 item 2: offline licensing, issuer/provisioning tool, local account security and authorization tests. Phase 2 starts only after the owner approves the Phase 1 report and the entry decisions below.

## 1. Entry decisions (owner)

| ID | Decision |
|---|---|
| O-03 | Permission list and defaults; initial owner provisioning; owner recovery (recovery key yes/no, printing; publisher-assisted reset rules) |
| O-04 | License term (perpetual recommended), device binding signals and tolerance, reissue policy, edition field (Modern/Legacy same SKU) |
| — | Signing key custody for the issuer (reuse the YariZan custody process or a separate key) |
| — | Password policy (minimum length; no composition rules recommended) and inactivity lock timeout |
| — | Windows 7 SP1 / 8.1 kit results returned (storage native load confirmed or a fallback decided) |

## 2. Scope (in)

| Task | Description | SRS / AC |
|---|---|---|
| P2-01 | License format: canonical JSON envelope signed with ECDSA P-256 P1363 (YariZan V2 profile) or Ed25519; claims: license ID, product, edition, device binding digest, policy, issued-at; public key embedded; fail closed | LIC-01/02, AC-02 |
| P2-02 | Device binding v1 (e.g. SMBIOS UUID + MachineGuid digest) with a documented tolerance; request/challenge file for offline transfer | LIC-02 |
| P2-03 | Publisher issuer tool (separate project, not in any installer): creates license + initial-owner provisioning artifact; private key outside the repo; test keys generated per test run | LIC-02, AUTH-01 |
| P2-04 | First-run setup: verify license → create installId, device secret, LMK, encrypted DB → owner account (kind-1 envelope) from the provisioning artifact; no default password | AUTH-01, AC-03 |
| P2-05 | Login and session: Argon2id unlock, persistent throttling, lock on inactivity and Windows lock, explicit logout, key wipe | AUTH-04 |
| P2-06 | Accounts and permissions: owner creates/deactivates delegates and resets delegate passwords (owner-driven re-wrap); last active owner protected; authorization enforced in application services | AUTH-02/03, AC-03 |
| P2-07 | Recovery per O-03 (recovery key envelope already exists) | AUTH-04 |
| P2-08 | Audit events for auth, accounts and licensing (minimal content, HMAC chain with the audit key) | NFR-03 |
| P2-09 | Persian login/setup/account screens (RTL, keyboard, DPI) | UI-02 |
| P2-10 | Tests: tampered/missing/copied license fail closed; copied credentials alone insufficient; service-level authorization; throttling; lock; cross-edition license acceptance | AC-02, AC-03 |
| P2-11 | Update kit; run on real Windows 7/8.1/10 if machines are available | PLAT-04 |

## 3. Scope (out)

Student registry and forms (Phase 3/4), attachments (Phase 5), PDF/backup UI (Phase 6), installer (Phase 7), YariZan website integration (Phase 8).

## 4. Risks

| Risk | Mitigation |
|---|---|
| Hardware-binding false negatives after maintenance (disk/board swap) | Tolerant digest, documented reissue path |
| Offline admin can patch out license checks | Stated limitation; data protection does not depend on licensing |
| Owner loses password and recovery key | Recovery policy and backup passphrase guidance shown at setup |
| Argon2id cost on low-end x86 | Measure on Windows 7 x86 hardware; parameters stored per envelope |
| Push/CI blocked | Owner pushes or approves; local gates remain mandatory |
