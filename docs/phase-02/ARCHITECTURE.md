# Phase 2 — Architecture

Phase 1 stays unchanged: C#/WPF/MVVM; Modern (`net10.0-windows`) and Legacy (`net48`), each for x86 and x64; shared netstandard2.0 libraries; SQLite3 Multiple Ciphers; thin ADO.NET; BouncyCastle; checksummed SQL migrations. Phase 2 adds one shared library, one publisher-only tool and the security layer of the application.

## 1. Projects

```
src/
  ParvNegar.Domain        (netstandard2.0)  unchanged
  ParvNegar.Security      (netstandard2.0)  + PNKE v2, TypedCode, PasswordPolicy (+ blocklist resource)
  ParvNegar.Storage       (netstandard2.0)  + migration 0002 (accounts, permissions, audit); backup hardening
  ParvNegar.Licensing     (netstandard2.0)  NEW: strict JSON, ES256-P1363 verification, license/request/
                                            provisioning formats, device binding, Win32 signal reader
  ParvNegar.Application   (netstandard2.0)  + Security/: SessionManager (state machine, setup, login,
                                            sessions, authorization, recovery, account admin), AuditLog,
                                            LoginThrottle, PreUnlockJournal, GuardKey, InstallationPaths
  ParvNegar.App           (net10.0-windows; net48)  gated Persian shell, pages, Light/Dark themes,
                                            SessionMonitor, InstanceGuard, BuildTrust, smoke/UI flows
tools/
  ParvNegar.Issuer        (net10.0)         NEW, PUBLISHER ONLY: key files, issuance, provisioning
  ParvNegar.DevTool       (both editions)   + license-signals / license-request / license-verify
  ci/                                       CI path classifier and mapping tests
tests/
  ParvNegar.Licensing.Tests   NEW (Issuer tests in the Modern runs only)
  ParvNegar.Application.Tests NEW (end-to-end security flows on temporary installations)
```

Dependency direction:
- App depends on Application, which depends on Licensing, Storage, Security and Domain.
- The Issuer depends on Licensing only.
- No customer project references the Issuer (enforced by `tools/check-issuer-isolation.ps1`).
- No new NuGet package was added in Phase 2.

## 2. Runtime flow

```
start → splash (2 s) → Home (school identity only, no data)
      → «شروع» → SessionManager.State decides the page:
         Unlicensed ─────────────── License page (request / import)
         LicensedNotInitialized ─── Provisioning page (import .pnprov)
         ProvisioningRequired ───── Setup page (bootstrap code + new password) ─┐
         RecoveryKeyConfirmationRequired ── Recovery key page ◄─────────────────┘ (also after recovery/rotation)
         Locked ─────────────────── Login / lock screen
         PasswordChangeRequired ─── Mandatory password change
         Authenticated ──────────── Workspace (dashboard, accounts*, security, license, settings)
         RecoveryRequired ───────── Owner recovery (device secret unreadable)
         InvalidInstallation ────── Error page (never creates a new database)
                                                                    * owner only
```

**Lock triggers:**
- inactivity (15 minutes, monotonic clock),
- Windows lock or user switch,
- suspend and resume,
- the Lock and Logout buttons,
- an account change detected inside a protected operation.

**On lock:**
1. The session ends: the database connection is closed, the LMK and audit key are wiped, background work is cancelled and every token is invalidated.
2. The shell clears the current page and closes the other windows.
3. The lock screen is shown.

## 3. Trust boundaries

| Boundary | Inside | Outside / untrusted |
|---|---|---|
| Publisher | Issuer, signing key (offline), issuance records | Everything on the customer PC |
| Customer PC, pre-unlock | License file, request, guard key (DPAPI LocalMachine), throttle store, pre-unlock journal, envelope files, device secret (DPAPI) | Any local administrator can read, delete or roll back these files |
| Customer PC, unlocked session | Process memory: LMK, open encrypted database, audit key | Malware or an administrator in the same session |
| Encrypted database | Authoritative accounts, permissions, envelope revisions, audit chain | — |

Details: THREAT-MODEL.md and AUTHENTICATION-ARCHITECTURE.md.

## 4. What is deliberately not in Phase 2

Student screens, forms, documents, PDF, the backup/restore UI, the installer, production license issuance and production key generation (PHASE-03-PLAN.md).
