# Phase 1 — Review Follow-up

Date: 2026-11-27. Branch: `feat/phase-01`. Scope: the Phase 1 prerequisite gate from the Phase 2 prompt (§3). **Phase 2 is not started**: Phase 1 is not merged into `main` and has no merge approval.

## 1. Gate verification

| # | Gate item | Result |
|---|---|---|
| 1 | Local and remote SHAs | Before the follow-up: `main` = `origin/main` = `dfc31257387e3c188c28e5e2a78cded5ed3f2198`; `feat/phase-01` = `origin/feat/phase-01` = `b92e2e602f41c43fe81035438a5d3748bc0257d8`. Follow-up commits are on top of `b92e2e6` (§6). |
| 2 | Repository privacy | **FAIL: `OAR-NOVA-Games/ParvNegar` is PUBLIC** (`gh repo view` → `"visibility":"PUBLIC","isPrivate":false`). Phase 1 documents describe it as a private remote. Owner action required (§5). |
| 3 | `PHASE-01-REVIEW-FOLLOWUP.md` | Did not exist; this document is it. No separate written review findings were available in the repository, so the findings below come from the items the Phase 2 prompt lists and from a fresh code review. |
| 4 | Plaintext LMK or other key material on disk in backup/restore | **Found and fixed** (F-1). |
| 5 | Backup path validation, source mutation, staging cleanup, archive entry validation | **Found and fixed** (F-2 to F-5). |
| 6 | Pre-authentication architecture review | Done (§4). It needs no Phase 1 code change; it gives binding requirements for Phase 2. |
| 7 | Clipped screenshots, physical resolution and DPI | **Found and fixed** (F-6 to F-8). |
| 8 | Phase 1 regression gates | All re-run and green (§3). |
| 9 | Phase 1 merged into `main` with owner approval | **NOT DONE.** There is no pull request and no merge. The prompt says the agent must not merge Phase 1 silently. |

## 2. Findings and fixes

