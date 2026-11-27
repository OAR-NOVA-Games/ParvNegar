# ParvNegar — Storage and Envelope Formats (v1)

Date: 2026-11-26. Status: implemented and tested in Phase 1. Source of truth: `src/ParvNegar.Security` and `src/ParvNegar.Storage`. All integers are **big-endian** unless stated. All formats are identical on net48 and net10, x86 and x64; this is proven by `tools/Run-CrossEdition.ps1` (CROSS-EDITION-EVIDENCE.md).

This document supersedes the Phase 0 nonce descriptions. Phase 0 described the chunk nonce in two ways: "random 96-bit per file DEK plus chunk counter" (THREAT-MODEL) and "file index ‖ chunk counter", little-endian (spike). The normative encoding is §2.2 below: big-endian stream index ‖ big-endian chunk counter, under a key that is random per stream set.

## 1. Encrypted database (SQLite3 Multiple Ciphers)

| Item | Value |
|---|---|
| Provider | SQLite3 Multiple Ciphers 2.7.1 (SQLite 3.54.1) via `SQLite3MC.PCLRaw.bundle` 2.7.1; native `sqlite3mc.dll` (static CRT, KERNEL32 only) |
| Provider proof | `SELECT sqlite3mc_version()` on an in-memory connection must return `SQLite3 Multiple Ciphers …` before any key is sent; otherwise `StorageSecurityException` |
| Cipher | `PRAGMA cipher='chacha20'` (sqleet: ChaCha20-Poly1305, per-page authentication) — set and read back explicitly |
| KDF inside cipher | `PRAGMA kdf_iter=64007`, `PRAGMA legacy=0` — set and read back explicitly |
| Key | 256-bit `DatabaseKey` = HKDF(LMK, label `ParvNegar/v1/key/database`), sent as `PRAGMA hexkey='<64 hex>'`; the pragma must return `ok` |
| Key validation | `SELECT count(*) FROM sqlite_master` right after keying; failure → `DatabaseKeyException` (wrong key never reaches later code) |
| Session pragmas | `journal_mode=WAL`, `synchronous=FULL`, `temp_store=MEMORY`, `foreign_keys=ON`, `secure_delete=ON` (journal_mode, temp_store and foreign_keys verified by read-back) |
| Connection string | `Data Source=…;Mode=…;Pooling=False;Cache=Private` — never contains a key; pooling off so closing releases the keyed handle |
| Create | Written to `<path>.creating`, schema marker table `parvnegar_format(format=1)`, closed, header verified ≠ `SQLite format 3\0`, then renamed. Existing file → refused |
| Open | File must exist, be ≥ 512 bytes and not start with the plain SQLite header (plaintext DB is refused) |
| Rekey | `PRAGMA hexrekey` in DELETE journal mode, then back to WAL (LMK rotation) |
| Schema version | `schema_migrations(version, name, sha256, applied_utc)` + `PRAGMA user_version`; current schema = 1 |
| Migration checksum | SHA-256 of the UTF-8 SQL text with CRLF normalized to LF |
| Dates | `yyyy-MM-dd` (Gregorian calendar date); timestamps ISO-8601 UTC; GUIDs as lowercase text |

Known plaintext: the first page is encrypted, but SQLite3MC leaves header bytes 16–23 unencrypted (page size and reserve bytes; vendor documentation). No user data is in that range.

## 2. Chunked AEAD stream (attachments and backups)

### 2.1 Parameters

| Item | Value |
|---|---|
| Algorithm | AES-256-GCM (BouncyCastle `GcmBlockCipher`), 96-bit nonce, 128-bit tag |
| Chunk size | 65,536 plaintext bytes; every non-final chunk is exactly full; the final chunk is 0..65,536 bytes; an empty stream is one empty final chunk |
| Key | 256-bit, random per stream set (per backup archive; per attachment in Phase 5) |
| Stream index | `u32`, 0..1,048,575 (2^20 − 1); unique within a key |
| Chunk index | `u64` counter from 0; maximum 2^31 chunks per stream (128 TiB) |

### 2.2 Encoding

```
record = flags:u8 | ctLen:u32 | ciphertext||tag[ctLen]
flags  = 0x01 for the final chunk, 0x00 otherwise; all other bits must be zero
nonce  = streamIndex:u32 | chunkIndex:u64                                   (12 bytes)
aad    = headerHash[32] | streamIndex:u32 | chunkIndex:u64 | flags:u8       (45 bytes)
```

Uniqueness: for one key, every (streamIndex, chunkIndex) pair is used once, so every nonce is used once. The key is fresh random per stream set; nonces are never reused across keys either way.

### 2.3 Reader rules (all tested)

- `ctLen` must be 16..65,552; non-final records must be exactly 65,552; unknown flag bits → `MalformedDataException`.
- Cumulative plaintext must not exceed the caller's limit.
- End of input before a final record → truncation (`MalformedDataException`).
- Reordered, duplicated, cross-stream or header-substituted chunks → `AuthenticationFailedException` (nonce/AAD mismatch).
- The caller checks for trailing bytes after the final record.

## 3. LMK key envelope (`PNKE` v1, 126 bytes)

```
off len field
  0   4 "PNKE"
  4   1 version = 1
  5   1 kind: 1 = PasswordAndDevice, 2 = RecoveryKey
  6   4 memoryKiB    (kind 2: 0)
 10   4 iterations   (kind 2: 0)
 14   4 parallelism  (kind 2: 0)
 18  16 salt (random per envelope)
 34  16 accountId   (.NET Guid.ToByteArray layout; kind 2: all zero)
 50  16 installId   (.NET Guid.ToByteArray layout)
 66  12 nonce (random)
 78  48 AES-256-GCM(LMK 32 bytes) || tag
AAD = bytes 0..65
```

