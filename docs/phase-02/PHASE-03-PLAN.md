# Proposed Phase 3 scope (needs owner approval)

## 1. Entry gates

1. Phase 2 review and an owner-approved merge of `feat/phase-02`, with the `ci` workflow green on the PR.
2. Owner decisions:
   - O-07 and O-08: required student fields, target-group options, «اولویت ایثارگری» values, subjects per grade, enforcement of the national-ID check digit, year closure, copy and purge rules;
   - O-ACL: which Windows accounts may use ParvNegar on shared PCs.
3. Preferably the compatibility kit results from Windows 7 SP1 and 8.1, to close or redirect platform risks early.

## 2. Scope

- **School identity in the database:** `school_profile` managed in the app, owner only (`owner.school_identity`). The exact HomeTitle stays byte-identical.
- **Academic years:** create, activate and close (`year.manage`); classrooms per grade 1–6.
- **Students** (`student.*`):
  - create, view, edit, soft delete and restore;
  - national-ID validation per O-07, masked display;
  - Persian search with `PersianText.SearchKey`.
- **Enrollment** per year with snapshots, transfer, and the promotion preview and apply (`year.promote`), using the Phase 1 domain.
- **Authorization:** every service goes through `SessionManager.Run` with the permissions in AUTHORIZATION-MATRIX. Audit events for student changes record identifiers only.
- **UI:** student list, detail and edit screens in RTL, Light and Dark, with the same layout matrix and UI evidence flow.

## 3. Deferred (later phases)

- Educational and pastoral records (Phase 4).
- Documents (Phase 5).
- PDF and print, the backup/restore UI, the installer, and production licensing operations (Phase 6).

## 4. Security work carried forward

| Item | Proposal |
|---|---|
| LMK rotation UI (owner) | Uses `EncryptedDatabase.Rekey` and re-wraps every envelope; needed to revoke former insiders (THREAT-MODEL T-07) |
| Larger password blocklist | O-PWLIST |
| Production key ceremony and Production trust anchor | O-KEY-CER, O-PROD-ANCHOR. Only after owner approval |
| Fault-injection tests | Process kill during recovery, device re-binding and password change |
| Real-event tests | Win+L, sleep and resume, and user switching (manual checklist with recorded results) |

## 5. Qualification carried forward

- Kit runs on Windows 7 SP1, 8.1 and 10.
- A 2 GB / dual-core PC measurement.
- Real DPI 100%, 150% and 200%.
- A Narrator/NVDA pass.

## 6. Open test gap from Phase 2

A process kill mid-recovery or mid-re-binding is not automated yet (TEST-EVIDENCE §2). Add a fault-injection hook (test builds only) in Phase 3.
