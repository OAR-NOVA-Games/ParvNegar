# ParvNegar Phase 0 — Dependency Matrix

Date verified: 2026-11-26. Versions come from the NuGet v3 API (`api.nuget.org/v3-flatcontainer/<id>/index.json` and `.nuspec`) on that date. Vendor statements come from the URLs listed in §6. A package is not chosen only because its TFM fits: runtime evidence is in TEST-EVIDENCE.

Legend for evidence: **R** = ran in the Phase 0 spike on Windows 11 x64 host (x64 and x86 processes), **B** = build only, **D** = documentation only.

## 1. Managed packages

| Area | Package | Version | TFMs in package | License | Native parts | Evidence | Maintenance / risk | Fallback |
|---|---|---|---|---|---|---|---|---|
| UI | WPF (in-box) | net10.0-windows / .NET Framework 4.8 | — | MIT / .NET Framework EULA | — | R | Serviced with runtime | — |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 | netstandard2.0/2.1, net8.0 | MIT | No | D | Active (.NET Foundation) | Hand-written `INotifyPropertyChanged` |
| ADO.NET SQLite | Microsoft.Data.Sqlite.Core | **10.0.15** (pin to 10.0.x) | netstandard2.0, net8.0 | MIT | via SQLitePCLRaw | R | 10.0 line serviced with .NET 10 LTS. **11.0.0 targets net10.0 only** (no netstandard2.0), so the Legacy edition cannot move to 11.x | Own thin wrapper over SQLitePCLRaw |
| SQLite provider | SQLite3MC.PCLRaw.bundle (+ .lib, .provider) | 2.7.1 | netstandard2.0 | MIT | `e_sqlite3mc.dll` win-x86/x64 (see §4) | R | Single maintainer (utelle); active; tracks SQLite releases | Build SQLite3MC from source; commercial SQLCipher/SEE |
| SQLite core binding | SQLitePCLRaw.core | resolved 3.0.2 (from SQLite3MC) vs 2.1.x expected by M.D.Sqlite 10 | netstandard2.0, net8.0 | Apache-2.0 | No | R (see TEST-EVIDENCE) | v3 author stopped free encryption builds | Pin compatible pair |
| (rejected) | SQLitePCLRaw.bundle_e_sqlcipher | 2.1.11 (last) | net461, netstandard2.0 | Apache-2.0 | yes | D | **Deprecated** by its author in v3 | — |
| (rejected) | System.Data.SQLite | 2.0.4 | net471, netstandard2.0/2.1 | Public domain | yes | D | Encryption needs commercial SEE | — |
| (rejected) | EF Core | 3.1 last for .NET Framework | — | MIT | — | D | Out of support; SRS forbids | — |
| Data mapping | Dapper | 2.1.100 | net461, netstandard2.0, net8.0, net10.0 | Apache-2.0 | No | D | Active | Manual `DbDataReader` mapping |
| Migrations | Own SQL runner (embedded `.sql`, SHA-256 checksums) | — | — | own | No | D | Full control on both TFMs | DbUp (dbup-sqlite 6.0.4) |
| Password KDF | BouncyCastle.Cryptography `Argon2BytesGenerator` (Argon2id) | 2.7.0 | net461, netstandard2.0, net6.0 | MIT | No (pure managed) | R (RFC 9106 vector) | Active, Legion of the Bouncy Castle | Konscious.Security.Cryptography.Argon2 1.3.1 (MIT); PBKDF2-SHA256 (platform) |
| AEAD | BouncyCastle `GcmBlockCipher` (AES-256-GCM) on both TFMs; platform `AesGcm` cross-check on net10 | 2.7.0 | as above | MIT | No | R (GCM test case 16) | as above | Windows CNG BCrypt AES-GCM via P/Invoke (Windows 7+) |
| KDF for subkeys | BouncyCastle `HkdfBytesGenerator` | 2.7.0 | as above | MIT | No | D | as above | Platform HKDF (net10 only) |
| Signatures | ECDSA P-256 (platform CNG, `ECDsa.Create`) and/or Ed25519 (BouncyCastle) | in-box / 2.7.0 | — | — | No | R (sign/verify/tamper; RFC 8032 vector) | Platform serviced by OS/runtime | Either one |
| Key protection | DPAPI: in-box `System.Security` (net48); `System.Security.Cryptography.ProtectedData` 10.0.0 (net10) | 10.0.0 (11.0.0 exists) | net462, netstandard2.0, net10.0 | MIT | No (crypt32) | R | Serviced | — |
| PDF (primary candidate) | SkiaSharp + SkiaSharp.HarfBuzz + HarfBuzzSharp | 4.157.0 / 4.157.0 / 14.4.0.200 | net462, net48, netstandard2.0, net10.0 … | MIT | `libSkiaSharp.dll`, `libHarfBuzzSharp.dll` | R | Active (.NET Foundation / Microsoft) | WPF XPS print path |
| PDF (alternative) | QuestPDF | 2026.9.2 | netstandard2.0, net5–10 | Community license (< USD 1M revenue) / Professional / Enterprise; source-available | `questpdf_skia.dll`, `questpdf_qpdf.dll` win-x86/x64 | R | Active; commercial | SkiaSharp direct |
| (rejected) | PDFsharp | 6.5.0 | netstandard2.0, net8–10 | **AGPL-3.0-only** (per nuspec) | — | D | License incompatible with closed commercial product | — |
| Safe PDF preview | bblanchon.PDFium.Win32 | 160.0.8179 | netstandard2.0 + native | Apache-2.0 (package) / PDFium BSD-style | `pdfium.dll` win-x86/x64 | R | Tracks Chromium PDFium weekly | PDFtoImage 5.4.0 (wraps same); "no preview" for PDFs |
| Image preview | WIC via WPF `BitmapDecoder` | in-box | — | — | OS codecs | R | Serviced by OS | — |
| Office preview | none in v1 | — | — | — | — | — | No safe in-process renderer without Office; owner decision O-05 | Stored only, explicit audited export |
| Font | Vazirmatn | v33.003 (`vazirmatn-v33.003.zip` SHA-256 `0a9afd41967e6f57096a56a181a23f81a2b999b62f1f2a4e4b26736580854fdb`; `Vazirmatn-Regular.ttf` SHA-256 `b69fd4c680b8f3f225feabcc655a2c585d97627b8f5f5c0f9985e894069f3a56`) | — | SIL OFL 1.1 (embedding and bundling allowed; cannot sell the font alone) | — | R | Community maintained | Sahel / Noto Naskh Arabic (OFL) |
| Installer | Inno Setup | 6.7.4 (6.x line; 7.1.1 also current) | — | Free; commercial users requested to buy a license | x86 setup stub | D | Active; 6.3.0+ minimum Windows 7 | NSIS (zlib) |
| (not recommended) | WiX Toolset | — | — | Requires Open Source Maintenance Fee for revenue use | — | D | — | — |

