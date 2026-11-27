# Key lifecycle (Phase 2 update)

Supersedes the Phase 2 parts of `docs/phase-01/KEY-LIFECYCLE.md`. Phase 1 rules still apply: no new primitives, and no plaintext LMK, database key or device secret on disk, in logs or on the clipboard.

| Key / secret | Created | Stored as | Used for | Rotated / destroyed |
|---|---|---|---|---|
| Publisher signing key (`pn-prod-*`) | Offline ceremony (owner approval pending) | PNSK v1 file (Argon2id + AES-GCM), offline backups | Signing licenses and provisioning | SIGNING-KEY-CUSTODY §5 |
| Development / test signing keys | Developer `keygen` / each test run | `.local/` (ignored) / memory and temp, deleted after the run | Development and tests only | Freely |
| Bootstrap secret (160 bits) | Issuer, per provisioning | Nowhere: shown once; only an HMAC commitment in the signed file | Proves the person setting up is the provisioned owner | Consumed by setup |
| Installation ID | Setup | Marker, DB, KEK salt, AAD | Binds envelopes and device secret to the installation | Kept on re-binding |
| Device secret (256 bits) | Setup / device re-binding | `device.pnds` (DPAPI LocalMachine, installation-bound entropy) | Second KEK input of password envelopes | Replaced on re-binding |
| LMK (256 bits) | Setup | Only inside PNKE v2 envelopes and in memory while unlocked | HKDF → database, attachment and audit keys | LMK rotation (DB rekey exists; UI later); wiped on lock |
| Account KEK | Each unwrap / wrap | Memory only | HKDF(Argon2id(password) ‖ device secret, installId) | Wiped immediately |
| Account envelope (PNKE v2) | Setup, account creation, password change or reset, recovery, re-binding | `accounts/<id>.pnke` | Unwrapping the LMK | Replaced; old file deleted; stale copies rejected |
| Recovery key (256 bits) | Setup, every recovery, every rotation | Nowhere: shown once; the recovery envelope holds the LMK under it | Owner recovery | Rotated after each use or on demand |
| Audit key | HKDF(LMK) | Memory while unlocked | Audit HMAC chain | With the LMK |
| Guard key (256 bits) | First start | `security/guard.key` (DPAPI LocalMachine) | HMAC of the throttle state and the pre-unlock journal | Recreated if unreadable (after a Windows reinstall) |
| Backup archive key (BAK) | Per backup | Wrapped by Argon2id(passphrase) in PNBK v1 | Backup encryption | Per backup |

**Backup and restore (Phase 1 follow-up):** key entries such as `keys/lmk` exist only in memory on both sides. A Phase 6 restore will create new envelopes for the target installation (RECOVERY-POLICY §4 describes the same re-binding steps).
