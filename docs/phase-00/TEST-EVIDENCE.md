# ParvNegar Phase 0 — Test Evidence

Date: 2026-11-26. All data synthetic. No source record, identifier or document was used as a fixture. Status values: PASS, FAIL, BLOCKED, NOT RUN. A compile-only result is never reported as PASS for runtime.

## 1. Environment

| Item | Value |
|---|---|
| Host OS | Windows 11 Home, `RtlGetVersion` 10.0.26300, UBR 9974 (registry `ProductName` reads "Windows 10 Home", a known Windows 11 registry quirk; `DisplayVersion` 26H2) |
| CPU / RAM | Intel i7-12700H (20 logical), 15.6 GB |
| Display | DPI scale 1.25 (WPF reported) |
| .NET SDK | 10.0.401 |
| Runtimes | .NET 10.0.12 (x64 and x86 via WOW64), .NET Framework 4.8.1 (release key 0x82718; reports 4.8.10008.0) |
| Tools | Python 3.14 (scratch venv: pypdf, pypdfium2, pillow, for evidence only) |
| Network | Used only for NuGet restore, vendor docs and font download (build/research time). Spike runtime makes no network call. |
| Not available | Windows 7/8/8.1/10 machines or VMs on this host. Hyper-V not available (Home edition). VMware has only Linux VMs. |

