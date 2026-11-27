# Storage contract changes in Phase 2

## 1. Database schema 2 (`0002_accounts_and_audit.sql`, new migration; migration 1 unchanged)

| Table | Purpose |
|---|---|
| `installation_info` (single row) | Installation ID, license ID, provisioning ID, recovery envelope ID and revision, recovery-confirmed flag, device generation |
| `account` | ID, unique canonical username, display name, role (1 owner, 2 delegate), status (1 active, 2 disabled, 3 deleted), envelope ID (nullable) and revision, must-change-password flag, `authz_version`, timestamps, creator |
| `account_permission` | (account ID, permission) grants, with grantor and time |
| `audit_event`, `audit_state` | Audit HMAC chain and its head and anchor (AUDIT-LOG-FORMAT) |

**Compatibility:**
- Phase 1 databases (schema 1) are migrated to 2 transactionally on open, with checksums verified.
- A database newer than the app is refused (Phase 1 rule).
- Cross-edition evidence:
  - the 16 Phase 1 data sets, all at schema 1, are opened and verified by every Phase 2 build;
  - Phase 2 producers write schema 2 data sets that every build reads (CROSS-EDITION-EVIDENCE).

## 2. PNKE v2 key envelope (178 bytes)

New format; v1 is unchanged and can still be read with the v1 reader. Layout in `Security/KeyEnvelopeV2.cs`.

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | `PNKE` |
| 4 | 1 | version = 2 |
| 5 | 1 | kind |
| 6 | 12 | memoryKiB, iterations, parallelism |
| 18 | 16 | salt |
| 34 | 16 | accountId |
| 50 | 16 | installId |
| 66 | 16 | envelopeId |
| 82 | 4 | revision |
| 86 | 32 | usernameHash |
| 118 | 12 | nonce |
| 130 | 48 | AES-256-GCM(LMK) and tag |

- **AAD:** bytes 0–117.
- **KEK derivations:** identical to v1.
- **The v2 reader rejects v1 envelopes** (tested). Phase 1 created no account envelopes in installations, so there is nothing to migrate. Future migration rule: unwrap v1, then re-wrap as v2 for the same account, in the password-change transaction.

## 3. PNBK v1 backup (Phase 1 follow-up)

The wire format is unchanged. Reader rules and writer behavior changed (`docs/phase-01/STORAGE-FORMAT.md` §4, PHASE-01-REVIEW-FOLLOWUP F-1 to F-5).

## 4. New non-database files

| File | Format | Section |
|---|---|---|
| `installation/installation.json` | Canonical JSON marker | PROVISIONING §3 |
| `license/license.pnlic` | Signed artifact | LICENSE-FORMAT §3 |
| `activation/pending-request.pnreq` | Canonical JSON | LICENSE-FORMAT §4 |
| `security/guard.key` | DPAPI blob | SESSION-SECURITY §4 |
| `security/throttle.pnth` | 32-byte HMAC, then canonical JSON | SESSION-SECURITY §4 |
| `security/journal.pnj` | MAC-chained lines | AUDIT-LOG-FORMAT §5 |
| `%LocalAppData%\…\settings.json` | Canonical JSON: theme and optional username only | SESSION-SECURITY §8 |

Each format has a format or version marker. Any future change follows the Phase 1 versioning rules.
