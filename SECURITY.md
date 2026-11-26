# Security policy

ParvNegar stores children's personal data. Security issues are handled privately.

## Reporting

Report suspected vulnerabilities privately to the OAR NOVA Games Studio owner (security contact listed on https://oarnova.games). Do not open a public issue and do not include real student data in a report.

## Scope and guarantees (summary)

- Offline only: no network calls at install, activation, login or runtime.
- Database encrypted at rest (SQLite3 Multiple Ciphers, ChaCha20-Poly1305); WAL, journal and temp data stay encrypted or in memory.
- Keys: random Local Master Key, HKDF-separated data keys, unlock requires the user's password AND the DPAPI-protected device secret.
- Backups: Argon2id passphrase envelope, AES-256-GCM chunks, names encrypted; licenses and device secrets are never in backups.
- Not protected against: local administrators, malware in an unlocked session, screenshots, printed output. See `docs/phase-01/SECURITY-ARCHITECTURE.md`.

## Rules for contributors

- No master passwords, hidden accounts, publisher-wide decryption keys or license artifacts that unlock data.
- No private signing keys in the repository or in any installer.
- No new cryptographic algorithms; use the reviewed primitives in `ParvNegar.Security`.
