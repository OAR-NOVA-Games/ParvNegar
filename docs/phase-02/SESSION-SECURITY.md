# Session security

Source: `Application/Security/SessionManager*.cs`, `App/Services/AppServices.cs` (`SessionMonitor`, `InstanceGuard`, `ThemeManager`), `App/Services/DataFolderAcl.cs`, `App/ViewModels/Shell.cs`.

## 1. State machine

| State | Meaning | Screen |
|---|---|---|
| `Unlicensed` | No license, or the license is invalid for this PC, edition or architecture | License |
| `LicensedNotInitialized` | Valid license, no installation | Provisioning import |
| `ProvisioningRequired` | Provisioning file verified and bound; waiting for the bootstrap code and a new password | Setup |
| `RecoveryKeyConfirmationRequired` | Session open, but the owner has not confirmed storing the recovery key (after setup, recovery or rotation). Every protected operation is refused | Recovery key |
| `Locked` | Installation ready, no session | Login or lock screen |
| `PasswordChangeRequired` | Session open with a temporary password. Every protected operation is refused | Password change |
| `Authenticated` | Normal session | Workspace |
| `RecoveryRequired` | The device secret cannot be read (DPAPI of another Windows installation) | Owner recovery |
| `InvalidInstallation` | Installation folder without a valid marker, or the database is missing | Error. A new database is **never** created |

The state is recomputed from disk and the license on every `Refresh()`. Before a session exists, the branded home screen is the only screen that does not need a license and a sign-in. It shows the school name, title and logos only.

## 2. Inactivity

- **Timeout:** 15 minutes (`SessionOptions.InactivityTimeout`), measured on the **monotonic clock** (`Stopwatch`). Wall-clock changes neither shorten nor extend it (tested at exactly 15:00.000 versus 14:59.999).
- **What counts as activity:** keyboard, text, mouse-button, wheel, touch and stylus input **in ParvNegar windows** (`InputManager.PreProcessInput`). Mouse movement and input to other applications do not count.
- **Check interval:** a background timer checks every 15 seconds, so a lock happens at most 15 seconds after the deadline.

## 3. Immediate locks

| Event | Source | Reason |
|---|---|---|
| Windows lock (Win+L, screen saver with lock) | `SystemEvents.SessionSwitch` → `SessionLock` | `WindowsLock` |
| User switching, remote disconnect, logoff | `ConsoleDisconnect`, `RemoteDisconnect`, `SessionLogoff` | `SessionSwitch` |
| Sleep or hibernate | `SystemEvents.PowerModeChanged` → `Suspend` | `Suspend` |
| Resume | `PowerModeChanged` → `Resume` (defensive: locks again) | `Suspend` |
| «قفل» button / «خروج از حساب» button | UI | `Explicit` / `Logout` |
| Account disabled, deleted or reset while signed in | Detected in `Demand` | `AccountChanged` |

**What a lock does:**
1. Writes the audit event.
2. Increments the session generation, which invalidates every token.
3. Cancels the session's `CancellationTokenSource`.
4. Disposes the SQLite connection and clears the pools: the database file has no open handle afterwards (tested by renaming it).
5. Wipes the LMK and the audit-key buffers.
6. The shell clears the current page (`PageViewModel.Clear` empties lists and messages), closes every other window, and shows the lock screen with the reason.

A protected operation runs under the same lock object as `Lock()`. Locking therefore waits for a running operation, and an operation whose session ended before commit rolls back (tested).

**Memory limit:** managed strings (for example `PasswordBox.Password` while it is passed to the service) and copies made by the runtime cannot be guaranteed to be erased. Byte buffers that ParvNegar controls are wiped.

## 4. Persistent progressive throttling

- **Keys:** `u:<first 24 hex of the username hash>` per name; `global` across all names; plus `bootstrap` and `recovery`.
- **Delay after n consecutive failures for a name:** 0 for n ≤ 3, then 5, 15, 30, 60, 120, 300 seconds, and at most 600 seconds. The global key allows 10 free failures, then uses the same steps. There is **no permanent lockout**: after the wait, a correct sign-in always succeeds (tested).
- **Storage:** `security/throttle.pnth` is canonical JSON with an HMAC-SHA256 under a key derived from the **guard key**. The guard key is 32 random bytes protected with DPAPI LocalMachine.
- **Survives restarts** (tested).
- **Clock moved backwards:** the maximum delay from now, never a reset (tested).
- **Tampered or unverifiable file:** one maximum delay, then normal counting (tested). This also happens once after a Windows reinstall, when DPAPI can no longer open the old guard key.
- **Within a process:** a monotonic deadline is enforced as well.

**Limits:**
- A local administrator can delete the throttle file and reset the counters.
- An attacker who copies the envelope files and runs code on this Windows installation (DPAPI) can guess offline. Then only Argon2id and password strength slow them down.

Throttling protects the interactive login, not stolen files.

## 5. Pre-unlock journal

Covered in AUDIT-LOG-FORMAT §5.

## 6. Concurrency and single instance

- **Argon2id:** one at a time per process (`SemaphoreSlim`). A concurrent request gets `Busy`.
- **Single instance:** `InstanceGuard` holds the named mutex `Global\ParvNegar-<hash of the data root>`, falling back to `Local\`. A second start shows «پرونگار هم‌اکنون روی این رایانه باز است…» and exits.
- **Setup:** `setup.lock` is held exclusively during setup.

## 7. Data-folder ACL

- **New data root:** when ParvNegar creates `%ProgramData%\OARNOVA\ParvNegar`, it applies a **protected DACL**:
  - SYSTEM: full control;
  - Administrators: full control;
  - the Windows account that created it: full control.
  - Inherited "Users can read" from `%ProgramData%` is removed (tested).
- **Existing folders** are never changed.
- **Adding other Windows accounts** of a shared school PC is an owner decision (O-ACL). An administrator then grants them, for example `icacls … /grant "PC\User:(OI)(CI)M"`.
- **Scope:** the ACL limits other *standard* users, not administrators.

## 8. Remembered username

Optional checkbox on the login screen. `%LocalAppData%\OARNOVA\ParvNegar\settings.json` stores only `theme` and `remembered_username`. Passwords are never remembered.
