# ADR-P1-02 — Encrypted SQLite via SQLite3 Multiple Ciphers

Status: Accepted (2026-11-26).

Decision: Microsoft.Data.Sqlite.Core 10.0.15 + SQLite3MC.PCLRaw.bundle 2.7.1 (MIT). Cipher, KDF iterations and legacy mode are set explicitly and read back. The key is a 256-bit HKDF-derived database key sent with `PRAGMA hexkey`. The provider is proven with `sqlite3mc_version()` before keying. WAL, `temp_store=MEMORY`, `foreign_keys=ON`, `secure_delete=ON`, `Pooling=False`. Thin ADO.NET repositories; no ORM; numbered SQL migrations with SHA-256 checksums in one transaction each. Details: STORAGE-FORMAT.md §1.

Alternatives rejected:
- SQLitePCLRaw `bundle_e_sqlcipher` (deprecated).
- System.Data.SQLite (encryption needs commercial SEE).
- EF Core (no supported .NET Framework version).

Risks: single maintainer for SQLite3MC; Windows 7 runtime load is not yet proven (static analysis clean). Fallback: build SQLite3MC from source (MIT).
