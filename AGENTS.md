# Instructions for implementation agents (Codex, Claude, others)

Agents implement; the owner decides. These rules apply to every session in this repository.

## Work rules

1. Work phase by phase. Start a phase only after the owner approves its scope. Stop at each review gate.
2. Read the SRS (`docs/requirements/ParvNegar-SRS-v1.0.md`), the latest phase report and `docs/phase-01/DECISION-REGISTER.md` before changing code.
3. Never treat an Open or Proposed item as approved. Record new owner decisions in the decision register with their date.
4. English for code, comments, docs, commits and PRs. Persian RTL for every customer-facing string (keep them in `Strings`).
5. Commit messages: one conventional subject line (`feat: ...`, `fix: ...`, `test: ...`, `docs: ...`, `chore: ...`).
6. Feature branches only. No direct push to `main`, no force-push, no merge without owner approval.
7. Do not modify YariZan or infrastructure repositories. Do not deploy, publish releases, issue licenses or create real credentials.

## Data and secrets

- Never commit, log, upload or paste real student data, the source archive, sample Word files, real exports, databases, backups, keys or secrets.
- Fixtures are synthetic. National IDs in tests must come from `Fixtures.InvalidCheckDigitNationalId` (they fail the check digit, so they cannot be real).
- The school home title is configuration and must stay byte-identical to SRS §1.
- Run `tools/check-repo-hygiene.ps1` before every commit.

## Engineering gates

- Build and test all four combinations (net10/net48 × x64/x86) with `build.ps1` and `test.ps1`. Tests assert the real process architecture.
- Run `tools/Run-CrossEdition.ps1` after any change to storage, crypto or backup formats.
- Any format change needs a version bump, a migration or reader rule, and an update to `docs/phase-01/STORAGE-FORMAT.md`.
- No new cryptographic algorithm or construction. Use the primitives in `ParvNegar.Security`.
- No PDF/preview native dependency may become required at application startup.
- Never report Windows 7/8.1/10 as PASS without runtime evidence from that OS. WOW64 on Windows 11 does not qualify a 32-bit OS.
- Never weaken an assertion or exclude a failing test to make a gate pass. Report the failure.