## 2. Runtimes and prerequisites

| Item | Version | Applies to | Offline package | Integrity check | Notes |
|---|---|---|---|---|---|
| .NET 10 Desktop Runtime | 10.0.x (host has 10.0.12) | Modern edition | Prefer self-contained publish (no prerequisite) | Authenticode + SHA-256 of release asset from dotnet release JSON | Microsoft support on Windows 10 only for LTSC/Enterprise 1607/1809/21H2 and Windows 11 |
| .NET Framework 4.8 | 4.8 (4.8.1 where available) | Legacy edition | `ndp48-x86-x64-allos-enu.exe` offline installer | Authenticode (Microsoft) + pinned SHA-256 recorded at download time in Phase 7 | Windows 7 SP1, 8.1: installable; Windows 10 1607+: installable/preinstalled |
| .NET Framework 4.6.1 | 4.6.1 | Windows 8 only (if D-01 = A) | Built in? No: Windows 8 ships 4.5 | — | **Retired 2022-04-26** |
| VC++ v14 redistributable | latest | Only if a native DLL imports `VCRUNTIME140`/`MSVCP140` | — | — | Latest supports **Windows 10/11 only**; avoid dependency (D-04) |
| Windows 7 updates | SP1 (+ SHA-2 support expected: KB4474419; verify) | Legacy on Windows 7 | MSU files | Microsoft signature | Verify on real Windows 7 |
| Windows 8.1 Update | KB2919355 | Legacy on 8.1 | — | — | SRS target is "8.1 Update" |

