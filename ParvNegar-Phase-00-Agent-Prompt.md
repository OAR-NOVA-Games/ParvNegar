# ParvNegar — Phase 0: Engineering Discovery & Compatibility Qualification

You are the principal Windows desktop engineer for ParvNegar, an offline commercial student-records product owned by OAR NOVA Games Studio. This is Phase 0 only. Read `ParvNegar-SRS-v1.0.md` in full before taking action; it supersedes v0.1. Use the final corrected brand kit for reference. The owner approved the product baseline, product name, Windows 7 onward and x86/x64 support, with Windows 10/11 primary. No business policy beyond that approval is implied.

## 1. Workspace and safety

- Inspect the actual working directory, Git status, remotes, branches, tools and available Windows test environments. If a repository does not yet exist, report the proposed private repository (`OAR-NOVA-Games/ParvNegar`) and workspace structure; do not create a remote, push, change permissions, or assume a local path without authorization. Work in a separate local scratch directory for spikes if needed. Preserve existing changes.
- Read the supplied prototype and forms only locally if available. The source archive contains real children's personal data. Never commit, upload, paste into reports/prompts, use as fixtures, or send to third-party services any source records or identifiers. Do not add the archive, sample Word files, real exports, databases, secrets, or backups to Git. Create anonymized field maps and synthetic fixtures. Treat the supplied product title and school logo as authorized configuration assets; keep product, publisher and school identities separate.
- Do not modify YariZan or infrastructure repositories, issue real licenses, create customer accounts, deploy, publish releases, or generate a production installer. No production application implementation in this phase. No internet dependency at application installation, activation or runtime; research/build-time network access is distinct.
- All prompts, code documentation, reports and commit messages in English; all customer-facing strings Persian RTL. Stop before any destructive or externally visible action. Never fabricate test results.

## 2. Establish the actual platform matrix

- Treat Windows 7 SP1 x86/x64, Windows 8 (not 8.1) x86/x64, Windows 8.1 Update x86/x64, Windows 10 x86/x64 and Windows 11 x64 (including x86 app execution on x64) as target rows. Record exact OS editions/builds/prerequisites and distinguish Microsoft lifecycle status from app compatibility. No ARM64-native requirement. Do not silently drop Windows 8, old Windows 10 builds or x86.
- Investigate a shared WPF codebase with .NET 10 LTS modern host and .NET Framework 4.8 legacy candidate; assess multi-targeting before splitting hosts. Windows 8 maximum Framework 4.6.1 makes it a separate feasibility gate. Compare alternative architectures where necessary. Determine exact runtime/SDK, C# language/API floor, package TFM and native binary requirements; do not use obsolete EF Core 3.1 or silently weaken crypto to satisfy the legacy target.
- Consult official vendor docs, package metadata, native dependency support and redistribution licenses. Record URLs, versions and date verified. Provide a maintained dependency matrix for UI, SQLite/encrypted provider, DB access/migrations, password KDF, AEAD, signatures, DPAPI/key protection, PDF/rendering, safe document preview and installers. Include offline installers/prerequisites, SHA-256 checksums/signing verification, x86 memory considerations and security maintenance strategy. Do not pick a library based only on a compatible TFM.
- If a required legacy platform cannot be qualified securely, report the exact failed condition and viable options; do not claim it works or change the scope unilaterally.

## 3. Threat model and architecture options

Define boundaries for a local application, Windows identities, local admins, device copying, stolen disk/backup, hostile attachments, failed migration and crash recovery. Include offline signed license issuance, owner provisioning/recovery, delegated authorization, tamper limitations, authenticated encryption, key separation, and portable recovery without cloning licenses. No shared/default passwords, hidden recovery bypasses, embedded signing private keys, readable attachment vault, or plaintext database/sidecars. Explain why DPAPI alone does not isolate processes running as the same OS user. Define explicit safe preview/export boundaries and no Explorer access to decrypted files. Use maintained, vetted primitives; no custom cryptography. Keep the same security guarantees across editions. Model versioned storage and backup compatibility across x86/x64 and Modern/Legacy; restoration requires a valid license. Record decisions as candidate ADRs, not final policies.

## 4. Isolated proofs of concept (not production code)

Where actual tools permit, build disposable minimal WPF shells and small isolated spikes using synthetic data. Verify Persian RTL text/font/DPI and UI on low-resource hardware; native encrypted SQLite create/write/reopen/wrong-key failure and check DB/WAL/journal/temp files for plaintext; crypto known-answer/tamper tests; offline signature verification and portable encrypted backup round-trip; PDF Persian shaping/print preview without Office or network; safe attachment rendering feasibility; x86/x64 native load; offline installer prerequisite strategy. Test actual supported VMs where available, not compatibility mode. A compile-only spike is not runtime proof. Label each check PASS, FAIL, BLOCKED or NOT RUN, include commands, exit codes, environment and sanitized evidence. Do not attempt to hide missing Windows VMs. Do not ship scratch keys or artifacts as production code.

## 5. Product and data discovery

Produce an anonymized source field map for the profile, educational and talent forms and logical folder categories. Keep the exact home title from SRS section 1. Map field IDs, type, requiredness (mark unknown), annual vs stable identity, print/export placement and source reference. Preserve the source's two assessment periods; mark proposed third Farvardin–Khordad and linkage as pending O-02 rather than implementing. Define grade promotion invariants and history snapshots. Prepare a decision register for O-02 through O-10 and unresolved O-01 details. Separate confirmed requirements, recommended defaults and owner approvals still needed. No invention of eligibility criteria, assessment scores, license expiry, or recovery policy.

## 6. Required output

Create or propose the following sanitized engineering documents under `docs/phase-00/` (local only, if workspace permissions allow):

- `DISCOVERY-REPORT.md` — executive summary, workspace status, actual findings, chosen recommendation vs alternatives, blockers, evidence and next approval gate.
- `PLATFORM-MATRIX.md` — per OS/build/process architecture, vendor support status, runtime and prerequisites, tests and exact status.
- `DEPENDENCY-MATRIX.md` — packages, TFM/native support, licenses, security maintenance, references, fallback options.
- `ARCHITECTURE-PROPOSAL.md` — solution structure, boundaries, ADR candidates, cross-target data and backup contracts, build/installer and CI strategy.
- `THREAT-MODEL.md` — assets, actors, trust boundaries, threats, mitigations and residual risk per target.
- `SOURCE-FIELD-MAP.md` — anonymized source mapping; mark unavailable files explicitly.
- `DECISION-REGISTER.md` — requirement IDs, proposed options, impact, owner decision needed and phase gate.
- `TEST-EVIDENCE.md` — reproducible commands, actual logs/results and untested matrix rows, with no real data.
- `PHASE-01-PLAN.md` — proposed implementation scope, tasks, acceptance mapping, risks and estimated dependencies (no promise of timing).

Recommend a private repo layout and test/CI strategy with actual Windows VM/hardware gates; do not spend CI minutes or change remotes without permission. At the end, provide an English report listing file paths, changes, tests actually executed, failures, outstanding decisions and the exact recommended Phase 1 scope. Stop. Do not start Phase 1 until the owner approves the report and applicable decisions.

## Acceptance gate

Phase 0 is complete for review when every target and dependency has a documented evidence status, the secure architecture alternatives and blockers are explicit, the source mapping is sanitized, and the next phase is scoped. Phase 0 report acceptance is not proof that all platform targets pass. Any unresolved mandatory compatibility/security blocker prevents declaring that target qualified and must have an owner-approved resolution before release.
