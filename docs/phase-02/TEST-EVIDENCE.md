# Phase 2 — Test evidence

All data is synthetic. Windows 11 x64 host; the x86 rows ran in 32-bit processes and were verified by `PARVNEGAR_EXPECTED_ARCH`. WOW64 does not qualify a 32-bit Windows installation.

## 1. Unit, integration and UI tests

Command: `test.ps1 -NoBuild` after the clean build (log `artifacts/logs/tests.log`). Exit 0, `FAILED_RUNS=0`, 614 s.

| Project | net10 x64 | net10 x86 | net48 x64 | net48 x86 |
|---|---|---|---|---|
| Domain.Tests | 27 | 27 | 27 | 27 |
| Security.Tests | 59 | 59 | 59 | 59 |
| Storage.Tests | 52 | 52 | 52 | 52 |
| Licensing.Tests | 51 | 51 | 48 | 48 |
| Application.Tests | 35 | 35 | 35 | 35 |
| App.Tests | 14 | 14 | 14 | 14 |
| **Total** | **238** | **238** | **235** | **235** |

**946 executions, 0 failed, 0 skipped.** The 3 Issuer tests run only in the Modern runs, because the publisher tool is .NET 10 only.

**Final pre-review run** (log `artifacts/logs/final-tests.log`), after the data-folder ACL test was added: App.Tests 15 per combination, **950 executions, 0 failed, 0 skipped**.

**Last addition:** the schema 1 → 2 upgrade test (`Existing_schema_1_database_with_data_upgrades_to_schema_2_and_keeps_its_data`) ran as Storage.Tests 53/53 on all four combinations. **Final total: 954 executions, 0 failed.**

## 2. Coverage of the Phase 2 prompt §25

| Area | Required case | Test(s) |
|---|---|---|
| Licensing | missing / valid / wrong product / invalid signature / tampered claims / unknown issuer key / unsupported format / malformed and ambiguous / device mismatch / one and several changed signals / missing and placeholder identifiers / cross-edition and cross-architecture / clock changes / provisioning replay | `LicenseVerificationTests.*`, `StrictJsonTests.*`, `LicensingAndSetupTests.*`, cross-edition `LICENSE_*` rows |
| Authentication | correct and incorrect / unknown account / disabled / password-only / device-only / swapped / stale / cross-installation / mandatory initial change / Unicode / policy / throttling across restarts / interrupted password change / failed and interrupted setup | `AuthenticationTests.*`, `KeyEnvelopeV2Tests.*`, `PasswordPolicyTests.*`, `LicensingAndSetupTests.Wrong_bootstrap…`, `…Interrupted_setup…` |
| Authorization | deny by default / direct service calls / unauthorized admin operations / escalation / permission changes / disabled account / last owner / session invalidation | `AuthorizationTests.*` |
| Recovery | correct / incorrect / tampered envelope / rotation / old password rejected / no publisher decryption / no plaintext key material in files / device re-binding | `RecoveryTests.*` |
| Interrupted recovery | Partly covered: the re-binding order and pending device secret promotion are implemented and documented. No test kills the process mid-transaction | Open (PHASE-03-PLAN §6) |
| Sessions | exact 15-minute policy (test clock) / explicit lock / Windows lock / suspend / connection disposal / service disposal / protected operation after lock / protected navigation / concurrent instance | `SessionTests.*`, `AuthorizationTests.Lock_invalidates…`, `LicensingAndSetupTests.Concurrent_second_instance…`. The Windows-lock and suspend handlers call the same `Lock(reason)` that is tested; real Win+L and sleep events were **not** triggered automatically |
| Audit and UI | chain modification and verification / no secrets in logs / Light and Dark smoke / RTL / official assets / exact title / low resolution / keyboard focus / no bypass through smoke or command-line modes | `AuditTests.*`, UI flow (§4), `ShellTests.*` (title, splash asset hash, 12-profile layout, smoke root, trust profile, ACL) |

## 3. Shell smoke (real windows, host)

