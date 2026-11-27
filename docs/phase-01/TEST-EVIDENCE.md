# Phase 1 — Test Evidence

Date: 2026-11-26. All data synthetic. Commands run from the repository root on the Windows 11 x64 host. "x86" rows ran in 32-bit processes (WOW64) and were verified by `PARVNEGAR_EXPECTED_ARCH`, which makes every test class fail if `Environment.Is64BitProcess` disagrees. WOW64 on Windows 11 does not qualify a 32-bit Windows installation.

> **Superseded counts (2026-11-27 follow-up):** 134 tests × 4 combinations = 536 executions, 0 failures. New tests and results: PHASE-01-REVIEW-FOLLOWUP.md §2 and §3.2. The figures below are the original 2026-11-26 run.

## 1. Unit, security, storage and UI tests

Command: `powershell -ExecutionPolicy Bypass -File test.ps1 -NoBuild` → exit 0, `FAILED_RUNS=0`, 164 s. TRX files: `artifacts/test-results/<tfm>-<rid>/` (not committed).

| Project | Tests | net10 x64 | net10 x86 | net48 x64 | net48 x86 |
|---|---|---|---|---|---|
| ParvNegar.Domain.Tests | 27 | 27/27 | 27/27 | 27/27 | 27/27 |
| ParvNegar.Security.Tests | 43 | 43/43 | 43/43 | 43/43 | 43/43 |
| ParvNegar.Storage.Tests | 36 | 36/36 | 36/36 | 36/36 | 36/36 |
| ParvNegar.App.Tests | 8 | 8/8 | 8/8 | 8/8 | 8/8 |
| **Total** | **114** | 114 | 114 | 114 | 114 |

456 test executions, 0 failed, 0 skipped. No test is excluded or skipped.

## 2. What the tests cover

| Area | Tests (examples) |
|---|---|
| Known answers | HKDF RFC 5869 case 1; AES-256-GCM McGrew–Viega case 16; Argon2id RFC 9106 §5.3; LMK→database/attachment/audit keys against values computed independently with Python `hmac` |
| Tamper | every ciphertext/tag byte of a GCM message; AAD and nonce change; chunk-stream byte flips; every byte of a PNKE envelope; KDF parameter change inside bounds |
| Format structure | chunk nonce/AAD byte layout; truncation at and inside chunk boundaries; reorder; duplicate; cross-stream splice; header substitution; flag bits; oversize/undersize lengths; non-final short chunk; trailing data; stream index range |
| Key envelope | roundtrip; device factor alone fails; password alone fails; other installation refused; recovery envelope; password change re-wrap (and documented limit that an old wrap still opens); salts and nonces differ per wrap; KDF bounds (4 GiB request refused) |
| DPAPI | LocalMachine device secret roundtrip; tampered blob rejected; wrong installation refused; file never contains the secret |
| Encrypted DB | correct key; wrong key (file unchanged); missing/short key refused before any file access; plain SQLite cannot read; plaintext DB never opened; create refuses overwrite and leaves no staging file; header not plain; DB/WAL/SHM/journal canary scans (WAL scanned before checkpoint, journal scanned during an open write transaction); page tamper → SQLITE_CORRUPT/NOTADB; crash image of an uncommitted transaction recovers to the last commit with `integrity_check=ok`; no pooled handle after dispose; rekey; loaded `sqlite3mc.dll` PE machine equals process architecture; pragma contract pinned (SQLite3MC 2.7.1, SQLite 3.54.1) |
| Large-sort temp files | 20,000 rows, 256 KiB cache, ORDER BY over blobs. Control with `temp_store=FILE` must create `etilqs_*` files (it does). With `temp_store=MEMORY` there must be none (there are none) |
| Migrations | apply once, idempotent; failing migration rolls back fully (schema and user_version unchanged, partial table absent); changed applied migration detected; newer database refused; CRLF/LF checksum stability; gaps rejected; foreign keys, unique enrollment, national ID shape enforced by schema |
| Backup | roundtrip with equal SHA-256 for 0 B/1 B/64 KiB/300 KB entries; names not visible in archive bytes; wrong passphrase; flips in header, wrap, body; truncation; trailing bytes; unsupported reader version; newer schema; foreign magic; no staging left after any failure; refuses overwrite; KDF bounds; forbidden/unsafe names (license, device, secrets, traversal, absolute, uppercase, `//`, drive) |
| Domain | exact home title (UTF-16 equality, UTF-8 bytes, FNV-1a fingerprint of the SRS text, length 79, no ZWNJ); title with spaces kept; Mehr–Khordad boundaries for 1403/1404/1405; summer gap; labels with Persian digits; year states and closure; grade 1–6 and no grade 7; national ID digit normalization and leading zeros; masked `ToString`; stable student ID; search-key normalization; promotion: next grade, source unchanged, idempotent repeat, grade 6 complete only, repeat/withdraw, class existence and grade, closed target, backwards year, duplicate enrollment and duplicate request conflicts; transfer rules |
| UI shell | RTL window, exact title from profile (not XAML), three Persian actions, icon, Vazirmatn font, buttons inside 1024×768, missing profile message, Start/Back/Escape, XML profile reader with DTD/XXE rejected, shipped config title exact, no PDF/preview assemblies loaded |

