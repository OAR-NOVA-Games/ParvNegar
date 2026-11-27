# ADR-P1-01 — Runtime targets and project split

Status: Accepted (2026-11-26). Owner decisions: D-01 (Windows 8 users upgrade to 8.1), D-03 (Windows 10 floor 1607; 1507/1511 blocked).

Context: SRS PLAT-01/02 requires Windows 7 SP1 onward on x86 and x64, with Windows 10/11 primary. .NET 10 does not support Windows 7/8.1. .NET Framework 4.8 is the highest runtime that installs on Windows 7 SP1 and 8.1.

Decision:
- Shared logic (Domain, Application, Security, Storage) targets `netstandard2.0` and is compiled once.
- Host, tests and tools multi-target `net10.0-windows` (Modern) and `net48` (Legacy) with `win-x64` and `win-x86`.
- No separate Windows 8 edition. No net461 target.

Consequences: one XAML set and one format implementation. Code in shared libraries avoids APIs missing on .NET Framework (`AesGcm`, `Convert.FromHexString`, span-only APIs). Microsoft.Data.Sqlite is capped at the 10.0 line (11.x dropped netstandard2.0).

Evidence: BUILD-EVIDENCE.md, TEST-EVIDENCE.md, CROSS-EDITION-EVIDENCE.md.