| ID | Severity | Finding | Fix | Evidence |
|---|---|---|---|---|
| F-1 | High | The interop producer (`tools/ParvNegar.DevTool`, `interop-produce`) wrote the LMK to a plaintext file `lmk.bin` so it could become backup entry `keys/lmk`. `RestoreToStaging` also wrote every entry, including `keys/lmk`, to the staging directory in plaintext. This contradicts KEY-LIFECYCLE ("never write the LMK … in plaintext to disk"). Scratch key only; no real key was exposed. | Key entries (`keys/...`) are memory-only on both sides. `BackupEntrySource.FromKeyMaterial` is the only way to add one; a file source named `keys/...` is refused. `RestoreToStaging` returns key entries in memory (`BackupInfo.GetKeyEntry`) and never writes them; `BackupInfo` is `IDisposable` and wipes them. The PNBK v1 wire format is unchanged: all 16 Phase 1 interop sets restore with the new code. | `Key_entries_round_trip_in_memory_and_never_reach_disk`, which scans every file in the work folder for the key bytes; interop check `BACKUP_RESTORE_AND_INTEGRITY` reports "key entry in memory only" and fails if `keys/` or any `lmk` file appears in staging. |
| F-2 | Medium | Path validation: Create did not check that the target folder exists or that the archive is not one of its sources. It deleted any existing `<archive>.partial`, even a user's file. Restore accepted a missing parent folder or a volume root. Entry names allowed Windows device names (`con`, `nul.txt`, `com1.blob`) and trailing-dot segments (`a./b`), which Windows silently maps to devices or other names. | Target folder must exist; the archive cannot be a source; the partial file has a unique random name and only our own partial file is removed. Restore needs an existing parent and archive and refuses a volume root. New name rules: device names, trailing dots, `.pnlic`/`.pnreq`/`.pnds`/`.pnprov`. | `Unrelated_partial_files_are_never_deleted_and_archive_cannot_be_its_own_source`, `Restore_requires_existing_parent_and_missing_archive_creates_nothing`, 8 new `Unsafe_or_forbidden_entry_names_are_refused` cases |
| F-3 | Medium | Source mutation: sources were hashed first and encrypted later in a second pass. Only the size was compared, so a same-size change between the passes produced an archive whose manifest hash did not match its content; that is only detected at restore time. | Every source is opened with `FileShare.Read` before hashing and kept open, so other processes cannot write to it. A source already open for writing (a live database) is refused. Bytes are hashed again while they are encrypted, and any difference aborts the backup. | `Source_held_open_for_writing_is_refused_and_leaves_nothing`, `Sources_cannot_be_opened_for_writing_while_the_backup_is_written` |
| F-4 | Medium | Staging cleanup: if deleting the staging folder failed after an error, the failure was swallowed. Partially restored files could stay behind without any signal. | 3 attempts. If the folder still exists, `BackupStagingCleanupException` is thrown with the path and the original error as inner exception. Key entries are wiped on every failure path. | Existing "no leftovers" tests and interop check `NO_STAGING_LEFT_AFTER_FAILURES`. A forced cleanup failure is **not** tested: it needs a file locked by another process at the right moment. |
| F-5 | Medium | Archive entry validation: a manifest could contain a file name that is also used as a folder (`data` and `data/x`); restore then failed with an `IOException` instead of a format error. Names were decoded with lenient UTF-8. Schema version 0 was accepted. | Name-set check on create (`ArgumentException`) and restore (`MalformedDataException`); strict UTF-8; schema ≥ 1. | `Conflicting_entry_names_are_refused` |
| F-6 | Medium | The main window was 1100×720 DIP with a minimum of 800×600 DIP. On a 1024×768 display it did not fit even at 100%. At 125% (work area 819×566 DIP) the minimum height pushed the footer off-screen. The About dialog (586 DIP high, not resizable, no scrolling) does not fit 1366×768 at 150% (464 DIP). | `WindowFit` clamps size, minimum and maximum to the Windows work area. The placeholder view and About scroll. Home buttons wrap (`WrapPanel`). The header notice and the footer school name wrap instead of overlapping. | `Every_display_profile_keeps_all_controls_reachable_without_overlap` (12 profiles × 2 views), `Window_fit_never_exceeds_the_work_area`, and `Layout_probe_detects_cut_off_controls`, which proves the probe can fail |
| F-7 | Medium | Phase 1 screenshots `home-1024x768.png` etc. were a 1024×768 **DIP** window at 125%, i.e. 1264×952 physical pixels. They did not show what a 1024×768 display shows, and they were evidence of rendering, not usability. | Screenshots are renamed and re-taken per display profile, and their size and method are documented (UI-SHELL-REPORT §3). The shell smoke runs `LayoutProbe` on the full matrix and fails on any layout problem. The smoke log separates physical pixels, DPI scale, work area (DIP) and window size (DIP). | `shell-smoke.tsv`: 24 layout rows, `layout_failures 0` on all four builds |
| F-8 | High (privacy) | The screen capture copies screen pixels. When another window covered the ParvNegar window, that window was recorded. This happened during this follow-up: a splash capture recorded another application. The file stayed in the agent's scratch folder and was deleted; it was never committed, uploaded or included in any report. The same risk applied to the compatibility kit on owner machines. | Capture is refused unless all 81 points of a 9×9 grid inside the capture rectangle belong to the ParvNegar window (`WindowFromPoint`). It is also refused if the window extends beyond its monitor. The dispatcher now keeps rendering during the wait (it used to call `Thread.Sleep` on the UI thread). Kit README tells testers to clear the screen. Residual: on Windows 11 the 4 rounded-corner pixels of a framed window can show what is behind it. | Re-run on all four builds; every image reviewed |

## 3. Owner requests handled in this follow-up (2026-11-27)

