# ADR-P1-04 — Key hierarchy, layered unlock, DPAPI LocalMachine

Status: Accepted for Phase 1 formats (2026-11-26); account policy is Phase 2 / O-03.

Decision:
- Random 256-bit LMK, with HKDF subkeys for database, attachments and audit.
- Normal unlock KEK = HKDF(Argon2id(password) ‖ deviceSecret, installId). The device secret is protected by DPAPI LocalMachine with entropy = label ‖ installId.
- DPAPI never wraps the LMK directly, so no DPAPI-only path to data exists.
- Recovery-key envelope format exists; whether and how it is used is O-03.
- Backups carry the LMK inside the passphrase-protected archive and never carry licenses or device secrets.

Why LocalMachine: school PCs are often shared by several Windows accounts. CurrentUser scope would bind data to one Windows profile.

Limitation: any local process can unprotect LocalMachine DPAPI. The password factor is the barrier; the device factor provides installation binding.

Details: SECURITY-ARCHITECTURE.md, KEY-LIFECYCLE.md.
