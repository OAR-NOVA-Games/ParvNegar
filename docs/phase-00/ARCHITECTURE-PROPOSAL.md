# ParvNegar Phase 0 — Architecture Proposal

Date: 2026-11-26. SRS: v1.0 (NFR-01, PLAT-02/03). Status: proposal. ADRs below are **candidates**, not approved policy.

## 1. Recommendation in one paragraph

Build one WPF/MVVM product from one repository. The presentation project and all libraries multi-target `net10.0-windows` (Modern edition: Windows 10 1607+ and 11) and `net48` (Legacy edition: Windows 7 SP1 and 8.1 Update). Data access uses Microsoft.Data.Sqlite **10.0.x** (the last line that targets netstandard2.0) over SQLite3 Multiple Ciphers (MIT) with the ChaCha20-Poly1305 page cipher, plain SQL plus Dapper, and hand-written, versioned SQL migrations. Cryptography uses BouncyCastle.Cryptography (MIT) for Argon2id, AES-256-GCM, HKDF and Ed25519 on both targets, so formats are byte-identical across editions. Key protection combines DPAPI with password-derived wraps. PDF output candidates are SkiaSharp + HarfBuzzSharp (MIT) with a bidi step and the embedded Vazirmatn font (OFL), or QuestPDF subject to license eligibility. Safe preview uses PDFium (in-memory) and WIC on Modern. The current Skia/PDFium native builds are not expected to load on Windows 7, so the Legacy PDF and preview route is open decision D-06. Installer: Inno Setup, separate x86 and x64 packages per edition, offline prerequisites. Windows 8 (not 8.1) is a blocked target pending owner decision D-01.

The spike in TEST-EVIDENCE built this whole stack from **one source file** for net10.0-windows and net48, x86 and x64, and ran it on the Windows 11 host.

## 2. Alternatives compared

| Option | Description | Pros | Cons | Verdict |
|---|---|---|---|---|
| A. Multi-target WPF (recommended) | net10.0-windows + net48 from one codebase | One XAML set, one domain, one storage library; spike proves it builds and runs | Must avoid APIs missing on net48 (Span-heavy code, `AesGcm`, `Convert.FromHexString`); C# features limited to those that compile down (LangVersion latest works with polyfills) | **Chosen** |
| B. net48 only for all OSes | One runtime | Simplest packaging | .NET Framework gets security fixes only via Windows Update; Windows 10 1607/1511 LTSC; misses modern runtime performance and supported servicing for Windows 11; SRS asks for .NET 10 LTS modern host | Fallback only |
| C. Two separate hosts | Modern host + Legacy host, shared libs | Clean per-host dependencies | Duplicate XAML/wiring; drift risk | Use only if A hits a blocker |
| D. EF Core | Modern ORM | Productivity | EF Core 3.1 is the last version for .NET Framework, out of support; SRS forbids it | Rejected |
| E. Electron/WebView2 | Web UI | — | WebView2 needs Win10 for current runtime; network-free bundles heavy; RTL fine but violates "native" direction | Rejected |
| F. Avalonia | Cross-platform XAML | Modern | .NET Framework support limited; SRS fixes WPF | Rejected |
| G. Windows 8 net461 edition | Third target | Covers Windows 8 natively | Retired runtime, no Skia/PDF, extra QA row | Owner decision D-01 |

## 3. Solution structure (proposed)

```
ParvNegar/
├─ ParvNegar.sln
├─ Directory.Build.props            # TargetFrameworks, LangVersion, nullable, warnings-as-errors, deterministic
├─ Directory.Packages.props         # Central package versions (pinned, reviewed)
├─ global.json                      # SDK 10.0.x pin
├─ src/
│  ├─ ParvNegar.Domain/             # netstandard2.0: entities, invariants, promotion rules, no I/O
│  ├─ ParvNegar.Application/        # netstandard2.0: use cases, permissions, DTOs, ports (interfaces)
│  ├─ ParvNegar.Security/           # net48;net10.0-windows: KDF, AEAD, HKDF, signatures, key hierarchy, DPAPI
│  ├─ ParvNegar.Storage/            # net48;net10.0-windows: SQLite3MC + Dapper, migrations, vault, backup format
│  ├─ ParvNegar.Licensing/          # netstandard2.0 + platform fingerprint adapter; verify only
│  ├─ ParvNegar.Reporting/          # net48;net10.0-windows: PDF (Skia/HarfBuzz), print (WPF XPS)
│  ├─ ParvNegar.Preview/            # PDFium/WIC adapters, in-memory only
│  ├─ ParvNegar.Presentation/       # WPF views, view models, fa-IR resources, RTL styles, brand assets
│  └─ ParvNegar.App/                # WPF host exe, DI composition, app.manifest (supportedOS, DPI)
├─ tools/
│  └─ ParvNegar.Issuer/             # publisher-only signing/provisioning tool; NOT in installer; keys outside repo
├─ tests/
│  ├─ ParvNegar.Domain.Tests/       # multi-target test runs on net48 and net10
│  ├─ ParvNegar.Storage.Tests/      # encryption, plaintext scans, migrations, backup failure modes
│  ├─ ParvNegar.Security.Tests/     # KATs, tamper, key separation
│  ├─ ParvNegar.Compat.Smoke/       # the PLAT-04 smoke exe (successor of the Phase 0 spike)
│  └─ fixtures/synthetic/           # generated synthetic data only
├─ installer/                       # Inno Setup scripts per edition × arch
├─ docs/                            # phase reports, ADRs, matrices
└─ .github/workflows/               # see §8 (not enabled without approval)
```

