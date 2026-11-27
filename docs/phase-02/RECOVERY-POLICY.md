# Owner recovery policy

Owner-approved: an installation-specific Owner Recovery Key. Source: `SessionManager.Recovery.cs`, `SessionManager.Auth.cs` (`RotateRecoveryKey`, `ConfirmRecoveryKeySaved`); tested in `RecoveryTests`.

## 1. The key

- **What it is:** 256 random bits (`CryptoRandom`). It protects a PNKE v2 recovery envelope of the LMK: HKDF(recovery key, envelope salt). No password and no device secret are needed, so it also works after a Windows reinstall.
- **How it is shown:** as a typed code of 55 characters in 11 groups (LICENSE-FORMAT §6), with input-error detection. Persian digits are accepted.
- **Shown once:** after setup, after every recovery and after every rotation. It is never stored in plaintext, logged, copied to the clipboard by the app, or saved to the installation folder. A test scans every file of the installation for the key bytes and its text.
- **Explained consequences:** if the password and the recovery key are both lost, the data cannot be recovered. OAR NOVA has no master key, universal password or backdoor (the recovery-key screen states this in Persian).

## 2. Confirmation

The owner confirms by re-typing the last group of 5 characters. Until then the state is `RecoveryKeyConfirmationRequired` and every protected operation is refused (tested). If the code is no longer on screen (for example after a restart), the owner must generate a new key; the old code is never shown again.

## 3. Recovery on the same installation (device secret readable)

1. Requires a **valid license**, the owner username, the recovery key and a new password that meets the policy.
2. Throttled with the `recovery` key. A mistyped code (bad check value) is not counted.
3. Unwrap the recovery envelope, then open the database. Then require:
   - the installation ID matches;
   - `recovery_envelope_id` and `recovery_revision` equal the envelope binding, so a stale recovery envelope from before a rotation is rejected (tested);
   - the user is an **active owner**.
4. The owner gets a new envelope (revision + 1) and the old envelope file is deleted, so the old password stops working (tested).
5. **The recovery key is rotated at once:** a new key and envelope; the old one stops working (tested). The new key is shown once.
6. No account is created by recovery.

## 4. Recovery after Windows reinstall or replacement hardware (device re-binding)

- **When:** DPAPI cannot open `device.pnds`. The installation folder was restored or moved, but the Windows installation is new. The state is then `RecoveryRequired`.
- **Requirements:**
  - a **valid license for this PC**: if the hardware changed substantially, a reissue comes first (LICENSE-REISSUE-POLICY);
  - one maximum throttle wait (the old guard key cannot be opened either);
  - the owner recovery as in §3, plus a transactional **re-binding**:
    1. Write a new device secret as `device.pnds.new`, and a new owner envelope bound to it.
    2. In one database transaction:
       - move the owner to the new envelope;
       - mark **every other account** as "no envelope, must change password": their old envelopes are bound to the old device secret and cannot work;
       - increment `device_generation`;
       - write the `installation.device_rebind` and `recovery.owner` audit events.
    3. Replace `device.pnds` with the new secret, and delete the old envelopes.
    4. Rotate the recovery key.
- **Crash after the transaction:** the next sign-in also accepts the pending `device.pnds.new` and promotes it.
- **Afterwards (tested):** delegates keep their permissions, but the owner must give them new temporary passwords. Their old passwords no longer work.

## 5. Rotation

- **Who:** owner only, from Workspace → امنیت → «ساخت کلید بازیابی تازه». Rotation is also forced after every recovery.
- **What happens:** a new key and envelope (revision + 1), the database updated, the old envelope deleted. The old key stops working (tested).

## 6. Publisher assistance

OAR NOVA can **reissue a license**. It can never decrypt school data, reset a password, or recover an account. There is no hidden account, universal recovery password, publisher-wide decryption key or support backdoor (tested: the issuer has no such capability; isolation check).
