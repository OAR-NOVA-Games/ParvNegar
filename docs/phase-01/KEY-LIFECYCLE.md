# ParvNegar — Key Lifecycle

Date: 2026-11-26. Applies the key hierarchy in SECURITY-ARCHITECTURE.md §2. "Implemented" means the cryptographic operation exists and is tested in Phase 1; account UI, policy and audit are Phase 2/6.

| Event | Operation | Implemented | Limitation stated honestly |
|---|---|---|---|
| First setup | Generate installId, device secret (DPAPI LocalMachine), LMK; create encrypted DB with HKDF(LMK) database key; wrap LMK for the initial owner (kind 1) | Primitives yes; setup flow Phase 2 | Initial owner provisioning artifact and license are Phase 2 |
| Account-specific wrapping | One PNKE kind-1 envelope per account: same LMK, own salt, own Argon2id output, own nonce | Yes (`WrapWithPasswordAndDevice`) | All accounts unlock the same LMK; permissions are enforced by application services (Phase 2), not by separate keys |
| Unlock | Load device secret via DPAPI, Argon2id(password), HKDF → KEK, open envelope → LMK, derive DB key, open DB | Yes | Unlocked LMK is in process memory until session lock |
| Password change | Unlock with old password, create a new envelope with the new password, atomically replace the stored envelope, wipe buffers | Yes (re-wrap tested) | The old envelope still opens if a copy of it survives elsewhere (e.g., an old file copy). Test documents this. |
| Administrator revocation (disable delegate) | Delete the delegate's envelope; deny at the application-service level | Envelope delete: trivial; service-level deny: Phase 2 | **Deleting a wrap does not revoke key material already extracted.** A delegate who copied the LMK while authorized keeps it. Full revocation needs LMK rotation. |
| Session termination / lock | Dispose `LocalMasterKey` (wipe), close all DB connections (pooling disabled, so the native keyed handle closes) | Yes for keys and DB handles; inactivity/Windows-lock triggers Phase 2 | GC copies cannot be guaranteed wiped (SECURITY-ARCHITECTURE §5) |
| LMK rotation | New LMK; `PRAGMA hexrekey` the DB to the new database key; re-wrap attachment DEKs (Phase 5); re-wrap every account envelope; delete old envelopes; take a new backup | DB rekey implemented and tested | Old backups remain decryptable with their own passphrases. Rotation must be followed by a new backup. |
| Recovery key (if approved, O-03) | Owner-held 256-bit key (printed once); PNKE kind 2 envelope on this installation | Format yes | Policy, printing and storage guidance are owner decisions |
| Backup | LMK copied inside the PNBK archive (encrypted under BAK, wrapped by Argon2id(passphrase)) together with the database file and attachment blobs | Yes | Losing the backup passphrase makes the backup unrecoverable (no hidden key) |
| Restore on a replacement device | 1) Valid license for the new device (Phase 2). 2) New installId and a new DPAPI device secret on the new machine. 3) Restore the PNBK archive to staging with the passphrase. 4) Take the LMK from the archive. 5) The owner sets/enters the owner password and the app creates new kind-1 envelopes for the new installId. 6) Atomic switch to live data. | Steps 3–4 implemented and tested cross-edition; others Phase 2/6 | Old device envelopes are useless on the new device by design (installId and device secret differ) |
| Rewrapping on a new installation | Same as steps 2 and 5: every envelope is re-created for the new installId and device secret; delegate accounts need new passwords or an owner-driven re-wrap | Primitives yes | Account behaviour on restore is decision O-06 |
| Device secret loss (Windows reinstall) | Normal unlock impossible; use backup restore or the recovery key path | — | Intended consequence of device binding |

Rules:
- Never write the LMK, the database key or the device secret in plaintext to disk, logs or the clipboard.
- Never place a license or device secret in a backup (enforced by the archive format).
- Never let a license artifact unlock data.