## 3. x86 memory considerations

- x86 process address space: 2 GB (4 GB with large-address-aware on x64 OS). Keep Argon2id memory ≤ 64 MiB per derivation and never run two in parallel.
- Large attachments: stream in 64 KiB chunks; never load whole files over a cap (proposed 50 MB per file, O-05).
- PDF generation: one page at a time; release Skia objects deterministically.
- PDFium preview: render visible page only, at screen resolution.
- Measured in spike: see TEST-EVIDENCE (Argon2id 64 MiB timing on x86 and x64).

## 4. Native binary analysis (Windows 7 / 8.1 risk)

From PE import tables of the restored packages (method and full table in TEST-EVIDENCE §4).

| Package | Windows 7 | Windows 8.1 | Action |
|---|---|---|---|
| SQLite3MC 2.7.1 (`sqlite3mc.dll`, static CRT, KERNEL32 only) | expected OK | expected OK | Keep; confirm with kit |
| SkiaSharp 4.157.0 (`libSkiaSharp.dll` imports `WaitOnAddress`) | expected FAIL | expected OK | Legacy: test an older SkiaSharp line (e.g. 2.88.x) or WPF print path; decide D-06 |
| HarfBuzzSharp 14.4 x86 (`GetSystemTimePreciseAsFileTime`) | expected FAIL (x86) | expected OK | As above |
| QuestPDF 2026.9.2 (x86 skia imports a Windows 8 API; x64 qpdf needs UCRT) | expected FAIL (x86) | expected OK | Not suitable for Windows 7 |
| PDFium 160 (subsystem 10.0) | expected FAIL | expected FAIL | Legacy preview: older PDFium build with Windows 7 subsystem, or no in-app PDF preview on Legacy (D-06) |

Persian PDF quality (TEST-EVIDENCE §3.4): Skia + HarfBuzz alone does not apply the Unicode bidi algorithm (digits reversed) and lost `پ` in text extraction. QuestPDF output was visually correct. Direct Skia use needs a bidi step (a managed UBA implementation or ICU) and a ToUnicode check.

## 5. Security maintenance strategy

1. Central package management, exact versions, `packages.lock.json` committed.
2. Weekly (manual, until CI approved): `dotnet list package --vulnerable --include-transitive`; GitHub Dependabot alerts on the private repo once enabled.
3. Track: SQLite releases (via SQLite3MC), PDFium (Chromium security), Skia/HarfBuzz, BouncyCastle advisories, .NET 10 Patch Tuesday.
4. Each native DLL update re-runs the import check (§4) and the Compat smoke on Windows 7/8.1.
5. Legacy runtime: .NET Framework 4.8 receives fixes only through Windows Update; on Windows 7/8.1 there are no OS-level updates anymore. Document as residual risk (THREAT-MODEL T21).
6. SBOM per release; licenses re-checked per release (PDFsharp moved to AGPL is a reminder that license terms change).

## 6. References (read 2026-11-26)

- https://learn.microsoft.com/en-us/dotnet/core/install/windows (updated 2026-11-13)
- https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md (last updated 2026-09-28)
- https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements
- https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server (updated 2026-02-04)
- https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework
- https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
- https://utelle.github.io/SQLite3MultipleCiphers/ and https://github.com/utelle/SQLite3MultipleCiphers-NuGet
- https://github.com/ericsink/SQLitePCL.raw (v3.md)
- https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/encryption (updated 2025-07-31)
- https://www.questpdf.com/pricing.html, https://www.questpdf.com/api-reference/content-direction.html
- https://github.com/mono/SkiaSharp
- https://github.com/bblanchon/pdfium-binaries
- https://jrsoftware.org/isinfo.php, https://jrsoftware.org/files/is6-whatsnew.htm
- https://github.com/wixtoolset/wix
- https://github.com/rastikerdar/vazirmatn (release v33.003)
- NuGet API: https://api.nuget.org/v3-flatcontainer/
