# Phase 1 — Cross-Edition Interoperability Evidence

Date: 2026-11-26. Command: `powershell -ExecutionPolicy Bypass -File tools\Run-CrossEdition.ps1` → exit 0, `FAILED_CHECKS=0`, 24 s. Output matrix: `artifacts/interop/cross-edition-matrix.tsv` (not committed; 165 lines). Synthetic data and a publicly known scratch key (`ParvNegar.DevTool`, test only).

> **2026-11-27 follow-up:** matrix re-run 164/164 PASS. The backup check now also verifies that the LMK key entry stays in memory. The 16 Phase 1 data sets were consumed by the follow-up builds, 16/16 exit 0 (PHASE-01-REVIEW-FOLLOWUP.md §3.2).

## 1. Method

1. Each of the four builds runs `interop-produce <dir>` and creates:
   - an encrypted database (schema 1, synthetic school, year, class, student and enrollment, Persian text, a leading-zero synthetic national ID);
   - an attachment-style chunked AEAD stream encrypted with the derived attachment key;
   - a PNBK backup containing the database, the stream and the LMK.
2. Each of the four builds runs `interop-consume` on each of the four data sets (16 pairs). Per pair:

| Check | Meaning |
|---|---|
| DB_FORMAT_DIRECT_OPEN | Opens the producer's database with the HKDF-derived key; exact title, student name and leading-zero ID; schema version; `integrity_check=ok` |
| DB_WRONG_KEY_REJECTED | Random key → `DatabaseKeyException` |
| ATTACHMENT_DECRYPT | Decrypts the producer's chunk stream; SHA-256 equals the plaintext (360,000 bytes) |
| BACKUP_RESTORE_AND_INTEGRITY | Restores the producer's backup to staging; no license/device entry; opens the restored DB with the LMK from the archive |
| BACKUP_WRONG_PASSPHRASE | `AuthenticationFailedException` |
| BACKUP_TAMPER | Byte flip in the middle → rejected |
| BACKUP_TRUNCATED | Last 100 bytes removed → rejected |
| BACKUP_UNSUPPORTED_FORMAT | Minimum reader version 9 → `UnsupportedBackupException` |
| BACKUP_UNSUPPORTED_SCHEMA | Reader limit below the archive schema → `UnsupportedBackupException` |
| NO_STAGING_LEFT_AFTER_FAILURES | No staging directory survives the five failure cases |

## 2. Results

Producers: 4/4 PASS. Pairs: 16 × 10 checks = 160 PASS. Total 164 PASS, 0 FAIL.

| Producer ↓ / Consumer → | Modern x64 | Modern x86 | Legacy x64 | Legacy x86 |
|---|---|---|---|---|
| Modern x64 | 10/10 | 10/10 | 10/10 | 10/10 |
| Modern x86 | 10/10 | 10/10 | 10/10 | 10/10 |
| Legacy x64 | 10/10 | 10/10 | 10/10 | 10/10 |
| Legacy x86 | 10/10 | 10/10 | 10/10 | 10/10 |

Required directions from the Phase 1 prompt: Modern x64 → Legacy x86 ✓, Legacy x86 → Modern x64 ✓, Modern x86 → Legacy x64 ✓, Legacy x64 → Modern x86 ✓, same-edition x86/x64 ✓.

## 3. Limits

- All 16 pairs ran on one Windows 11 host. The kit also writes an interop data set on each real machine (`results-<PC>\<edition>\interop`). When those return, they will be consumed here to cover Windows 7/8.1-produced data.
- The backup restore here stops at staging; the atomic switch to live data and the pre-restore backup are Phase 6.

## 4. Machine-bound protection on a licensed destination

Backups never carry the device secret or the license. After a restore on a new or replacement PC:
1. A valid license for that PC is required (Phase 2).
2. The new installation creates its own installId and DPAPI LocalMachine device secret.
3. The LMK from the archive is re-wrapped into new PNKE kind-1 envelopes, password ‖ new device secret, for the owner (and for delegates per O-06).
4. Envelopes from the old PC are not used.

This is KEY-LIFECYCLE.md "Restore on a replacement device".
