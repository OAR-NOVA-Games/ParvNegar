# ParvNegar Phase 0 — Threat Model (candidate)

Date: 2026-11-26. SRS: v1.0 (AUTH, LIC, DOC, BKP, NFR-02/03, PLAT-03). Method: asset/actor/boundary listing, then STRIDE per boundary. Status: candidate for owner and security review. No claim of unbreakable security.

> **Phase 1 update (2026-11-26):** the key hierarchy in §4 and the nonce description are superseded by `docs/phase-01/SECURITY-ARCHITECTURE.md` and `docs/phase-01/STORAGE-FORMAT.md`. DPAPI no longer wraps the LMK; normal unlock requires password AND device secret.

## 1. Assets

| ID | Asset | Sensitivity |
|---|---|---|
| A1 | Student identity and profile (name, national ID, photo, guardian names/phones, `اولویت ایثارگری`) | High (children's personal data) |
| A2 | Educational, pastoral, counseling and visit records; talent selections | High |
| A3 | Attachments (report cards, counseling documents, images) | High |
| A4 | Local account credentials and permission grants | High |
| A5 | Data encryption keys (DB key, attachment key-encryption key) | Critical |
| A6 | Device license artifact and install identity | Medium (commercial) |
| A7 | Publisher license-signing private key | Critical (never on customer machines) |
| A8 | Backup archives and their passphrases / recovery material | High |
| A9 | Audit log | Medium (integrity matters) |
| A10 | Printed / exported PDFs | High, outside app control after export |

## 2. Actors

| Actor | Trust | Notes |
|---|---|---|
| Owner (school buyer) | Trusted for school data | Manages accounts, backup, restore, recovery |
| Delegated administrator | Partially trusted | Scoped permissions (AUTH-03) |
| Other Windows user on the same PC | Untrusted for app data | Can read files that ACLs allow |
| Local Windows administrator | Can defeat local controls | Out of absolute protection (NFR-02) |
| Same-Windows-user process (malware, other app) | Untrusted, but same OS identity | Can call DPAPI as the same user |
| Thief of disk, PC or backup media | Untrusted, offline | Has all bytes, no passwords |
| Copier of installation to a second PC | Untrusted (licensing) | Tries to run without a license |
| Author of a hostile attachment | Untrusted | Malformed PDF/image/Office file |
| Publisher license issuer | Trusted for licensing only | Never receives student data |

## 3. Trust boundaries

| ID | Boundary | Crossed by |
|---|---|---|
| B1 | Windows session ↔ ParvNegar process | Login, session lock |
| B2 | Process ↔ data directory (`%ProgramData%\ParvNegar\<install-id>`) | DB, vault blobs, journals, temp |
| B3 | Process ↔ OS key protection (DPAPI) | Wrapping of the local master key |
| B4 | Customer PC ↔ publisher signing station (air gap, file transfer) | License request / license / provisioning artifact |
| B5 | Process ↔ imported file (Windows file picker) | Attachment import |
| B6 | Process ↔ renderer (PDFium, WIC image decoder) | Preview |
| B7 | Process ↔ removable media / user folder | Backup, restore, export, PDF |
| B8 | Old schema/version ↔ new version | Upgrade and migration |
| B9 | Legacy edition (net48) ↔ Modern edition (net10) | Shared backup and storage formats |

## 4. Key hierarchy (proposal; ADR-C04)

```
Owner password / delegate password ──Argon2id──► password-derived KEK (per account)
                                                    │ wraps
Local Master Key (LMK, 256-bit random, per install) ◄┘   also wrapped by:
   │                                                   - DPAPI (CurrentUser or LocalMachine + entropy) for "app installed here" binding
   │                                                   - Owner recovery key (printed once at setup, Argon2id) — O-03
   ├─HKDF "db"──────► SQLite3MC database key (ChaCha20-Poly1305 per page)
   ├─HKDF "vault"───► attachment KEK ──wraps──► per-file DEK (AES-256-GCM, chunked, unique nonces)
   └─HKDF "audit"───► audit-chain MAC key
Backup archive key (BAK, random per backup) ◄─wrapped by─ Argon2id(backup passphrase) — portable, not device-bound
License verification public key (embedded, ECDSA P-256 or Ed25519) — verify only
```

Rules: the LMK is never written unwrapped. Unwrap requires a valid license **and** a successful app login (AUTH-01). Every account stores its own wrapped copy of the LMK, so disabling an account deletes its wrap. DPAPI wrap alone never unlocks data: it binds the key to this Windows profile/machine, while the password wrap enforces app login. Key separation via HKDF labels. Nonces: random 96-bit per file DEK plus chunk counter (no reuse under one key).

## 5. Why DPAPI alone is not enough

DPAPI `CurrentUser` scope encrypts with a key derived from the Windows user's logon secret. Any process running as that same Windows user can call `CryptUnprotectData` and get the plaintext. Optional entropy is not a secret if it lives in the app's binary or data folder. `LocalMachine` scope is weaker: any process on the machine can unprotect. So DPAPI protects against disk theft and other Windows users. It does not protect against malware or another program running as the same user, and it does not enforce ParvNegar login. Therefore the LMK also needs a password-derived wrap (Argon2id). The spike confirmed DPAPI round-trip and wrong-entropy rejection only (TEST-EVIDENCE: DPAPI-CURRENTUSER).

## 6. Threats and mitigations

| ID | Boundary | STRIDE | Threat | Mitigation (proposed) | Residual risk |
|---|---|---|---|---|---|
| T01 | B2 | I | Stolen disk/PC: attacker reads DB, WAL, journal, temp files | SQLite3MC encryption (page-level AEAD) incl. WAL/journal; `temp_store=MEMORY`; vault blobs AES-GCM; opaque names; no plaintext sidecars. Spike scans DB/WAL/journal for canaries. | Memory dump of a running, unlocked session. Offline password guessing against Argon2id wraps; mitigated by password policy. |
| T02 | B1 | S/E | Other Windows user starts ParvNegar | App login required; data dir ACL limited to the installing users group + SYSTEM + Administrators; LMK needs password wrap | Local admin can copy files; still needs a password |
| T03 | B1 | E | Same-user malware reads keys via DPAPI | DPAPI wrap is not sufficient alone (see §5); LMK in memory only while unlocked; session lock on inactivity and Windows lock | Malware in an unlocked session can read screen/memory. Not preventable by the app. |
| T04 | B1 | S | Password brute force in app | Argon2id (m≥64 MiB, t=3 on x64; tuned for 2 GB x86 profile), persistent throttling stored in encrypted DB + DPAPI-protected counter | Offline guessing after disk copy limited only by KDF cost |
| T05 | B1 | E | Hidden bypass / default password | None exist: unique initial owner per sale via signed provisioning artifact; no master password; code review checklist | — |
| T06 | B4 | S/T | Forged or modified license | Asymmetric signature (ECDSA P-256 P1363 or Ed25519), embedded public key, canonical JSON, domain separation; fail closed | Patched binary skips the check (offline admin can tamper). Not DRM-proof. |
| T07 | B4 | I | Signing key leak | Private key only on publisher signing station (YariZan custody model); never in repo or installer; scratch keys in spikes only | Insider at publisher |
| T08 | B2 | S | Copied installation / copied license to new PC | License bound to device fingerprint; LMK DPAPI wrap fails on another machine/profile; copied credentials alone insufficient (AC-02) | Fingerprint spoofing by a skilled admin |
| T09 | B7 | I | Stolen backup | Backup encrypted with random BAK wrapped by Argon2id(passphrase); names and manifest encrypted; spike proves wrong-passphrase/bit-flip/truncation rejection | Weak passphrase. Lost passphrase = lost backup (must be stated in UI) |
| T10 | B7 | T | Malicious or corrupt backup on restore | AEAD per chunk with header-hash AAD, final-chunk flag, size and name validation, staging dir, atomic switch, pre-restore backup | Disk full during switch → rollback path required (Phase 6 test) |
| T11 | B7 | E | Backup used to clone license | Backup excludes license and install identity; restore needs a valid license on the target | — |
| T12 | B5 | T/D | Hostile attachment: traversal, huge file, zip bomb | Copy into vault via stream; validate magic bytes vs allowlist; size cap; no user-supplied names as paths; disk-space precheck; staged commit journal | Unknown parser bugs |
| T13 | B6 | E | Renderer exploit (PDF/image) | PDFium from memory, no JavaScript/forms; WIC for images; no shell execute; no Office automation; optional future: render in low-integrity child process | PDFium/WIC zero-days; keep libraries updated |
| T14 | B6 | I | Plaintext temp files during preview | Render from memory buffers (spike: PDFium from memory, no temp file); no Explorer access to decrypted files | Windows pagefile/hibernation file may hold memory pages; full-disk encryption recommended |
| T15 | B7 | I | Export/print leaks | Explicit export permission; audit success without PII; warning dialog | Recipient handling outside control |
| T16 | B8 | T/D | Failed migration corrupts data | Backup before migration; migrations in transaction; schema version table; recovery path | Native crash mid-transaction → SQLite rollback |
| T17 | B2 | D | Crash during attachment import | Write blob to `.partial`, fsync, then DB row in transaction, then rename; startup sweeper | — |
| T18 | B9 | T | Legacy/Modern format drift | One storage library source (multi-target), shared format version constants, cross-edition round-trip tests | Untested combos until Windows 7/8.1 runs |
| T19 | B1 | R | Repudiation of changes | Audit events (actor, time, action, object ID, outcome) with HMAC chain | Admin can delete the whole DB; tamper-evident, not tamper-proof |
| T20 | all | I | PII in logs/crash dumps | Structured logging with allowlisted fields; no national ID/phone/document content; disable WER full dumps for the app | Third-party library messages; review |
| T21 | all | I | Unsupported OS (Windows 7/8.1/10 Home) | Show an explicit warning; document that app security cannot fix OS vulnerabilities | High on unpatched legacy OS; owner accepted target |

## 7. Per-target differences

| Target | Differences | Same guarantees? |
|---|---|---|
| Modern (net10, Win10 1607+/11) | Platform AES-GCM (CNG) available; BouncyCastle still used for shared formats | Yes |
| Legacy (net48, Win7 SP1/8.1) | No platform AesGcm on .NET Framework; use BouncyCastle (same algorithm, same format). Native libs must load without the latest VC++ runtime. OS out of support. | Yes for crypto/storage, if natives pass; OS risk higher |
| Windows 8 (net461, if approved) | Retired runtime (no security fixes since 2022); no Skia/PDFium in spike | Crypto/storage can match; PDF/preview cannot (BLOCKED, D-01) |
| x86 process | 2 GB address space; Argon2id memory must fit; large PDFs stream | Same algorithms; KDF parameters stored per wrap, so a backup made on x64 restores on x86 if memory parameter ≤ x86 limit (rule: cap m at 64 MiB) |

## 8. Out of scope / accepted limits

Local administrators, kernel-level malware, cameras/screenshots, physical access to an unlocked session, printed paper, and a determined offline attacker patching the license check. These are documented, not mitigated.
