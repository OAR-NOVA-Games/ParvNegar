# ADR-P2-01: Signed artifacts — ES256-P1363 over canonical JSON

Status: Accepted (engineering), for owner review. Date: 2026-11-27.

## Context

Licenses and provisioning files must be verifiable offline, the same way in .NET Framework 4.8 and .NET 10, and on x86 and x64. Each must have one unambiguous byte form. The YariZan ES256-P1363/JCS profile was evaluated read-only as a reference.

## Decision

- **Algorithm:** ECDSA P-256 / SHA-256 through BouncyCastle (already a dependency); no new package. Signatures use IEEE P1363 `r‖s` (64 bytes), deterministic per RFC 6979, low-S only.
- **Envelope:** `{algorithm, kid, payload: base64url(canonical payload), signature}`. Carrying the payload as base64url avoids canonicalization on the verify path. The verifier still requires the decoded payload to equal its own canonical re-serialization, so duplicate keys, alternate escapes and whitespace variants are impossible.
- **Domain separation:** a domain prefix per artifact type.
- **Trust:** keys are pinned SPKI DER (named-curve OID) in embedded profile anchors, with `kid` prefix rules per environment.
- **No reuse:** none of YariZan's production keys, product IDs or trust anchors.

## Consequences

- One small, strict JSON implementation (`StrictJson`, about 300 lines) must be maintained and tested; the tests cover duplicate keys, numbers, escapes, surrogates, BOM and depth.
- Signing code exists only in `tools/ParvNegar.Issuer`, plus test helpers.
- Alternatives rejected:
  - **JWS:** extra dependencies on .NET Framework, and algorithm-confusion pitfalls.
  - **RSA:** larger keys and signatures, no benefit.
  - **Ed25519:** not available in the .NET Framework platform crypto; BouncyCastle would work, but P-256 matches the existing OAR NOVA tooling and HSM options.
