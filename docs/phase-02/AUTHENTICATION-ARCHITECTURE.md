# Authentication architecture

Status: implemented (`Application/Security/SessionManager*.cs`) and tested (`ParvNegar.Application.Tests`, 35 tests per combination).

## 1. Factors and what they mean

The normal unlock requires all of the following:
1. A **valid application license** for this PC, edition and architecture.
2. The **canonical username** and **password**: Argon2id with m = 64 MiB, t = 3, p = 1.
3. The **device secret** of this installation, readable only through DPAPI LocalMachine on this Windows installation.
4. Successful **authenticated processing of the PNKE v2 envelope**, which yields the LMK.
5. An **active account record** in the encrypted database whose envelope ID and revision equal the envelope's authenticated binding.

DPAPI LocalMachine binds the data to the installation. It is **not** a second human factor: any process of any local user can call it. The password is the human factor. Password-only and device-only unlocks fail (tested).

## 2. Files before unlock

See `InstallationPaths` for the full list.
- `installation/accounts/<envelopeId>.pnke`: PNKE v2 envelopes. File names carry no meaning.
- `installation/recovery/<envelopeId>.pnke`: the recovery envelope.
- `installation/device.pnds`: the DPAPI-protected device secret.
- `security/guard.key`, `throttle.pnth`, `journal.pnj`: SESSION-SECURITY §4–5.

There is **no account index** and no plain list of usernames, roles or permissions before unlock.

## 3. Pre-unlock discovery

1. The login computes `usernameHash = SHA-256("ParvNegar/v1/username\n" ‖ canonical username)`.
2. It reads the **unauthenticated** header of each envelope file (`PeekBinding`) and keeps envelopes whose username hash matches, highest revision first, at most 3.
3. The header is used only to *find* candidates. Every decision is taken after authenticated unwrap and the database check (§4).
4. **Unknown usernames:** with no candidate, one Argon2id runs on dummy input, so an unknown username costs as much time as a wrong password. The result is the same generic failure («اطلاعات ورود درست نیست.»).
   - The `disabled` result is shown only **after** the correct password, which is acceptable.
   - Username enumeration is reduced, not eliminated: file-system access reveals how many envelopes exist.

## 4. Binding checks after unlock (authoritative)

After the LMK opens the database, `OpenAndValidate` requires all of:
- `installation_info.install_id` equals the marker's installation ID;
- the account exists, its username equals the canonical login name, and the username hash equals the binding;
- `account.envelope_id` equals `binding.EnvelopeId`, and `account.envelope_revision` equals `binding.Revision`;
- the status is `active`. A disabled account gets a specific message; a deleted one gets the generic failure.

Any mismatch closes the database, wipes the LMK and writes `security.failure` (`envelope-rejected`) to the audit chain.

| Case | Result (tested) |
|---|---|
| **Stale** envelope (restored after a password change) | Rejected |
| **Swapped** envelopes (file names exchanged) | No effect: identity comes from the binding |
| **Transplanted** envelope from another installation | Rejected (installation ID is part of the KEK and checked before decryption) |
| **Deleted** account with a restored envelope | Rejected |

## 5. What application checks cannot prevent

- **Password change and revocation:** a password change replaces the envelope. It does **not** change the LMK. Someone who copied the old envelope file *and* knows the old password *and* runs code on this Windows installation (DPAPI) can still unwrap the LMK offline. Only LMK rotation revokes that cryptographically; the DB rekey primitive exists, and the UI is planned for later (THREAT-MODEL T-07).
- **Shared LMK:** all accounts share the installation LMK after a valid unlock. Permissions are **service-level authorization**, not cryptographic isolation (AUTHORIZATION-MATRIX §4).
- **Full-disk rollback:** a hostile local administrator can restore the whole data folder to an earlier state. Application-level checks cannot detect a consistent full rollback.

## 6. Password changes

Own password change (current password required, throttled) and delegate reset (owner only) both use `ReplaceEnvelope`:
1. Write the new envelope with a new ID and revision + 1.
2. One database transaction moves the account to it (optimistic check on the old revision) and appends the audit event.
3. Delete the old file.

Crash before step 2: an orphan file remains; the next sign-in removes it (tested). Crash after step 2: the old file is stale and rejected (tested).

## 7. Concurrency

- One Argon2id at a time per process. A concurrent request receives `Busy` (tested).
- One running ParvNegar per PC: a named mutex in `Global\`, falling back to `Local\`. The setup lock file prevents concurrent initialization (tested).
