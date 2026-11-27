# ParvNegar — Security Architecture (Phase 1)

Date: 2026-11-26. Updates and supersedes the key hierarchy in `docs/phase-00/THREAT-MODEL.md` §4. The Phase 0 assets, actors and threat table still apply except where this document changes them. Status: implemented for storage, crypto and backup formats. Authentication, licensing and recovery policy are Phase 2 and owner decisions.

## 1. Principles

1. Established primitives only: AES-256-GCM, ChaCha20-Poly1305 (inside SQLite3MC), HKDF-SHA256, Argon2id, ECDSA P-256 / Ed25519 (Phase 2). No new algorithms or constructions.
2. Same guarantees on every edition and architecture: BouncyCastle on both runtimes for shared formats, proven byte-compatible across net48/net10 and x86/x64.
3. Fail closed: wrong key, missing key, unknown provider or plaintext database → refuse. Never create or open an unencrypted database.
4. Least information: one generic authentication error; keys never in connection strings, logs or exception messages.
5. No master passwords, hidden accounts, publisher-wide decryption keys, or license artifacts that unlock data.

## 2. Key hierarchy (normative)

```
                       ┌────────── Local Master Key (LMK): 256 random bits per data set ──────────┐
                       │                     never stored unwrapped                                │
 HKDF-SHA256(LMK, info=label)                                                                      │
   ├─ "ParvNegar/v1/key/database"    → SQLite3MC database key                                      │
   ├─ "ParvNegar/v1/key/attachments" → attachment key-encryption key (Phase 5)                     │
   └─ "ParvNegar/v1/key/audit"       → audit-chain MAC key (Phase 6)                               │
                                                                                                   │
 LMK wraps (PNKE envelopes, one per purpose):                                                      │
   kind 1  per account: KEK = HKDF(Argon2id(password) ‖ deviceSecret, installId) ──────────────────┤ normal unlock
   kind 2  owner recovery key (if O-03 approves): KEK = HKDF(recoveryKey, salt) ───────────────────┤ recovery only
   backup  LMK copied INSIDE an encrypted PNBK archive, protected by Argon2id(backup passphrase) ───┘ portable restore

 deviceSecret: 256 random bits, protected by DPAPI LocalMachine with entropy = label ‖ installId,
               stored in a "PNDS" file under the installation's data directory.
```

### 2.1 Layered unlock (requirement: password AND device)

Normal unlock needs both factors at the same time:

- The **password factor** (Argon2id) proves the user knows the account password.
- The **device factor** (DPAPI-protected device secret) proves the data runs on the Windows installation where it was set up.

The KEK is derived from both values concatenated. Neither factor alone derives the KEK. Tests: `Device_factor_alone_never_unlocks` and `Password_alone_never_unlocks_without_the_device_secret`. A DPAPI-only copy of the LMK **does not exist** in this design: DPAPI protects only the device secret, never the LMK itself.

### 2.2 DPAPI scope decision (ADR-P1-04)

| Option | Effect | Decision |
|---|---|---|
| CurrentUser | Bound to one Windows profile; another Windows user on the same licensed PC cannot unlock even with a valid ParvNegar password | Rejected: schools often share one PC among several Windows accounts |
| **LocalMachine + entropy** | Bound to the Windows installation; any Windows account on the licensed PC can use the app with its ParvNegar password | **Chosen** |

Windows-account assumptions:

- Any process on the machine can call DPAPI LocalMachine Unprotect. The device factor is therefore not a secret from local software. Its value is binding: a copied data directory does not unlock on another Windows installation, even with the right password.
- Offline attackers with the whole disk can, with specialist tools, recover DPAPI machine keys from the same disk. Against a stolen disk, the password factor (Argon2id) is the real barrier. Full-disk encryption (BitLocker) is recommended as an additional control.
- Reinstalling Windows or replacing the PC loses the device secret. Data is then recoverable only by backup restore (§4) or the recovery path (O-03).

### 2.3 What this does not protect against

- Local administrators or malware running while the app is unlocked (memory, screen, keyboard).
- Same-machine software calling DPAPI: it still needs the password factor.
- Weak passwords against offline guessing: Argon2id (64 MiB, t = 3) slows it but does not prevent it.
- Printed or exported documents.
- An administrator who patches the binary to skip license checks (licensing is not DRM).

## 3. Threat model updates (delta to Phase 0)

| Phase 0 item | Update |
|---|---|
| §4 key hierarchy: "DPAPI (CurrentUser or LocalMachine + entropy)" wraps LMK | Replaced. DPAPI never wraps the LMK; it protects a device secret that is one of two KEK inputs (§2.1). LocalMachine scope chosen (§2.2). |
| §4 "Nonces: random 96-bit per file DEK plus chunk counter" | Replaced by STORAGE-FORMAT §2.2: big-endian stream index ‖ chunk counter under a per-set random key; envelopes use random nonces with single-use KEKs. |
| T01 temp files | Large-sort test proves `temp_store=MEMORY` creates no SQLite temp files (control run with FILE does). |
| T04 | KDF parameters bounds-checked before any work (64 MiB cap, x86-safe). |
| T08 copied installation | Copy fails at the device factor even with the right password; license binding is Phase 2. |
| T10 malicious backup | Implemented: header/version/schema/KDF bounds, per-chunk AEAD bound to header hash, path containment, size limits, trailing-data rejection, staging-only restore. |
| T11 backup clones license | Enforced in format: `license*`, `device*`, `secrets*` entry names are refused on write and read. |
| New T22 | Provider substitution: a plain SQLite DLL could silently produce plaintext. Mitigated by proving `sqlite3mc_version()` before keying, header check on open, header check before rename on create. |
| New T23 | Connection pooling keeps keyed handles alive after logout. Mitigated: `Pooling=False`; test proves the file can be moved right after dispose. |

## 4. Recovery paths kept separate

| Path | What it restores | What it never does | Status |
|---|---|---|---|
| Password reset (delegate) | Owner re-wraps the LMK for the delegate with a new password (owner must be unlocked) | Does not reveal the old password; does not touch the license | Phase 2 |
| Owner recovery key (kind 2) | LMK on the same installation when the owner password is lost | Does not work on another PC without the device re-binding steps; not a master key (per installation, owner-held) | Format implemented; policy pending O-03 |
| Backup restore | LMK + data on a new installation, after the backup passphrase | Does not carry or create a license; does not restore device secrets | Format implemented; UI Phase 6 |
| License recovery / reissue | A new signed license for replacement hardware | Never decrypts or unlocks school data | Phase 2, policy O-04 |

## 5. Memory hygiene (practical limits)

Key arrays are wiped with `Array.Clear` after use (`Bytes.Wipe`), and `LocalMasterKey.Dispose` wipes the key. Limits: the .NET garbage collector can copy arrays before they are wiped; strings (passwords from WPF `PasswordBox` are `SecureString`-backed but become managed bytes for Argon2id) and hex strings sent to SQLite live until collected; the native SQLite layer keeps the derived page key while a connection is open. No claim of memory secrecy is made.

## 6. Randomness

`CryptoRandom` uses `RandomNumberGenerator` (BCryptGenRandom on Windows) on both runtimes. Salts, nonces, LMK, BAK and device secrets all come from it. A sanity test checks 2,000 random 96-bit values for duplicates.
