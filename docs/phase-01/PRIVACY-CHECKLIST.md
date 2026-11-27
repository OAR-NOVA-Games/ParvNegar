# Phase 1 — Privacy Checklist

Date: 2026-11-26. Check performed before each commit with `tools/check-repo-hygiene.ps1` (final run: `HYGIENE_OK`).

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 | No real student records, names, national IDs, phone numbers or photos in the repository | PASS | All fixtures are invented. National IDs come from `Fixtures.InvalidCheckDigitNationalId` (they fail the Iranian check digit) or the documented patterns `0000000011` / `0012345678`. The hygiene script flags other 10-digit numbers in tests/tools |
| 2 | Source archive, sample Word/PowerPoint files and school logo not committed | PASS | `.gitignore` blocks `*.rar *.zip *.doc* *.ppt* *.xls*`; hygiene script blocks those types; the school logo is not in the repo |
| 3 | Extracted archive copies deleted | PASS (Phase 0) | Phase 0 TEST-EVIDENCE §6 |
| 4 | No databases, WAL/journal files or backups committed | PASS | `.gitignore` and hygiene block `*.db* *.sqlite* *.pnbk *.bak`; test databases live under `%TEMP%\parvnegar-tests` and are deleted |
| 5 | No secrets, private keys or certificates | PASS | Hygiene blocks `*.pfx *.p12 *.snk *.pem *.key *.cer *.env`, `secrets/`, and `BEGIN … PRIVATE KEY`. The only key literal is the publicly known DevTool scratch LMK, labeled test-only |
| 6 | Keys never in connection strings, logs or exceptions | PASS | `EncryptedDatabase` sends keys by pragma only; test asserts connection string has no password; exceptions are generic |
| 7 | National ID never printed in full by `ToString()` | PASS | Domain test `National_id_is_never_printed_in_full` |
| 8 | Screenshots contain synthetic configuration only | PASS | The shell shows only the approved school title and school name (configuration, not personal data) and no student data |
| 9 | Personal workstation paths and user names not in committed docs | PASS | Phase 0 docs scrubbed to `<workspace>`; hygiene flags references to personal folders |
| 10 | Test outputs (TRX, interop data sets, kit results) not committed | PASS | Written under `artifacts/` (ignored) and `%TEMP%` |
| 11 | No network access at application runtime | PASS (by construction) | No HTTP client or network API in src; no telemetry packages |
| 12 | School identity separate from product and publisher | PASS | `SchoolProfile` (configuration) vs `ProductIdentity` constants; footer and About show them separately |
| 13 | Kit results returned from other machines contain only OS info and synthetic data | PASS (by design) | README.txt in the kit; the results folder name includes the PC name, so it is not committed |

Open privacy items for later phases: audit-log content minimization (Phase 6), export warnings (Phase 6), crash-dump policy (Phase 7), and the official school name text and school logo, which are owner-provided configuration.
