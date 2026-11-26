# Phase 1 — Dependency Lock Report

Date verified: 2026-11-26. Source: `Directory.Packages.props` (exact ranges) and the 10 committed `packages.lock.json` files. Vulnerability scan: none found (`dotnet list package --vulnerable --include-transitive`, exit 0). Licenses from the NuGet `.nuspec` license expressions.

## 1. Direct dependencies

| Package | Version | Used by | TFM used | License | Native | Status |
|---|---|---|---|---|---|---|
| Microsoft.Data.Sqlite.Core | 10.0.15 | Storage | netstandard2.0 | MIT | via SQLitePCLRaw | Validated (4 combinations + cross-edition). Pinned to 10.0.x: 11.0.0 targets net10.0 only |
| SQLite3MC.PCLRaw.bundle (+ .lib, .provider) | 2.7.1 | Storage | netstandard2.0 | MIT | `sqlite3mc.dll` x86/x64 (static CRT, KERNEL32 only, subsystem 6.0) | Validated on host; Windows 7/8.1 runtime NOT RUN |
| BouncyCastle.Cryptography | 2.7.0 | Security | netstandard2.0 | MIT | none | Validated (known answers) |
| System.Security.Cryptography.ProtectedData | 10.0.0 | Security | netstandard2.0 (net462 asset on .NET Framework) | MIT | none (crypt32) | Validated |
| CommunityToolkit.Mvvm | 8.4.2 | App | netstandard2.0 asset on net48, net8.0 asset on net10 | MIT | none | Validated (ObservableObject, RelayCommand; source generators not used) |
| Microsoft.NET.Test.Sdk | 18.12.0 | tests | net462/net8.0 | MIT | — | Test only |
| xunit | 2.9.3 | tests | — | Apache-2.0 | — | Test only |
| xunit.runner.visualstudio | 3.1.5 | tests | net472/net8.0 | Apache-2.0 | — | Test only |

## 2. Resolved transitive packages (from lock files)

| Package | Resolved | Pulled by |
|---|---|---|
| SQLitePCLRaw.core | 3.0.2 | SQLite3MC provider (Microsoft.Data.Sqlite.Core 10 declares ≥ 2.1.11; 3.0.2 works on both runtimes, proven by tests) |
| SQLite3MC.PCLRaw.lib / .provider | 2.7.1 | bundle |
| System.Memory | 4.6.3 | several (netstandard2.0 on .NET Framework) |
| System.Buffers | 4.6.1 | System.Memory |
| System.Numerics.Vectors | 4.6.1 | System.Memory |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | System.Memory |
| System.Threading.Tasks.Extensions | 4.6.3 | CommunityToolkit.Mvvm |
| Microsoft.Bcl.AsyncInterfaces | 10.0.1 | CommunityToolkit.Mvvm |
| System.ComponentModel.Annotations | 5.0.0 | CommunityToolkit.Mvvm |
| NETStandard.Library | 2.0.3 | netstandard2.0 projects (implicit) |
| Microsoft.NETCore.Platforms | 1.1.0 | NETStandard.Library |
| Test-only: Microsoft.TestPlatform.*, Microsoft.CodeCoverage 18.12.0, xunit.* 2.9.3, xunit.abstractions 2.0.3, xunit.analyzers 1.18.0, System.Collections.Immutable 1.5.0, System.Reflection.Metadata 1.6.0 | — | test SDK |

## 3. Bundled non-NuGet assets

| Asset | Version | License | Integrity |
|---|---|---|---|
| Vazirmatn Regular/Medium/Bold TTF | v33.003 (release zip SHA-256 `0a9afd41967e6f57096a56a181a23f81a2b999b62f1f2a4e4b26736580854fdb`) | SIL OFL 1.1 (redistribution and embedding allowed; `OFL.txt` shipped as `licenses/Vazirmatn-OFL.txt`) | Regular `b69fd4c680b8f3f225feabcc655a2c585d97627b8f5f5c0f9985e894069f3a56`, Medium `b986623e4ddef10755e04be39f8ea7bcb1dc08bfe8dd0aa6af395736f256ad4a`, Bold `f635fdbea28f265de395ba83b4b1570dcf2f58d13c65469e61903b1c2d2ae723`, OFL.txt `17e355067c8284f47743a1ee3b1ef7ff684ff0601eda357f9353b10b3016ab31` |
| Brand icon and horizontal logo | Corrected brand kit (approved) | OAR NOVA proprietary | ICO: 7 sizes 16–256 px, 32 bpp, corners alpha 0 (transparent) verified |

## 4. Not included (deliberately)

SkiaSharp, HarfBuzzSharp, QuestPDF, PDFium, PDFsharp (AGPL), EF Core, Dapper (thin ADO.NET instead), System.Data.SQLite, SQLitePCLRaw.bundle_e_sqlcipher (deprecated). Reasons: ADR-P1-02, ADR-P1-06.

## 5. Maintenance

- Re-run the vulnerability scan and `tools/check-native-imports.py` for every dependency change.
- Watch: SQLite3MC releases (SQLite security fixes), BouncyCastle advisories, the .NET 10 servicing line (Microsoft.Data.Sqlite 10.0.x patches), .NET Framework 4.8 updates via Windows Update (none on Windows 7/8.1).
- The SQLitePCLRaw.core 3.x / Microsoft.Data.Sqlite 10 combination is outside the declared minimum and is proven only by our tests. Keep the probe and storage tests as the gate.
