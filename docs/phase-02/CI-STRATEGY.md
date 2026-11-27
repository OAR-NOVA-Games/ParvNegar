# CI strategy (selective GitHub Actions)

Owner-approved: selective CI with controlled minute use. Files: `.github/workflows/ci.yml`, `tools/ci/Select-CiJobs.ps1`, `tools/ci/Test-CiMapping.ps1`.

## 1. Triggers and coverage

| Event | Covered | Gate |
|---|---|---|
| Pull request to `main` | Yes | Classified (§2) |
| Push to `main`, including merges and any direct push | Yes | Classified against `github.event.before`. A new branch or unknown base runs the full gate |
| Manual "Run workflow" (`workflow_dispatch`) | Yes | **Always full** |
| Pushes to feature branches without a PR | No (saves minutes) | They run when the PR is opened |

## 2. Path classification

- **Documentation only** (hygiene gate only): `docs/**`, root `*.md`, `assets/brand-kit/00-Readme/**`, `assets/publisher/README.md`, `LICENSE`.
- **Everything else** runs the **full gate**. That includes `src`, `tests`, `tools` (issuer, DevTool, scripts, CI scripts, kit), assets, build files (`*.props`, `*.targets`, `*.sln`, `global.json`), lock files, `.gitignore`, `.gitattributes`, the workflow itself, **and any unknown or unmapped path**.
- **Conservative fallbacks:** mixed changes, empty change lists and unknown change lists all run the full gate.
- **Mapping tests:** `Test-CiMapping.ps1` holds 33 cases and runs first in every CI run. Local result: `CI_MAPPING_OK cases=33`.

## 3. Jobs

| Job | Runner | Timeout | Runs | Steps |
|---|---|---|---|---|
| `classify` | ubuntu-latest | 5 min | Always | Mapping self-test; decision |
| `hygiene` | windows-latest | 10 min | Always | `check-repo-hygiene.ps1` (secrets, data, national-ID-like numbers, password literals, forbidden files); `check-issuer-isolation.ps1` |
| `full-gate` | windows-latest | 75 min | When `full == true` | setup-dotnet from `global.json` with the NuGet cache keyed by `**/packages.lock.json`; x86 .NET 10 runtimes; `build.ps1 -LockedRestore` (4 combinations); `test.ps1` (4 combinations, architecture asserted); issuer build; `Run-CrossEdition.ps1` (storage, backup, licensing); isolation with outputs; vulnerability audit; native import analysis; test results uploaded (7 days) |
| `ci-result` | ubuntu-latest | 3 min | Always | **The single required check.** Passes only if classify and hygiene succeeded, and the full gate succeeded when required (or was skipped when not required) |

- **Concurrency:** `ci-<PR number or ref>`. Superseded **pull-request** runs are cancelled; pushes to `main` are never cancelled.
- **Permissions:** `contents: read`.
- **Secrets:** none used. Test signing keys are generated inside the run and deleted.

## 4. Cost considerations

- The repository is currently **public**, so GitHub-hosted standard runners are free for public repositories. If it becomes private, Windows minutes count double against the plan.
- **Estimated duration:**
  - documentation PR: about 2–3 runner minutes (classify plus one short Windows job);
  - full gate: about 25–45 Windows minutes, based on the local timings (build 82 s, tests 614 s, cross-edition about 2 min) and a slower runner.
- No large runners, no matrices that multiply jobs, and no scheduled runs.

## 5. Not covered by CI (by design; run locally before a merge request)

| Item | Reason | Local command |
|---|---|---|
| UI evidence flow in Light and Dark | Needs an interactive desktop that no other window covers (privacy guard) | `tools/Run-UiSmoke.ps1` |
| Shell smoke on real windows | Same | `ParvNegar.exe --smoke <dir>` per build |
| Compatibility kit | Needs real Windows 7, 8.1 and 10 machines | `tools/Build-CompatKit.ps1`, then `RUN-COMPAT.cmd` |

GitHub-hosted Windows runners do **not** qualify Windows 7, 8.1 or 10.

## 6. Execution evidence

At the time of this report the workflow has **not run on GitHub yet**: it triggers on PRs and pushes to `main`, and the Phase 2 PR is not open. The mapping logic was executed locally (33/33). The first real run happens when the Phase 2 PR is opened. Its result must be checked before merge; the YAML syntax is validated by GitHub on that run.