Dependency direction: App → Presentation → Application → Domain. Infrastructure projects implement Application ports. Domain and Application never reference WPF, SQLite or crypto.

## 4. ADR candidates

| ADR | Title | Decision (candidate) | Evidence |
|---|---|---|---|
| ADR-C01 | Runtime split | Multi-target net10.0-windows + net48; Windows 8 pending D-01 | Spike build/run (TEST-EVIDENCE §3) |
| ADR-C02 | Encrypted storage | SQLite3MC via `SQLite3MC.PCLRaw.bundle` 2.7.1 + Microsoft.Data.Sqlite.Core 10.0.x; cipher `chacha20` (sqleet, default); `journal_mode=WAL`, `temp_store=MEMORY`, `secure_delete=ON`; key via `PRAGMA key` on open | Spike SQLITE-MC-* |
| ADR-C03 | Data access and migrations | Dapper + raw SQL; numbered `NNNN_name.sql` migrations embedded as resources, SHA-256 recorded in `schema_migrations`; backup before migrate | Pattern from YariZan backend |
| ADR-C04 | Key hierarchy | LMK + HKDF subkeys; wraps by Argon2id(password) per account, DPAPI, owner recovery key (THREAT-MODEL §4) | Spike KATs + DPAPI |
| ADR-C05 | Crypto library | BouncyCastle.Cryptography 2.7.0 for shared formats on both targets; platform CNG only where identical output is guaranteed (ECDSA, SHA-2) | Spike KATs on all targets |
| ADR-C06 | License signature | Canonical JSON envelope, ECDSA P-256 P1363 (same profile as YariZan V2) or Ed25519; public key embedded; issuer tool separate | Spike ECDSA/Ed25519 |
| ADR-C07 | Attachment vault | Per-file random DEK, AES-256-GCM 64 KiB chunks, nonce = file-nonce-prefix‖counter, AAD = header hash‖index‖final; opaque blob IDs; staged commit | Spike backup format reuses the chunk scheme |
| ADR-C08 | Backup format | `PNBK` v1: header (magic, format, schema version, Argon2id params, salt, wrapped BAK), encrypted manifest, chunked entries; temp file then rename; staged restore then directory switch | Spike BACKUP-ROUNDTRIP |
| ADR-C09 | PDF | **Open (D-06).** One engine for all editions preferred: SkiaSharp + HarfBuzzSharp + a Unicode bidi step, Vazirmatn embedded. QuestPDF is visually correct but its x86 native fails Windows 7 statically and needs L-01. Current Skia 4.x natives are not expected to load on Windows 7. WPF XPS print is the proven fallback on every build. | Spike PDF-*, TEST-EVIDENCE §3.4, §4 |
| ADR-C10 | Preview | PDFium from memory on Modern (proven on host). PDFium 160 cannot load on Windows 7/8.1 (subsystem 10.0): Legacy needs an older PDFium build or no PDF preview (D-06). Images via WIC, text via WPF on all editions. Office formats not previewed in v1. | Spike PDFIUM-RENDER, WPF-IMAGE-DECODE |
| ADR-C11 | Calendar | Store `DATE` as ISO-8601 Gregorian (`yyyy-MM-dd`) and timestamps as UTC ticks; present Solar Hijri via `System.Globalization.PersianCalendar` (available on net48 and net10); academic year = (start SH year), months Mehr..Khordad | Built into both runtimes |
| ADR-C12 | Persian text normalization | Search keys normalize Arabic Yeh/Kaf to Persian, Persian/Arabic digits to ASCII, remove ZWNJ/tatweel for matching only; stored display text unchanged | Source shows mixed Yeh (SOURCE-FIELD-MAP) |
| ADR-C13 | Installer | Inno Setup 6.x (Windows 7 minimum since 6.3.0); four packages: Modern x64, Modern x86, Legacy x64, Legacy x86 (or one Legacy x86 package with both payloads); offline prerequisites bundled with SHA-256 checks | Vendor docs; YariZan uses Inno |
| ADR-C14 | Font | Vazirmatn v33.003 (SIL OFL 1.1), bundled and embedded in PDFs; no reliance on installed fonts | Spike RTL/PDF |

