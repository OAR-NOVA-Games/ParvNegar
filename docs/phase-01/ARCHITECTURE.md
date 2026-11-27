# ParvNegar — Architecture (Phase 1 baseline)

Date: 2026-11-26. Implements PLAT-02 and NFR-01. Decisions are recorded as ADRs in [`adr/`](adr/).

## 1. Solution

```
ParvNegar.sln
├─ src/ParvNegar.Domain        netstandard2.0   entities, invariants, promotion planner, calendar, Persian search keys
├─ src/ParvNegar.Application   netstandard2.0   ports: ISchoolProfileSource, IReportRenderer, IDocumentPreviewer; ProductIdentity
├─ src/ParvNegar.Security      netstandard2.0   CryptoRandom, HKDF, Argon2id (bounded), AES-256-GCM, ChunkedAead, LocalMasterKey,
│                                                KeyEnvelope (PNKE), DeviceSecret + DPAPI LocalMachine protector
├─ src/ParvNegar.Storage       netstandard2.0   EncryptedDatabase (SQLite3MC), MigrationRunner + Migrations/*.sql, BackupArchive (PNBK)
├─ src/ParvNegar.App           net10.0-windows;net48 (x64, x86)   WPF shell: App, MainWindow, AboutWindow, MainViewModel, Themes, Strings
├─ tests/ParvNegar.*.Tests     net10.0-windows;net48 (x64, x86)   xUnit 2.9.3; Shared/TestEnvironment asserts process architecture
└─ tools/ParvNegar.DevTool     net10.0-windows;net48 (x64, x86)   interop-produce / interop-consume / compat (isolated checks)
```

Dependency direction: App → Application → Domain. Storage → Security. The Phase 1 shell does not reference Storage or Security yet: the home screen needs no data and no keys. It stays that way until authentication exists (Phase 2), so no student data can appear before login and licensing.

Platform-specific code is limited to:

- WPF hosting and P/Invoke in the App (smoke capture, memory counters);
- DPAPI in `DpapiLocalMachineProtector` (Windows-only API through the `System.Security.Cryptography.ProtectedData` package);
- the native `sqlite3mc.dll` per RID (`runtimes/win-x86|x64/native`).

Everything else is shared source compiled once (netstandard2.0) and consumed by both runtimes.

## 2. Shared vs edition-specific

| Concern | Shared | Edition-specific |
|---|---|---|
| Domain rules, calendar, promotion | Yes (netstandard2.0) | — |
| Crypto formats | Yes (BouncyCastle on both runtimes) | — |
| Database access | Yes (Microsoft.Data.Sqlite.Core 10.0.15 on netstandard2.0) | Native DLL per architecture |
| Views, styles, strings, brand assets | Yes (same XAML for net48 and net10) | — |
| DPI | Same manifest (PerMonitorV2 with PerMonitor fallback) | .NET Framework 4.8 vs .NET 10 WPF runtime behaviour |
| Startup host | Same `App` class | Framework-dependent: .NET Framework 4.8 vs .NET 10 Desktop Runtime |

## 3. Optional capabilities (PDF, preview)

`IReportRenderer` and `IDocumentPreviewer` are ports with `Unavailable*` defaults. No PDF or preview package (Skia, HarfBuzz, QuestPDF, PDFium) is referenced in Phase 1. A test (`Report_and_preview_ports_default_to_unavailable_without_native_dependencies`) asserts that none of these assemblies loads. When an engine is approved (D-06/L-01), it is loaded lazily behind the port, so a native load failure on an old OS disables that feature with a Persian message and never blocks startup.

## 4. Data contracts across editions

See STORAGE-FORMAT.md. The contract is proven by the cross-edition matrix: each build creates a data set, and every build opens it (16 pairs). Covered: the database file, an attachment-style AEAD stream, the backup archive, and the negative cases.

## 5. Calendar representation

Academic years are keyed by their Solar Hijri start year (e.g. 1405). Boundaries are computed with `System.Globalization.PersianCalendar` (available on both runtimes): start = 1 Mehr, end = last day of Khordad of the next year (31 Khordad). Stored dates are Gregorian `yyyy-MM-dd`; the UI shows Solar Hijri with Persian digits. Tests pin 1403, 1404 and 1405 boundaries against independently computed dates.

## 6. Build, packaging and CI

- SDK pinned by `global.json` (10.0.401, latestPatch). Central package versions are exact (`[x.y.z]`), and lock files are committed for every project. `build.ps1 -LockedRestore` fails on any drift. All projects declare `RuntimeIdentifiers=win-x64;win-x86`, so lock files do not depend on how restore is triggered.
- `TreatWarningsAsErrors=true`, deterministic builds, NuGet audit on.
- Builds and tests run sequentially, one combination at a time (`-m:1`, no node reuse), because this workstation has limited free memory.
- Packaging: framework-dependent publish for the compatibility kit (`tools/Build-CompatKit.ps1`). The installer (Inno Setup, offline prerequisites, signing) is Phase 7.
- CI: not enabled. Proposed workflow (Windows runner: locked restore, build ×4, test ×4, hygiene, native import check, vulnerability scan) awaits owner approval of runner minutes. Hosted runners can never qualify Windows 7/8.1/10. Those rows need the compatibility kit on real machines.