1. **Startup splash with the studio logo, 2 seconds.**
   - `SplashWindow` shows the owner-supplied white OAR NOVA mark on brand navy `#14233D`, as the asset README requires.
   - The main window is built only after the splash has rendered, because building it earlier blocked the splash's first paint by about 1.9 s.
   - The main window is shown once the splash has been visible for `MinimumDuration` = 2 s, measured with a monotonic `Stopwatch`.
   - Measured visible time: 2,083–2,145 ms on all four builds.
   - Time to the first visible pixel is unchanged; the main window now appears about 2 s later (smoke `startup_to_first_render_ms` 2.6–4.4 s, including the splash).
2. **Publisher and school logos from the zip in Downloads.**
   - The zip was verified against its `SHA256SUMS.txt`.
   - The publisher mark is committed under `assets/publisher/`: the original WebP (byte-identical) plus a PNG with verified identical pixels. The PNG is needed because Windows 7/8.1 WPF cannot decode WebP.
   - The **school logos are not committed**: the zip README makes their inclusion and the formal school display name subject to owner confirmation.

## 3.2 Regression gates (2026-11-27, Windows 11 host, x64 + WOW64)

| Gate | Command | Result |
|---|---|---|
| Clean locked build ×4 | `build.ps1 -LockedRestore` after deleting all `bin`/`obj` | BUILD_OK, 0 warnings, 46 s |
| Tests ×4 | `test.ps1 -NoBuild` | 134 tests × 4 = **536 PASS, 0 failed, 0 skipped** (Domain 27, Security 43, Storage 52, App 12) |
| Cross-edition matrix | `tools/Run-CrossEdition.ps1` | **164/164 PASS** (16 pairs) |
| Phase 1 data sets read by the follow-up code | `interop-consume` on the 4 preserved Phase 1 sets × 4 builds | 16/16 exit 0 (backward compatibility of PNBK v1) |
| Vulnerability audit | `dotnet list ParvNegar.sln package --vulnerable --include-transitive` | No vulnerable packages (10 projects) |
| Native imports | `python tools/check-native-imports.py …` | `WINDOWS7_BLOCKERS=0` |
| Hygiene | `tools/check-repo-hygiene.ps1` | `HYGIENE_OK files_checked=157` |
| Shell smoke ×4 | `ParvNegar.exe --smoke <dir>` | PASS ×4; splash ≥ 2 s ×4; layout_failures 0 ×4 |
| Compatibility kit on host | `tools/Build-CompatKit.ps1`, then `RUN-COMPAT.cmd` | 24/24 isolated checks, 4/4 interop sets, 4/4 shell smokes. Kit zip: 23,275,653 bytes, SHA-256 `88617c7c72cdc881a134c5cce406962ea2d64d55401dfb3a1526ecad074fc19c` |

Windows 7 SP1, 8.1 and 10 runtime rows remain **NOT RUN**.

## 3.3 Screenshots and DPI: what is and is not proven

- **Layout is proven at the 12 DIP work areas.** WPF lays out in DIP, so the layout result for 1024×768 at 150% is the same on a real display of that kind. The test sizes are 1024×768, 1366×768 and 1920×1080 at 100/125/150/200%, assuming a 48-DIP taskbar.
- **Rendering is proven only on the host**: 1920×1080 physical at 125%.
- **NOT RUN**: real rendering at 100/150/200% and real 1024×768 and 1366×768 displays. These need the owner's machines or a manual display change.
- 1024×768 at 200% (512×336 DIP) passes the probe, but most of the page needs scrolling. Windows normally does not offer 200% on such a display.

## 4. Pre-authentication architecture review (input for Phase 2)

Scope: everything that exists or happens before the LMK is unwrapped. No Phase 1 code changes are needed. The items below are requirements Phase 2 must meet.

