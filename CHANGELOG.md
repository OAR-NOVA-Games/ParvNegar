# Changelog

All notable changes. Versions follow Semantic Versioning once releases begin. No release has been published.

## [Unreleased] — Phase 2 (feat/phase-02)

### Added
- Offline licensing: strict canonical JSON, ES256-P1363 signed licenses and owner provisioning files, activation requests, 2-of-3 hardware binding (SMBIOS UUID, system-disk serial, MachineGuid), build-profile trust anchors.
- Publisher-only issuer tool (`tools/ParvNegar.Issuer`): encrypted signing-key files, issuance, reissue, provisioning, verification, privacy-minimized records.
- First-run owner provisioning with a separately delivered bootstrap code; crash-safe staged setup.
- Authentication: PNKE v2 envelopes bound to envelope id, revision and username; database-authoritative account checks; password policy (15–128, NFC, blocklist); persistent progressive throttling.
- Sessions: state machine, 15-minute inactivity lock, Windows lock/switch/suspend locks, single instance, protected operations re-authorized in the transaction.
- Owner and delegated-administrator accounts with deny-by-default permissions; last-owner protection.
- Owner recovery key with confirmation and rotation; recovery with device re-binding after a Windows reinstall.
- Security audit log (HMAC chain, bounded) and pre-unlock journal; schema 2 migration.
- Persian RTL screens for licensing, setup, recovery key, login/lock, password change, recovery, accounts, permissions, security, settings; Light and Dark themes; school logos and publisher mark.
- Protected ACL for a newly created data folder.
- Selective GitHub Actions CI with tested path mapping; issuer isolation gate; Light/Dark UI evidence flow; licensing checks in the cross-edition matrix and the compatibility kit.

## Phase 1 (merged into main, 853dc56)

### Added
- Solution foundation: central package management with exact versions and lock files, SDK pin, sequential build and test scripts for net10.0-windows/net48 × x64/x86.
- Domain foundation: SchoolProfile, AcademicYear (Mehr–Khordad), GradeLevel, Classroom, Student, Enrollment, promotion planner with idempotency and conflict report, Persian search keys.
- Security library: HKDF-SHA256, bounded Argon2id, AES-256-GCM, chunked AEAD stream format, LMK key envelopes (password+device, recovery key), DPAPI LocalMachine device secret.
- Encrypted storage: SQLite3 Multiple Ciphers (ChaCha20-Poly1305) connection factory with provider verification, WAL, temp_store=MEMORY, foreign keys, transactional checksummed migrations, schema 1.
- Portable backup format PNBK v1 with authenticated header, encrypted manifest and staged restore.
- WPF development shell: Persian RTL, embedded Vazirmatn, approved brand assets, Start/About/Exit, smoke mode with screenshots, timing and memory.
- Tools: cross-edition interop matrix, isolated compatibility checks, compatibility kit, native PE import check, repository hygiene check.

### Not included (later phases)
- Licensing, authentication, student forms, attachments UI, PDF/print engine, document preview, installer.
