# First-run owner provisioning

Status: implemented (`SessionManager.LoadProvisioning`, `CompleteSetup`) and tested. There is no universal default username or password, and no owner can be created without provisioning.

## 1. Artifacts

- **Provisioning file** (`.pnprov`), signed: binds `license_id`, `request_id`, `owner_username`, and an HMAC commitment to the bootstrap secret (LICENSE-FORMAT §5).
- **Bootstrap code:** 160 random bits as a typed code of 36 characters. It is shown **once** by the issuer and must be delivered by a different channel than the files (for example in person or by phone). It is never stored by the issuer and never written to any file, log or record. The UI flow test keeps it in a synthetic temporary file that the script deletes.

## 2. Binding and replay protection

`LoadProvisioning` accepts the file only when all of these hold:
1. The installation is not initialized, and no `installation` folder exists.
2. The installed license is valid.
3. The signature verifies with a trusted key, and the product is ParvNegar.
4. `license_id` and `request_id` equal those of the installed license.
5. `request_id` equals the **pending activation request created on this PC** (`activation/pending-request.pnreq`).

Consequences (each tested):
- **Replay:** after setup, the same file returns `AlreadyInitialized`.
- **Newer request:** after generating a newer request on the same PC, an older provisioning file returns `StaleArtifact`.
- **Other license or request:** returns `StaleArtifact`.
- **Reinstalled Windows or a deleted pending request:** the old provisioning file no longer matches, so OAR NOVA must issue a new one. This is deliberate: provisioning cannot recreate an owner on someone else's PC.
- **One-time use:** the pending request is deleted when setup completes.

## 3. Setup transaction (crash-safe)

`CompleteSetup(bootstrapCode, newPassword)`:
1. **Throttle:** the `bootstrap` throttle key is checked. A wrong code counts as a failure; a mistyped code (bad check value) does not.
2. **Bootstrap code:** verified against the commitment (HMAC).
3. **Password:** the new password is checked against the policy, with the provisioned username. Setting this password **is** the mandatory change of the initial credential.
4. **KDF gate:** one Argon2id at a time per process.
5. **Setup lock:** an exclusive `setup.lock` file is taken, so a second process cannot initialize at the same time (tested).
6. **Build in staging** (`installation.staging-<random>`):
   - new installation ID;
   - DPAPI LocalMachine device secret;
   - random 256-bit LMK;
   - owner PNKE v2 envelope (revision 1) and recovery-key envelope;
   - encrypted database with migrations 1–2 (`installation_info`, owner account, audit state and the first audit events);
   - the marker `installation.json`, written last.
7. **Publish:** one `Directory.Move(staging, installation)`.
8. **Finish:**
   - delete the pending request;
   - start the owner session;
   - show the recovery key once; the owner must confirm it by re-typing its last group (RECOVERY-POLICY §2).

**Failure at any step:** the staging folder is deleted. No account, key or database exists outside staging until the move. Leftover staging folders from a crash are deleted on the next start (tested). An existing `installation` folder without a marker is reported as `InvalidInstallation`; a new database is never created over it (tested).
