# Phase 2 — Decision Register

Date: 2026-11-27. Continues `docs/phase-01/DECISION-REGISTER.md`.

Status values:
- **Approved:** an owner decision, with its date.
- **Engineering:** an agent decision; it can be reviewed and changed.
- **Open:** not decided yet.

## 1. Owner decisions recorded in Phase 2

| ID | Decision | Status | Where implemented |
|---|---|---|---|
| G-P1-MERGE | Phase 1 review follow-up accepted; push `feat/phase-01`, open the pull request and merge it into `main` | **Approved 2026-11-27** | PR #1, merge commit `853dc56` |
| G-VIS | Repository visibility: stays **public** for now (GitHub Actions minutes are free for public repositories). The agent may make it private if needed | **Approved 2026-11-27** | Risk noted in PRIVACY-CHECKLIST §5 |
| N-01 | Formal school name «دبستان دخترانه شاهد شهیده شهناز کریمی» and the two supplied school logos may be used in the application | **Approved 2026-11-27** | `src/ParvNegar.App/Config/school-profile.xml`, `Config/school/` |
| UI-SPLASH | Startup splash with the OAR NOVA studio mark for 2 seconds | **Approved 2026-11-27** | `SplashWindow` (Phase 1 follow-up) |
| L-POL-1 | Perpetual offline license | **Approved** (Phase 2 prompt §4) | LICENSE-FORMAT §3 (`policy = perpetual`, `expires_at = null`) |
| L-POL-2 | One licensed device; limited hardware-change tolerance; publisher-approved reissue for substantial changes or a replacement PC | **Approved** | DEVICE-BINDING §3, LICENSE-REISSUE-POLICY |
| L-POL-3 | The same license is valid for the Modern and Legacy editions and for x86 and x64 | **Approved** | `editions`, `architectures` claims; CROSS-EDITION-EVIDENCE §3 |
| L-KEY | Independent product-specific signing key, not the YariZan key | **Approved** | SIGNING-KEY-CUSTODY |
| R-KEY | Owner-held, installation-specific recovery key | **Approved** | RECOVERY-POLICY |
| A-MODEL | Owner and delegated-administrator accounts; explicit permissions; delegates denied by default | **Approved** | AUTHORIZATION-MATRIX |
| P-POL | Passwords of 15–128 characters; Unicode; paste allowed; offline common-password blocklist; no composition rules; mandatory change of the initial password; no forced periodic change | **Approved** | PASSWORD-POLICY |
| T-POL | Persistent progressive throttling; no permanent automatic lockout | **Approved** | SESSION-SECURITY §4 |
| S-POL | Passwords are never remembered; remembering the username is optional; lock after 15 minutes of inactivity, at once when Windows locks, and on suspend, sleep or hibernation | **Approved** | SESSION-SECURITY §2–3 |
| O-10 | **Light and Dark themes** (supersedes the Phase 1 "Light only" decision) | **Approved** | `Themes/Light.xaml`, `Themes/Dark.xaml` |
| BR-1 | Official school and publisher logos displayed in the application | **Approved** | Home, footer, About, splash |
| CI-1 | Selective GitHub Actions CI with controlled minute use | **Approved** | CI-STRATEGY |
| OFF-1 | Fully offline application runtime | **Approved** | No network code in the product |

## 2. Engineering decisions (reviewable)

| ID | Decision | Reference |
|---|---|---|
| E-P2-01 | Signed artifacts use ES256 in IEEE P1363 form, with a separate domain prefix per artifact type. The payload is canonical JSON, carried as base64url; the envelope is canonical JSON. Only low-S signatures are accepted | ADR-P2-01, LICENSE-FORMAT |
| E-P2-02 | Device binding v1: SMBIOS UUID, system-disk serial and MachineGuid, read with plain Win32 calls (no WMI); a match needs 2 of the 3 signals | ADR-P2-02, DEVICE-BINDING |
| E-P2-03 | Owner provisioning is a signed file holding an HMAC commitment to a 160-bit bootstrap code that is delivered separately | ADR-P2-03, PROVISIONING-ARCHITECTURE |
| E-P2-04 | PNKE v2 key envelope: its AAD binds the envelope ID, revision and username hash. The database is the authority for accounts | ADR-P2-04, STORAGE-CONTRACT-CHANGES |
| E-P2-05 | A pre-unlock guard key protected by DPAPI LocalMachine is used for the throttling state and the pre-unlock journal | SESSION-SECURITY §4–5 |
| E-P2-06 | Audit log as an HMAC chain under the LMK-derived audit key, limited to 50,000 events and pruned behind a moving anchor | AUDIT-LOG-FORMAT |
| E-P2-07 | The recovery key is rotated automatically after every recovery; recovery after a Windows reinstall re-binds the device | RECOVERY-POLICY |
| E-P2-08 | Only one Argon2id operation runs at a time per process (memory limit for 32-bit PCs with 2 GB) | SESSION-SECURITY §6 |
| E-P2-09 | Trust anchors are embedded per build profile: Development or Production. Production is empty until the key ceremony | SIGNING-KEY-CUSTODY §5 |
| E-P2-10 | Passwords are normalized with NFC only. There is no Persian/Arabic letter unification, so the student-name search normalization is not used for passwords | PASSWORD-POLICY §2 |
| E-P2-11 | Dark theme header: the brand kit has no product logo for dark surfaces, so the Dark header shows the approved app icon and the product name as text. No logo is recolored | UI-UX-REPORT §4 |
| E-P2-12 | A single CI workflow with path classification. Unknown paths always run the full gate; `ci-result` is the one aggregate required check | CI-STRATEGY |

## 3. Open decisions and owner inputs

| ID | Topic | Needed by | Recommendation |
|---|---|---|---|
| O-KEY-CER | Production signing-key ceremony: date, offline machine, custody location, passphrase holders, backup location | Before the first customer license | SIGNING-KEY-CUSTODY §4. Two sealed backups in separate places |
| O-PROD-ANCHOR | Enrolling the production public key in `Config/trust/Production.json` | After O-KEY-CER | Separate reviewed commit |
| O-ACL | Data-folder ACL policy: which Windows group may use ParvNegar on shared PCs | Before release | Administrators + SYSTEM full control; a dedicated local group "ParvNegar Users" with Modify |
| O-DARKLOGO | A product logo variant for dark surfaces (optional) | Any time | Supply it, or keep the current app icon + text treatment |
| O-PWLIST | A larger offline password blocklist (e.g. a vetted public list, filtered to 15+ characters) | Before release | Owner approves the source licence |
| O-06 | Backup passphrase policy and the restore UI | Phase 6 | Unchanged from Phase 1 |
| O-07, O-08 | Student data rules, year closure | Phase 3 | PHASE-03-PLAN |
| D-06 | PDF engine for Legacy | Before Phase 6 | Unchanged |
| N-03 | Windows 7 SP1, 8.1 and 10 test machines | Before release | Run the compatibility kit |
