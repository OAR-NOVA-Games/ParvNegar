# Phase 2 — Cross-edition evidence

Command: `tools/Run-CrossEdition.ps1` (log `artifacts/logs/cross-edition.log`; matrix `artifacts/interop/cross-edition-matrix.tsv`, not committed). Exit 0, `FAILED_CHECKS=0`.

**Result: 185/185 PASS.**

## 1. Storage and backup (Phase 1 checks, now with schema 2)

- **Producers:** each of the 4 builds produces a synthetic set: an encrypted database migrated to **schema 2**, an attachment stream, and a PNBK backup with an in-memory LMK key entry.
- **Consumers:** each of the 4 builds consumes every set (16 pairs), with these checks:
  - direct database open, and wrong-key rejection;
  - attachment decryption;
  - backup restore and integrity, with the key entry kept in memory;
  - wrong passphrase, tamper, truncation, unsupported format and unsupported schema;
  - no staging folder left behind.
- **Result:** 164 rows PASS.

## 2. Backward compatibility with Phase 1 data

The 16 preserved **Phase 1** data sets (schema 1, produced by the Phase 1 builds) were consumed by all 4 Phase 2 builds: **16/16 PASS**. A test-tool defect was found and fixed first (TEST-EVIDENCE §5 #9).

## 3. Licensing

| Check | Result |
|---|---|
| `LICENSE_SIGNALS`, ×4 builds | smbios = good, disk = good, machine_guid = good in every build |
| `LICENSE_CLAIMS_IDENTICAL` | All 4 builds produce identical claim digests (prefixes `d5c0f5683097 55e368ebf0d6 70c344e33016`). x86 processes read the same MachineGuid through the 64-bit registry view |
| `LICENSE_VERIFY`, 4 × 4 | A request from each build, a license issued by the Issuer with an **ephemeral test key** for this run, verified by every build: 16/16 `Valid`, matched = 3 |

The test key is generated at the start of the run and deleted at the end. The anchors file stays in the ignored `artifacts/` folder.

## 4. Limits

- The 4 builds ran on one Windows 11 PC, so this proves format and edition/architecture interoperability, not other operating systems.
- Licensing across **different** PCs is covered by the unit tests (changed-signal cases), not by real hardware changes.
