# Authorization matrix

Source: `Application/Security/Infrastructure.cs` (`Permissions`) and `SessionManager.Demand`. Identifiers are stored in the database and **must never be renamed**.

## 1. Roles

- **Owner:** the primary school administrator. Has every grantable permission and every owner-only operation. The **last active owner** cannot be disabled, deleted or demoted (`LastOwnerException`, tested). Another owner can be created by promoting a delegate.
- **Delegated administrator:** created by an owner. **All permissions are denied by default** (tested for every permission). Must change the temporary password before any protected access (tested).

## 2. Grantable permissions (delegates: only when granted)

| ID | Persian label | Module (phase) |
|---|---|---|
| `student.view` | مشاهده پرونده دانش‌آموزان | Students (3) |
| `student.create` | ثبت دانش‌آموز جدید | Students (3) |
| `student.edit` | ویرایش اطلاعات دانش‌آموز | Students (3) |
| `student.delete_restore` | حذف و بازگردانی دانش‌آموز | Students (3) |
| `record.educational` | مدیریت سوابق آموزشی | Records (4) |
| `record.pastoral` | مدیریت سوابق پرورشی | Records (4) |
| `document.manage` | مدیریت مدارک | Documents (5) |
| `print` | چاپ | Reports (6) |
| `export.pdf` | خروجی PDF | Reports (6) |
| `year.promote` | انتقال به سال تحصیلی بعد | Years (3) |
| `year.manage` | مدیریت سال تحصیلی | Years (3) |
| `backup.create` | تهیه نسخه پشتیبان | Backup (6) |

**Reserved (identifiers fixed now; not grantable until the module exists):** `report.view`, `assessment.manage`, `data.import`, `attendance.manage`.

## 3. Owner-only operations (never grantable)

| ID | Operation | Phase 2 service |
|---|---|---|
| `owner.accounts` | Create, list, disable, enable, delete accounts; change role | `CreateDelegate`, `ListAccounts`, `SetAccountStatus`, `SetRole` |
| `owner.permissions` | Assign permissions | `SetPermissions` |
| `owner.password_reset` | Reset a **delegate's** password to a temporary one | `ResetDelegatePassword` (an owner's password is reset only through recovery) |
| `owner.recovery` | Recovery-key rotation | `RotateRecoveryKey` |
| `owner.backup_restore` | Backup restore | Phase 6 |
| `owner.license` | License management | License import is also allowed before login (activation) |
| `owner.school_identity` | School identity configuration | Phase 3 |
| `owner.audit` | Read and verify the audit log | `ReadAudit`, `VerifyAudit` |

## 4. Enforcement

- **In the services, not only in the UI.** Every protected call goes through `SessionManager.Run(token, permission, work)` or an explicit `Demand`. It re-reads the account from the database **inside the transaction**: status, role, envelope and permissions. Hidden or disabled UI controls are convenience only. Direct service calls are tested for every owner-only operation and every grantable permission.
- **Escalation is refused:**
  - a delegate cannot grant permissions, change its own role, reset the owner's password, rotate the recovery key, read the audit log or create accounts;
  - an owner cannot grant owner-only or reserved identifiers (tested).
- **Live changes:**
  - a removed permission is refused on the very next call;
  - an account disabled or reset while signed in ends its session at the next protected call (`SessionExpiredException`), and the UI shows the lock screen (tested).
- **Session invalidation:** lock, logout and account changes increment the session generation. Old tokens and queued operations fail, and a transaction whose session was locked mid-way does not commit (tested).
- **Cryptographic note:** every signed-in account can technically reach the installation LMK. Deleting an account's envelope does not revoke key material or data that the account extracted earlier. Real revocation of a hostile former insider needs LMK rotation (THREAT-MODEL T-07).
