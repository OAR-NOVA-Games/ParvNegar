# ADR-P1-03 — Shared crypto formats and nonce scheme

Status: Accepted (2026-11-26).

Decision: BouncyCastle.Cryptography 2.7.0 for AES-256-GCM, HKDF-SHA256 and Argon2id on both runtimes.
- Chunk streams (attachments, backups): 64 KiB chunks. nonce = streamIndex:u32be ‖ chunkIndex:u64be under a key that is random per stream set. AAD binds the header hash, indexes and final flag.
- Envelopes: random nonce, single-use KEK (fresh salt).
- Argon2id bounds 19–64 MiB, t 2–10, p 1–4, checked before work.

This resolves the inconsistent Phase 0 nonce descriptions (STORAGE-FORMAT.md, preamble).

Evidence: known-answer tests (RFC 5869, McGrew–Viega GCM case 16, RFC 9106), independent HKDF values computed with Python, tamper/truncation/reorder tests, and the cross-edition matrix.
