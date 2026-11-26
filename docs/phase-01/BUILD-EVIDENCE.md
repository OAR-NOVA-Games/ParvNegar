# Phase 1 — Build Evidence

Date: 2026-11-26. Host: Windows 11 Home x64 10.0.26300, i7-12700H, 15.6 GB RAM (about 2–3 GB free during runs). .NET SDK 10.0.401; .NET Framework 4.8 targeting pack; .NET Framework 4.8.1 runtime; .NET 10.0.12 runtime (x64 and x86).

## Commands and results

| Step | Command | Exit | Result |
|---|---|---|---|
| Lock-file regeneration (after declaring both RIDs for every project) | `dotnet restore ParvNegar.sln --force-evaluate` | 0 | 10 lock files |
| Clean build (all `bin`/`obj` deleted first) | `powershell -ExecutionPolicy Bypass -File build.ps1 -LockedRestore` | 0 | `BUILD_OK`; net10 x64 9 s, net10 x86 10 s, net48 x64 8 s, net48 x86 7 s; 36 s total; 0 errors, 0 warnings (warnings are errors) |
| Locked restore drift check | same | 0 | Lock files unchanged after build (`git status` clean for `packages.lock.json`) |
| Compatibility kit | `powershell -File tools\Build-CompatKit.ps1` | 0 | `artifacts\ParvNegar-Phase1-CompatKit.zip`, 22,193,863 bytes, SHA-256 `0a11838d12a75331f7545f8fff3b8ff41076b6f9c622f646308d750f2b04d932` |

Projects built per combination by `build.ps1`: App, DevTool, Domain.Tests, Security.Tests, Storage.Tests and App.Tests. The libraries are built through project references.

## Build configurations

| Combination | TFM | RID | PlatformTarget (from RID) | Output |
|---|---|---|---|---|
| Modern x64 | net10.0-windows | win-x64 | x64 | `bin/Release/net10.0-windows/win-x64` |
| Modern x86 | net10.0-windows | win-x86 | x86 | `bin/Release/net10.0-windows/win-x86` |
| Legacy x64 | net48 | win-x64 | x64 | `bin/Release/net48/win-x64` |
| Legacy x86 | net48 | win-x86 | x86 | `bin/Release/net48/win-x86` |

## Artifact hashes (SHA-256, clean Release build)

| Artifact | SHA-256 |
|---|---|
| ParvNegar.exe net10 x64 | `5c8709953dae1b5800d103381708481c4343b1ff8ec54aaa915bc1ccdcc117e1` |
| ParvNegar.exe net10 x86 | `080934c9062f699e27949ae422759631225381601569b39a6585ac2e73ae2076` |
| ParvNegar.exe net48 x64 | `df8f27ca14b65de6884f1fc05e8b832e93e9f341e9d9f697968e4bf9662ab47c` |
| ParvNegar.exe net48 x86 | `6df6a49f9facdf4328244eacc3d23e9887800b937b19676e22069d2ee8935ac9` |
| ParvNegar.DevTool.exe net10 x64 | `cae2fa922a93000b550295ef9ee06243470df4f30a021e02240062f90d544ee9` |
| ParvNegar.DevTool.exe net10 x86 | `67132106f99cd4873bc585eec1f087e809e7723715d006566dcb32b499c2c7ca` |
| ParvNegar.DevTool.exe net48 x64 | `ffd579da73e853bcf5227be0d459e94ad79775304baaa2a44f4f6524d7fbfbd0` |
| ParvNegar.DevTool.exe net48 x86 | `6f023c53d442ece242aac0174a8d372ddcea6b2f211ac9d223b08af12b0d5638` |
| ParvNegar.Storage.dll (net48 x64 output) | `cb42b6970053c424a9ba64943823d2364280ef252e0e3cb6c0db405c7f7aada9` |
| ParvNegar.Storage.dll (net48 x86 output) | `a1c882c0842a2e4cd6641f7f41129b86f3241f2d100575b6ec0371458b0baf43` |
| ParvNegar.Domain.dll | `a5d2c212c7242e55e01dc75916232bde7d1f76ca680c2853155f8c0b6831f4ea` |
| sqlite3mc.dll x64 (SQLite3MC 2.7.1) | `5f23fc48a04c0c3546459ba53365bc397203ecf4218f8ebf92aeb679a3452205` |
| sqlite3mc.dll x86 (SQLite3MC 2.7.1) | `da5f63f94ee85486edd6c05309a62fbb831775f3bc4f8a070ee68941e8bdc1e7` |

Builds are deterministic, but the assembly hashes depend on the source commit. They identify this evidence run, not a release. Binaries are unsigned (signing is Phase 7).

## Issues found and fixed during Phase 1 builds

| Issue | Fix |
|---|---|
| `NU1004` in locked mode: library lock files recorded the runtime of whichever `dotnet test -r` triggered restore | `RuntimeIdentifiers=win-x64;win-x86` for every project; lock files regenerated; tests run with `--no-restore` |
| `test.ps1` used `$args` (a PowerShell automatic variable) | Renamed to `$targs` |
| DevTool on .NET Framework reported OS 6.2 | Added an app manifest with supportedOS entries |
