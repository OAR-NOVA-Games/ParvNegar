# Security audit log

Source: `Application/Security/AuditLog.cs`, `Infrastructure.cs` (`PreUnlockJournal`); schema in `Storage/Migrations/0002_accounts_and_audit.sql`; tested in `AuditTests`.

## 1. Events

Event kinds: `license.import`, `license.check`, `installation.setup`, `installation.device_rebind`, `auth.login`, `auth.login_failed`, `auth.throttled`, `session.lock`, `session.logout`, `account.create`, `account.disable`, `account.enable`, `account.delete`, `account.password_change`, `account.password_reset`, `account.permissions`, `recovery.owner`, `recovery.failed`, `recovery.key_rotated`, `recovery.key_confirmed`, `provisioning.bootstrap_failed`, `security.failure`.

**Fields:** `seq`, `utc`, `kind`, `actor_id`, `subject`, `outcome`, `detail`, `source` (`session` or `pre-unlock`), `mac`. Every text field is at most 200 characters.

**Content rule:** identifiers and outcome codes only.
- Never: passwords, recovery keys, bootstrap codes, raw hardware identifiers, full national IDs, phone numbers, document contents or student records.
- Usernames appear as account GUIDs. Before unlock, a name appears only as a 12-hex prefix of its hash.
- A test checks that a wrong password, the owner password, the recovery code and a disk serial never appear.

## 2. Chain

```
anchor_mac = 32 zero bytes (until pruning)
mac_n = HMAC-SHA256(audit_key, mac_{n-1} ‖ UTF8(seq ␟ utc ␟ kind ␟ actor ␟ subject ␟ outcome ␟ detail ␟ source))
audit_key = HKDF(LMK, "ParvNegar/v1/key/audit")
```

- Absent values are encoded as U+0000.
- `audit_state` holds `head_seq`, `head_mac`, `anchor_seq` and `anchor_mac`. An event and the state update are written in one transaction, usually the same transaction as the change they record.
- `Verify()` recomputes the chain from the anchor. It reports the first bad sequence number for:
  - a modified event (tested);
  - a deleted middle event (tested);
  - a deleted last event (the head does not match; tested);
  - reordering or a wrong key (tested).

## 3. Bounded growth

At most 50,000 events (`AuditLog.MaxEvents`). When the limit is exceeded, the oldest events are deleted and the anchor moves to the MAC of the last deleted event, so the remaining chain still verifies (tested with a limit of 20). The Phase 6 backup can export the log before pruning if the owner wants long-term retention.

## 4. What the chain does not do

- It does **not** make the log tamper-proof.
- Anyone who holds the LMK (any signed-in account, or anyone with the password, the device and the files) can rewrite a consistent chain.
- A rollback of the whole database file, or deletion of the whole log, is not detected.
- It detects edits made *without* the key, and accidental damage.

## 5. Pre-unlock journal

- **Why:** before unlock there is no LMK, so events such as failed logins, throttling, license imports, bootstrap failures and failed recoveries go to `security/journal.pnj`.
- **Format:**
  - at most 256 records, oldest dropped first;
  - each line is `ticks ⇥ kind ⇥ subject ⇥ outcome ⇥ verifiedFlag ⇥ mac`;
  - `mac = HMAC-SHA256(guard-derived key, previous mac ‖ line)`.
- **Import:** after the next successful unlock, records are imported into the audit chain with `source = 'pre-unlock'` and the journal is cleared. A broken journal chain is imported with the detail "integrity NOT verified", plus a `security.failure` event.
- **Not authenticated by the LMK:** a local administrator can delete or edit the journal before the next sign-in. Its guard key is only DPAPI-protected. The journal is shown and treated as weaker evidence (tested: tampering is flagged; the cap holds).
