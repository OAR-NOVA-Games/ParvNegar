# Changelog

All notable changes. Versions follow Semantic Versioning once releases begin. No release has been published.

## [Unreleased] — Phase 1 (feat/phase-01)

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