## 3. Shell smoke (real window, host)

Command: `ParvNegar.exe --smoke <dir>` (also part of the compatibility kit). Window shown on screen, captured from screen pixels after first render.

| Build | Startup to first render | Peak private bytes | Result |
|---|---|---|---|
| Modern x64 | 565 ms | 238,878,720 | PASS |
| Modern x86 | 1,532 ms | 134,434,816 | PASS |
| Legacy x64 | 1,221 ms | 233,553,920 | PASS |
| Legacy x86 | 1,978 ms | 119,431,168 | PASS |

Single runs on a warm host; not the low-resource profile. The SRS target "startup-to-login ≤ 5 s" will be measured on reference hardware.

## 4. Isolated compatibility checks (kit on host)

Command: `RUN-COMPAT.cmd` from `artifacts\compat-kit`. Each check ran in its own child process (`ParvNegar.DevTool --check <id>`).

| Check | legacy-x86 | legacy-x64 | modern-x86 | modern-x64 |
|---|---|---|---|---|
| ENV | PASS | PASS | PASS | PASS |
| STORAGE (SQLite3MC create/migrate/wrong key) | PASS | PASS | PASS | PASS |
| CRYPTO (GCM/HKDF known answers) | PASS | PASS | PASS | PASS |
| DPAPI (LocalMachine device secret) | PASS | PASS | PASS | PASS |
| ARGON2-64MIB timing | PASS, 746 ms | PASS, 245 ms | PASS, 674 ms | PASS, 483 ms |
| BACKUP roundtrip | PASS | PASS | PASS | PASS |
| Interop data set produced | PASS | PASS | PASS | PASS |
| Shell smoke | PASS | PASS | PASS | PASS |

Timings are from the first kit run. The kit was rebuilt after adding the DevTool OS-compatibility manifest and run again: 24/24 checks PASS, and ENV now reports 10.0.26300 on .NET Framework too. Argon2id peak private bytes in the check process: 81–95 MB.

## 5. Other gates

| Gate | Command | Exit | Result |
|---|---|---|---|
| Cross-edition matrix | `tools\Run-CrossEdition.ps1` | 0 | 164/164 PASS (CROSS-EDITION-EVIDENCE.md) |
| Vulnerability scan | `dotnet list ParvNegar.sln package --vulnerable --include-transitive` | 0 | "no vulnerable packages" for all 10 projects |
| Native PE import check | `python tools\check-native-imports.py src\ParvNegar.App\bin\Release tools\ParvNegar.DevTool\bin\Release` | 0 | Only native DLL: `sqlite3mc.dll` x86/x64, subsystem 6.0, KERNEL32 only, static CRT, `WINDOWS7_BLOCKERS=0` |
| Repository hygiene | `tools\check-repo-hygiene.ps1` | 0 | `HYGIENE_OK` (first run found 2 false positives, the FNV-1a offset basis and a fixture modulus, now on an explicit allowlist) |

## 6. Failures found during Phase 1 and their fixes (none hidden)

| Failure | Cause | Fix |
|---|---|---|
| 15 storage tests: "SQLite3 Multiple Ciphers is not loaded" (SQLite error 26) | `sqlite3mc_version()` was called on the encrypted file before keying; any statement needs page 1 | Provider proof moved to an in-memory connection at initialization |
| 5 storage tests after that change | Race: parallel tests saw "initialized" before the version was stored | Lock around provider initialization |
| Crash-image test: IOException on `-shm` | The WAL index is byte-range locked | The crash image copies DB and WAL only (SHM is rebuilt on open) |
| UI test `Title_comes_from_profile_not_from_xaml`: empty text | Deferred binding work on a new STA dispatcher | Flush the dispatcher before asserting; 3 consecutive passing runs |
| Smoke screenshots mirrored | `RenderTargetBitmap` and `PrintWindow` mirror RTL windows; the on-screen window was correct | Capture screen pixels with BitBlt from the DWM frame bounds |
| `test.ps1` first run: every run exit `-2147450751` | Splatted `@args` automatic variable → `dotnet` called without arguments | Variable renamed |

## 7. Not run in Phase 1

- Windows 7 SP1, 8.1 and 10 runtime rows: no such machines on this host. Kit ready; owner to run (PLATFORM-MATRIX.md).
- Low-resource profile (2 GB RAM, 2 cores, 1024×768 physical screen).
- Keyboard-only walkthrough by a person and screen-reader check (automated focus/Escape tests only).
- Physical printing, PDF, preview: out of Phase 1 scope.