| Kind | KEK derivation |
|---|---|
| 1 PasswordAndDevice | `KEK = HKDF-SHA256(ikm = Argon2id(passwordUTF8, salt, m, t, p, 32) ‖ deviceSecret[32], salt = installId bytes, info = "ParvNegar/v1/kek/password+device")` |
| 2 RecoveryKey | `KEK = HKDF-SHA256(ikm = recoveryKey[32], salt = envelope salt, info = "ParvNegar/v1/kek/recovery")` |

Reader rules: length exactly 126, magic, version 1, known kind, installId must equal the expected installation, Argon2id parameters within bounds (§5) before any KDF work, recovery envelopes must carry zero KDF fields. Any decryption failure is one generic `AuthenticationFailedException`.

## 4. Backup archive (`PNBK` v1)

### 4.1 Header (113 bytes)

```
off len field
  0   4 "PNBK"
  4   2 formatVersion = 1
  6   2 minReaderVersion = 1      (reader refuses if > its own version)
  8   4 schemaVersion             (database schema inside the backup)
 12   8 createdUtc (Unix seconds)
 20   1 kdfId = 1 (Argon2id v1.3)
 21   4 memoryKiB
 25   4 iterations
 29   4 parallelism
 33  16 salt
 49  12 wrapNonce
 61  48 AES-256-GCM(BAK 32 bytes) || tag     AAD = bytes 0..48
109   4 entryCount (files, excluding the manifest)
headerHash = SHA-256(all 113 bytes) — bound into every chunk's AAD
```

`BackupKEK = HKDF-SHA256(Argon2id(passphraseUTF8, salt, m, t, p, 32), salt = none, info = "ParvNegar/v1/kek/backup")`. BAK (archive key) is random per backup.

### 4.2 Body

Stream 0 is the manifest, streams 1..N are the files in manifest order, each encoded per §2 under BAK. Manifest (plaintext inside stream 0):

```
"PNMF" | version:u8 = 1 | count:u32 | count × ( nameLen:u16 | name UTF-8 | size:u64 | sha256[32] )
```

Entry names use `[a-z0-9._-/]`, ≤ 200 characters, no leading or trailing `/`, no `//`, no `.` or `..` segments, no segment ending in `.`, and no Windows device name as a segment stem (`con`, `prn`, `aux`, `nul`, `conin$`, `conout$`, `com0`–`com9`, `lpt0`–`lpt9`, with any extension). Names starting with `license`, `device` or `secrets`, or ending in `.pnlic`, `.pnreq`, `.pnds` or `.pnprov`, are refused on write and on read: **device licenses, activation files and device secrets are never backed up**. A name set must not contain duplicates or a name that is also used as a folder (`a` together with `a/b`); readers reject such a manifest as malformed. Names are decoded as strict UTF-8.

**Key entries** (`keys/...`, at most 4,096 bytes, follow-up 2026-11-27): on the wire they are ordinary encrypted entries, so the PNBK v1 format is unchanged and Phase 1 archives stay readable. The writer accepts them only from memory (`BackupEntrySource.FromKeyMaterial`), never from a file. The reader never writes them to staging: `RestoreToStaging` returns them in memory (`BackupInfo.GetKeyEntry`), and `BackupInfo.Dispose` wipes them.

### 4.3 Write and restore rules

- Write:
  - The target folder must exist. An existing archive (file or folder) is never overwritten, and the archive cannot be one of its own sources.
  - Every source is opened with `FileShare.Read` before hashing and stays open until the end, so no other process can write to it. A source that another process holds open for writing, such as a live database, is refused.
  - Each entry is hashed again while it is encrypted. A size or SHA-256 difference from the manifest aborts the backup.
  - Output goes to a unique `<archive>.<random>.partial` file, which is flushed to disk and then renamed. A pre-existing `<archive>.partial` belonging to someone else is never deleted. On failure only our own partial file is removed.
- Restore:
  - `RestoreToStaging` requires a non-existent staging directory whose parent exists, cannot be a volume root, and requires an existing archive.
  - It checks the header, version, schema limit (schema ≥ 1), entry count and KDF bounds, then unwraps BAK (wrong passphrase → `AuthenticationFailedException`).
  - It decrypts the manifest, then every entry, with size limits (default 2 GiB per entry, 64 GiB total), path containment, SHA-256 per entry and no trailing data.
  - Any failure wipes in-memory key entries and deletes the staging directory. If that deletion fails after 3 attempts, `BackupStagingCleanupException` (which carries the path and the original error) is thrown instead of failing silently.
  - Live data is never touched; the atomic switch to live data is a Phase 6 operation.

## 5. Bounds and limits

| Parameter | Bound |
|---|---|
| Argon2id memory | 19,456–65,536 KiB (64 MiB cap so a 32-bit process on a 2 GB PC can open every envelope) |
| Argon2id iterations | 2–10 |
| Argon2id parallelism | 1–4 |
| Default Argon2id | m = 65,536 KiB, t = 3, p = 1 |
| Backup entries | ≤ 100,000 |
| Manifest size | ≤ 16 MiB |
| Device secret file | `"PNDS"` | version 1 | installId[16] | blobLen:u32 | DPAPI blob (≤ 4 KiB) |

## 6. Versioning rules

- Any change to §1–§4 bumps the relevant version byte and requires reader support for older versions or an explicit refusal message.
- KDF parameters travel with each envelope; changing defaults does not break old envelopes.
- Labels (`ParvNegar/v1/...`) are part of the format; new labels get a new version segment.