| # | Topic | Phase 1 state | Requirement for Phase 2 |
|---|---|---|---|
| P-1 | Envelope binding | PNKE v1 AAD binds kind, KDF parameters, salt, accountId and installId. It binds **no** envelope revision and no username. | New envelope version (PNKE v2) that also binds the envelope ID and revision. After unlock, the account record in the encrypted DB must match accountId, canonical username, envelope ID and revision, and the account must be active. Otherwise reject before any protected data is shown. Keep the v1 reader for migration only. |
| P-2 | Password change does not revoke the old password cryptographically | The LMK stays the same when a password changes. An attacker who kept a copy of the old envelope file, plus the old password and the device secret, can still unwrap the LMK offline. Application checks (P-1) stop the normal flow, not this. | Document it in AUTHENTICATION-ARCHITECTURE and RECOVERY-POLICY. Real revocation of a known-compromised password or a removed delegate needs LMK rotation (`PRAGMA hexrekey` already exists). Recommend an owner-triggered "rotate keys" action. |
| P-3 | Pre-unlock account discovery | None yet. | Stores only what is needed to find an envelope: canonical username hash → envelope file. It is never the source of roles or permissions. It must not reveal which usernames exist to someone without the files (use the same cost and the same generic error for unknown users, P-5). |
| P-4 | Throttling store | None yet. | Must exist before unlock, so it cannot use the LMK. Store per-installation and per-username-hash failure counters with HMAC under a DPAPI-LocalMachine-protected key. Count elapsed time with a monotonic clock within a process and with stored timestamps across restarts. Treat any backward clock jump or tamper as "maximum delay", never as "reset". Document that a local administrator can delete the store, and that offline guessing against a copied envelope is limited only by Argon2id and the device secret. |
| P-5 | KDF concurrency | Argon2id is 64 MiB per attempt. | Allow one Argon2id at a time per process (a semaphore), and reject parallel logins while one is running. Otherwise a 32-bit process on a 2 GB PC can run out of memory. |
| P-6 | Pre-unlock audit | None. | Keep a bounded local journal (fixed number of records, no usernames in plaintext, only a username hash). It is HMAC-protected under the DPAPI key from P-4 and labelled "unauthenticated by LMK". After unlock, import it into the LMK-keyed audit chain. Never present it as tamper-proof. |
| P-7 | Data folder ACL and single instance | Not set. | `%ProgramData%\ParvNegar` with an explicit ACL: SYSTEM and Administrators full control; authorized Windows users modify. Exact group policy is an owner decision. Use a named mutex for one running instance per installation, and an exclusive lock file during setup, so two processes never initialize or migrate at the same time. |
| P-8 | Setup crash safety | Encrypted DB creation is fail-closed (provider proof, key check). | Write setup to a staging folder, mark completion with an atomic rename, and remove incomplete setups on the next start. Never create a new empty database when an existing installation cannot be unlocked. |
| P-9 | DPAPI LocalMachine scope | Documented (SECURITY-ARCHITECTURE §2.2). | Unchanged. It binds the data to the installation; it is not a second human factor. Any local administrator or local process can call it. |
| P-10 | Smoke and debug modes | `--smoke` shows only synthetic configuration and has no data access. | Once authentication exists, `--smoke` must not open real data folders, must not skip license or login, and must refuse to run when an initialized installation exists, unless it is pointed at a synthetic data root. Needs a test. |

## 5. Owner actions required before Phase 2

1. **Repository visibility (urgent).**
   - The repository is public. It contains proprietary source, the SRS, the Phase 0/1 documents (no student data; hygiene and history checks passed in Phase 1) and the brand kit.
   - Making it private is a repository setting change that the agent will not make without your explicit instruction. Example: `gh repo edit OAR-NOVA-Games/ParvNegar --visibility private --accept-visibility-change-consequences`.
   - Anything already public may have been cloned or cached.
2. **Review and merge approval for Phase 1.**
   - Open and review the pull request `feat/phase-01` → `main`, then approve or reject the merge.
   - After the merge, Phase 2 starts from `main` on `feat/phase-02`.
3. Confirm the school logos, and the formal display name «دبستان دخترانه شاهد شهیده شهناز کریمی», for in-app use (Phase 2D).
4. Windows 7 SP1, 8.1 and 10 machines for the kit (`artifacts/ParvNegar-Phase1-CompatKit.zip`, rebuilt for this follow-up).

## 6. Commits in this follow-up

See `git log b92e2e6..feat/phase-01`. All are conventional one-line commits on `feat/phase-01`. Nothing is merged to `main`.