Durable copies of spike source, tools, results and the kit: `<workspace>\ParvNegar-phase0-scratch\` (outside the repository, not committed).

## 2. Spike

- Source: `ParvNegar-phase0-scratch/spike-src/` (`Program.cs`, `ParvSpike.csproj`, `app.manifest`). One source file, multi-targeted `net10.0-windows;net48;net461`, `RuntimeIdentifiers` `win-x64;win-x86`. Disposable, not production code. Scratch keys are random per run and never stored.
- Packages: Microsoft.Data.Sqlite.Core 10.0.15, SQLite3MC.PCLRaw.bundle 2.7.1 (resolves SQLitePCLRaw.core 3.0.2), BouncyCastle.Cryptography 2.7.0, SkiaSharp + SkiaSharp.HarfBuzz 4.157.0, HarfBuzzSharp 14.4.0.200, QuestPDF 2026.9.2, bblanchon.PDFium.Win32 160.0.8179. net461 excludes the PDF packages.
- Font: Vazirmatn v33.003 Regular (SHA-256 in DEPENDENCY-MATRIX).

Commands (from the spike folder):

```
dotnet restore -v q
dotnet build -c Release -f <tfm> -r <rid> --no-restore -p:SelfContained=false
bin/Release/<tfm>/<rid>/ParvSpike.exe <outdir>      # exit code = number of FAIL checks
```

Build results (final run): all six combinations exit 0.

| TFM | RID | Build exit | Run exit | PASS | FAIL | NOT RUN | exe SHA-256 |
|---|---|---|---|---|---|---|---|
| net10.0-windows | win-x64 | 0 | 0 | 18 | 0 | 0 | `1e559901fe0ebcd911ae9f7f6f7ec4a01c298cd99881463e8f0f3675bfdae63c` |
| net10.0-windows | win-x86 | 0 | 0 | 18 | 0 | 0 | `0077f7321723855b807ac4a4657cea6d86fb9ce902df9819f691287d72f8c13d` |
| net48 | win-x64 | 0 | 0 | 17 | 0 | 0 | `61005caeaec884000239c53a4a66b8c965b17aecaab8be75a5db114ea12367ca` |
| net48 | win-x86 | 0 | 0 | 17 | 0 | 0 | `614c8ee0628e3c282823352937a81627f1e06e642f3a1b6e46309f04deb9e1d4` |
| net461 | win-x64 | 0 | 0 | 14 | 0 | 3 | `918c2b72c97bf7d1d5849a8b4b6176b2cbcbe3cb555b3d98f71680a965697910` |
| net461 | win-x86 | 0 | 0 | 14 | 0 | 3 | `360ef30e8e53f546b8687cb124404bd1f730dacd1f7ec7b4ae9cfd918af301d9` |

(net10 has one extra check, platform `AesGcm`, which does not exist on .NET Framework.)

Important limit: the net461 binaries ran on this host's .NET Framework **4.8** runtime (in-place upgrade). That proves the net461 build compiles and runs on 4.8, not that it runs on a real 4.6.1 / Windows 8 system.

## 3. Check results on Windows 11 host

| Check | What it verifies | net10 x64 | net10 x86 | net48 x64 | net48 x86 | net461 x64 | net461 x86 |
|---|---|---|---|---|---|---|---|
| ENV | OS/runtime/bitness report | PASS | PASS | PASS | PASS | PASS | PASS |
| WPF-RTL-RENDER | Persian RTL text with embedded Vazirmatn rendered off-screen; ink in right half | PASS | PASS | PASS | PASS | PASS | PASS |
| WPF-XPS-FIXEDDOC | WPF FixedDocument → XPS (print path) without printer/Office/network | PASS | PASS | PASS | PASS | PASS | PASS |
| WPF-IMAGE-DECODE | In-memory PNG decode; random bytes rejected | PASS | PASS | PASS | PASS | PASS | PASS |
| SQLITE-MC-PASSWORD-KEYWORD | Encrypted DB via `Password=` connection keyword | PASS | PASS | PASS | PASS | PASS | PASS |
| SQLITE-MC-PRAGMA-KEY | Encrypted DB via `PRAGMA key` | PASS | PASS | PASS | PASS | PASS | PASS |
| CRYPTO-AESGCM-KAT-BC | AES-256-GCM, McGrew–Viega test case 16; ciphertext and AAD tamper rejected | PASS | PASS | PASS | PASS | PASS | PASS |
| CRYPTO-AESGCM-KAT-PLATFORM | `System.Security.Cryptography.AesGcm` same vector | PASS | PASS | n/a | n/a | n/a | n/a |
| CRYPTO-ARGON2ID-KAT-RFC9106 | RFC 9106 §5.3 Argon2id vector + timing m=64 MiB, t=3, p=1 | PASS | PASS | PASS | PASS | PASS | PASS |
| CRYPTO-PBKDF2-SHA256-KAT | RFC 7914 PBKDF2-HMAC-SHA256 vector (BC); platform vs BC | PASS | PASS | PASS* | PASS* | PASS | PASS |
| CRYPTO-ED25519-KAT-RFC8032 | RFC 8032 test 1 key/signature; tamper rejected | PASS | PASS | PASS | PASS | PASS | PASS |
| CRYPTO-ECDSA-P256-PLATFORM | Platform ECDSA P-256 sign/verify, P1363 64-byte signature; tamper rejected | PASS | PASS | PASS | PASS | PASS | PASS |
| DPAPI-CURRENTUSER | Protect/unprotect; wrong entropy rejected | PASS | PASS | PASS | PASS | PASS | PASS |
| BACKUP-ROUNDTRIP | Spike backup envelope (§3.2) | PASS | PASS | PASS | PASS | PASS | PASS |
| PDF-SKIA-HARFBUZZ | PDF file generated | PASS | PASS | PASS | PASS | NOT RUN | NOT RUN |
| PDF-QUESTPDF | PDF file generated, RTL content direction | PASS | PASS | PASS | PASS | NOT RUN | NOT RUN |
| PDFIUM-RENDER | Render Skia PDF page from memory; malformed PDF rejected | PASS | PASS | PASS | PASS | NOT RUN | NOT RUN |
| NATIVE-MODULES | Native DLLs actually loaded from the RID folder | PASS | PASS | PASS | PASS | PASS | PASS |

\* First run of net48: **FAIL** `ArgumentException: Salt is not at least eight bytes.` .NET Framework's `Rfc2898DeriveBytes` rejects salts shorter than 8 bytes, and the RFC 7914 vector uses a 4-byte salt. The BC part of the check passed. The check was changed to compare platform and BouncyCastle output with a 16-byte salt and 1,000 iterations; it then passed. Product salts are 16 bytes, so this is a test-vector limit, not a defect. Both runs are recorded here.

### 3.1 Encrypted database details (all six runs identical)

```
sqlite=3.54.1; cipher=chacha20; rows=400;
walScan=store.db:4096:clean,store.db-shm:32768:clean,store.db-wal:49472:clean;
journalScan=store.db:40960:clean,store.db-journal:41552:clean;
finalScan=store.db:86016:clean; header=non-plain; wrongKey=rejected; noKey=rejected
```

Method: 200 rows containing an ASCII canary, a Persian string and `پرونگار` were written in WAL mode. The DB, `-wal` and `-shm` files were scanned **before checkpoint**, while the WAL held the pages. A rollback-journal write transaction was then held open and the `-journal` was scanned. Canary patterns: UTF-8 and UTF-16LE ASCII canary and UTF-8 Persian. No match in any file. The first 16 bytes are not `SQLite format 3\0`. A wrong key and no key both raise `SqliteException`.

Findings:
- Microsoft.Data.Sqlite.Core 10.0.15 works with the SQLitePCLRaw.core **3.0.2** that SQLite3MC pulls in, on both net48 and net10, x86 and x64.
- The `Password=` keyword works on .NET Framework 4.8 with Microsoft.Data.Sqlite 10.0.x. (The SQLite3MC README says the keyword works "only with .Net 10". In practice that means Microsoft.Data.Sqlite 10, not the .NET 10 runtime.)
- `temp_store=MEMORY` was set. A large-sort temp-file scan was not performed: **NOT RUN**, add in Phase 1.

### 3.2 Backup envelope details

Format `PNBK-SPIKE-1` (spike only): Argon2id (m=64 MiB, t=3, p=1) wraps a random 256-bit data key with AES-256-GCM; the encrypted manifest (names, sizes, SHA-256) and files are 64 KiB AES-GCM chunks. Each chunk's nonce = file index ‖ chunk counter, and its AAD = header hash ‖ indexes ‖ final flag. Written to `.partial`, flushed, then renamed. Restore goes to a staging folder, then a directory move.

Verified: 4 files (300 KB, 64 KiB, 1 byte, 0 bytes) restored with equal SHA-256. Wrong passphrase, one flipped byte in the middle and truncation by 32 KiB were each rejected, and no target folder was left behind. File names do not appear in the archive bytes.

### 3.3 Timings (host, indicative only, not the low-resource profile)

| Measure | net10 x64 | net10 x86 | net48 x64 | net48 x86 |
|---|---|---|---|---|
| Argon2id m=64 MiB, t=3, p=1 | 339–504 ms | 1,113 ms | 230 ms | 599–625 ms |
| Backup write (≈365 KB, includes one Argon2id) | 207–225 ms | 1,147 ms | 308 ms | 653–978 ms |

x86 is 2–5× slower for Argon2id. On the low-resource profile, expect several seconds per login: measure on Windows 7 x86 hardware before fixing KDF parameters.

### 3.4 Persian rendering and PDF quality (visual and extraction review)

| Output | Visual (reviewed PNG) | Text extraction | Status |
|---|---|---|---|
| WPF TextBlock (`wpf-rtl.png`) | Correct joining, RTL order, digits `۱۲۳۴` placed correctly | n/a | **PASS** |
| Skia + HarfBuzz PDF (`skia-harfbuzz.pdf`, rendered by PDFium) | Joining correct, but digits drawn **reversed** (`۴۳۲۱` instead of `۱۲۳۴`): HarfBuzz shapes one RTL run and does no Unicode bidi reordering | pypdf and PDFium: `پ` extracts as U+0000/U+FFFE (missing ToUnicode mapping for that glyph) | **FAIL** (needs a bidi pass before shaping and a ToUnicode fix or check) |
| QuestPDF (`questpdf.pdf`) | Correct joining, correct digit order, correct parentheses mirroring | pypdf: logical order (presentation forms); PDFium: some words reversed | Visual **PASS**; extraction **FAIL** in PDFium reader, PASS in pypdf |
| WPF XPS (`fixeddoc.xps`) | Not visually inspected (no XPS viewer used) | — | NOT RUN (visual) |

Physical A4 printing: **NOT RUN** (no printer test).

## 4. Static native-binary analysis

Tool: `ParvNegar-phase0-scratch/tools/peimports.py` (no dependencies). It reads PE headers and import tables. It flags `MajorSubsystemVersion` and a short list of known Windows 8+ APIs (`GetSystemTimePreciseAsFileTime`, `WaitOnAddress`, `WakeByAddressSingle`, …). This is a heuristic: it cannot prove Windows 7 compatibility, only find known blockers. Windows 7 = 6.1, Windows 8 = 6.2, 8.1 = 6.3.

| Native DLL (package) | Arch | Subsystem | CRT | Imports of concern | Windows 7 | Windows 8.1 |
|---|---|---|---|---|---|---|
| `sqlite3mc.dll` (SQLite3MC 2.7.1) | x86, x64 | 6.0 | static | KERNEL32 only | expected OK | expected OK |
| `libSkiaSharp.dll` (4.157.0) | x86, x64 | 6.0 | static | `api-ms-win-core-synch-l1-2-0.dll` (`WaitOnAddress`, Windows 8+); x86 also `GetSystemTimePreciseAsFileTime` | **expected FAIL** | expected OK |
| `libHarfBuzzSharp.dll` (14.4.0.200) | x64 | 6.0 | static | none | expected OK | expected OK |
| `libHarfBuzzSharp.dll` | x86 | 6.0 | static | `GetSystemTimePreciseAsFileTime` | **expected FAIL** | expected OK |
| `pdfium.dll` (bblanchon 160.0.8179) | x86, x64 | **10.0** | static | — | **expected FAIL** (loader rejects subsystem 10.0) | **expected FAIL** |
| `questpdf_skia.dll` (QuestPDF 2026.9.2) | x64 | 6.0 | static | none | expected OK | expected OK |
| `questpdf_skia.dll` | x86 | 6.0 | static | `GetSystemTimePreciseAsFileTime` | **expected FAIL** | expected OK |
| `questpdf_qpdf.dll` | x64 | 5.2 | UCRT (`api-ms-win-crt-*`) | needs Universal CRT (KB2999226 on 7/8.1) | needs UCRT | needs UCRT |
| `questpdf_qpdf.dll` | x86 | 4.0 | `msvcrt.dll` | — | expected OK | expected OK |

Consequence: on Windows 7 the current Skia/HarfBuzz/QuestPDF/PDFium binaries are not expected to load. Encrypted storage (SQLite3MC) is expected to work. On Windows 8.1, Skia/HarfBuzz/QuestPDF are expected to load and PDFium is not. These are **expectations from static analysis** until the kit runs on real machines.

## 5. Untested matrix rows and the compatibility kit

Kit: `<workspace>\ParvNegar-phase0-scratch\ParvNegar-Phase0-CompatKit.zip` (187,996,218 bytes, SHA-256 `533953d5422d5235129ece4ca69598dd0b27ce6354f7a7b3fa7c460b5cafab4c`). It contains `net48-x86/x64`, `net461-x86/x64`, `net10-x86/x64` (framework-dependent), `RUN-COMPAT.cmd`, `README.txt` and `SHA256SUMS.txt`. The script records OS version, hotfixes and .NET Framework release, then runs each build. Each check is logged as it starts and ends (`progress.log`), so a native crash still leaves evidence.

| Row | Status | Needed |
|---|---|---|
| Windows 7 SP1 x64 / x86 | NOT RUN | Owner runs kit (net48) after installing .NET Framework 4.8 |
| Windows 8.1 Update x64 / x86 | NOT RUN | Owner runs kit (net48) |
| Windows 8 x64 / x86 | BLOCKED (D-01) / NOT RUN | Machine, plus .NET Framework 4.6.1 (net461 folder) |
| Windows 10 (22H2, 1607/1809 LTSC) x64 / x86 | NOT RUN | Machine; .NET 10 Desktop Runtime for the net10 folders; net48 folders run as-is |
| Windows 10 1507/1511 | BLOCKED (D-03) | — |
| Low-resource profile (2 GB, 2 cores, 1024×768) | NOT RUN | Windows 7 x86 hardware |
| Installer / offline prerequisite install | NOT RUN | Phase 7 |
| Physical print | NOT RUN | Printer |
| Cross-edition backup restore (net48 ↔ net10, x86 ↔ x64) | NOT RUN as a cross-run test (the same code and format ran on all builds; a file made by one build was not restored by another) | Phase 1 test |
| Licensing (offline signed license, device binding) | NOT RUN (only signature primitives tested) | Phase 2 |

## 6. Source-data handling evidence

- The archive was extracted into the session scratch directory, read by a local script that printed only cross-file-identical labels (SOURCE-FIELD-MAP §1), and then the extracted copy was **deleted** (`rm -rf <scratch>/src-archive`). The original archive in the owner's Downloads folder was not modified.
- No source file, value, logo or screenshot is in the repository or in the spike outputs.