| Build | Result | Splash visible | First main render (incl. splash) | Layout failures | Peak private |
|---|---|---|---|---|---|
| Modern x64 | PASS | 2,414 ms | 4,985 ms | 0 | 290 MB |
| Modern x86 | PASS | 2,423 ms | 4,307 ms | 0 | 130 MB |
| Legacy x64 | PASS | 2,269 ms | 3,547 ms | 0 | 284 MB |
| Legacy x86 | PASS | 2,316 ms | 3,642 ms | 0 | 130 MB |

## 4. Light/Dark UI evidence flow

Command: `tools/Run-UiSmoke.ps1`. It runs a synthetic installation through the real gates, signed with the development key.

| Build | Result | Screens captured | Setup (Argon2id + DB) | Delegate login | Delegate password change | Peak private |
|---|---|---|---|---|---|---|
| Modern x64 | PASS (20/20 steps) | 40 | 1,941 ms | 1,282 ms | 1,002 ms | 374 MB |
| Legacy x86 | PASS (20/20 steps) | 40 | 1,203 ms | 898 ms | 1,588 ms | 207 MB |

- **Login time:** a failed login (one Argon2id) takes 531 ms on Modern x64 and 817 ms on Legacy x86.
- **Capture refusals:** earlier attempts were refused by the privacy guard when other windows covered ParvNegar. That is the intended behavior. The runs listed are complete.

## 5. Failures found during Phase 2 and their fixes (none hidden)

| # | Failure | Cause | Fix |
|---|---|---|---|
| 1 | Licensing tests failed only on net48 (`System.Memory` not found) | `SequenceEqual` bound to the span overload that `System.Memory` provides | Replaced with `Bytes.FixedTimeEquals` |
| 2 | Recovery-key format test | Wrong expected group count in the test (55 characters = 11 full groups) | Test corrected |
| 3 | Password policy: a username with dots was not detected | Separators stripped from the password but not from the username | Strip both; threshold of fewer than 8 remaining characters |
| 4 | Application tests: most failed together | (a) throttle timestamps in ticks exceeded the strict-JSON safe-integer range; (b) `not_before` was always rounded up by 1 s; (c) parallel test classes hit the process-wide Argon2id gate (`Busy`, correct behavior) | Seconds and ceiling rounding; test assembly serialized |
| 5 | A tampered or unverifiable throttle file kept penalizing every later failure | The tamper entry carried an artificial failure count | One maximum wait, then normal counting |
| 6 | Setup threw when an installation folder existed without a marker | `Directory.Move` collision | Explicit `AlreadyInitialized` refusal |
| 7 | 1024×768 at 200%: home title not reachable vertically | The probe required tall blocks to fit the viewport | Tall blocks must be reachable by scrolling; a negative test remains |
| 8 | Hygiene: password-like literals | Persian UI labels in `Strings.cs` (false positive); **synthetic smoke credentials compiled into the app (true positive)** | Labels file exempted with a reason; smoke passwords are now random per run |
| 9 | Cross-edition: Phase 1 data sets failed `BACKUP_UNSUPPORTED_SCHEMA` | Test-tool bug: the limit used the consumer's schema count (now 2), not the archive's | Limit taken from the archive header; 16/16 pass |
| 10 | Isolation check script did not parse | C-style `\"` escape in PowerShell | Single-quoted pattern; ASCII-only script |
| 11 | Isolation false positive | `InternalsVisibleTo` matched as a reference | Rule limited to `ProjectReference` |
| 12 | Kit and scripts with LF endings in the working tree | Python rewrites | All `*.ps1` and `*.cmd` normalized to CRLF |
| 13 | Clean build reported −1 | The diagnostic pipeline (`Select-Object -First`) killed the child | Logged re-run; documented |

## 6. Not run

- Windows 7 SP1, 8.1 and 10 at runtime.
- Real 100%, 150% and 200% DPI rendering. The layout is verified at those scales by DIP simulation (PLATFORM-MATRIX).
- Screen readers.
- Real Win+L and sleep/resume events.
- A process kill during recovery or re-binding.
- 2 GB / dual-core performance.