## 5. Cross-target data contracts (PLAT-03)

| Contract | Rule |
|---|---|
| Database file | Same SQLite3MC cipher and parameters on both editions and both architectures. `PRAGMA user_version` + `schema_migrations` table. Little-endian, page size 4096. |
| Key derivation params | Stored with each wrap (algorithm ID, m, t, p, salt). Upper bound m = 64 MiB so an x86 process on a 2 GB PC can open any wrap. |
| Attachments | Blob format version byte; same on all editions. |
| Backup | `PNBK` format version + min reader version; schema version inside manifest. A newer backup on an older app is refused with a clear message. Cross-edition restore (Modern ↔ Legacy, x86 ↔ x64) is a mandatory test row. |
| License | Separate from data. Not in backup. Edition field in license allows Modern/Legacy of the same SKU. |
| Text | UTF-8 everywhere; normalization only in search keys. |

## 6. Build and installer strategy

- SDK: .NET SDK 10.0.x (host has 10.0.401). .NET Framework 4.8 reference assemblies from the targeting pack (present on host).
- Modern edition: framework-dependent or self-contained `win-x64` / `win-x86`. Recommendation: **self-contained** for offline install (no runtime prerequisite), at the cost of size. Alternative: bundle the .NET Desktop Runtime 10 offline installer.
- Legacy edition: framework-dependent on .NET Framework 4.8. Bundle the offline installer `ndp48-x86-x64-allos-enu.exe` and verify its Authenticode signature and SHA-256 before running. Windows 7 SP1 machines without SHA-2 code-signing support (KB4474419, with servicing stack KB4490628) are expected to reject SHA-256-only signed binaries and installers. This is **not verified** in Phase 0: the Microsoft pages read do not list Windows 7 prerequisites. Verify on a real, unpatched Windows 7 SP1 machine.
- Native DLLs: `runtimes/win-x86/native` and `win-x64/native` copied per RID. Each native DLL is checked by a build step for imports (no `api-ms-win-*` sets absent on Windows 7, no `VCRUNTIME140` unless the redistributable strategy is approved).
- Signing: Authenticode SHA-256 for exe/dll/installer (L-05), timestamped.
- Deterministic builds, SBOM (CycloneDX) per release, `dotnet list package --vulnerable` gate.

## 7. Test strategy

| Level | Content | Where |
|---|---|---|
| Unit | Domain invariants, promotion idempotency, permission checks, normalization, Solar Hijri mapping | Both TFMs on build agent |
| Security | KATs, tamper, wrong key, nonce uniqueness, plaintext canary scans of DB/WAL/journal/temp/vault | Both TFMs, x86+x64 |
| Integration | Migrations up from each previous schema, backup/restore failure modes (wrong pass, flip, truncate, disk full), crash-in-import recovery | Both TFMs |
| Compat smoke | `ParvNegar.Compat.Smoke.exe` (successor of the spike) writes `results.tsv`; run on each matrix row | Real Windows VMs/hardware |
| UI | Persian RTL, DPI 100/125/150/200%, keyboard navigation; screenshots with synthetic data | Real Windows |
| Print | Physical A4 print + PDF text extraction | Manual gate |

## 8. CI proposal (not enabled; needs approval)

- Repository: private `OAR-NOVA-Games/ParvNegar` (remote already configured in local clone; nothing pushed in Phase 0).
- Note from YariZan: the OAR-NOVA-Games organization's Actions minutes were blocked/limited by billing. CI minutes are an owner decision.
- Proposed jobs (windows-latest): restore with lock files → build net48+net10 × x86+x64 → unit/security tests on both TFMs (x86 tests via `dotnet test --arch x86` / vstest x86) → native import check → package artifacts (unsigned). Signing and installer build happen only on the release machine.
- Hosted runners are Windows Server 2022/2025 only. **They do not prove Windows 7/8.1/10 behaviour.** Release gates require the Compat smoke on real VMs/hardware: Windows 7 SP1 x86/x64, 8.1 x86/x64, 10 (1607 LTSB or 22H2) x86/x64, 11 x64.
- No real student data in CI, ever; fixtures are generated.
