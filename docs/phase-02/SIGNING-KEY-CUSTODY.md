# Signing-key custody

The ParvNegar signing key is dedicated to ParvNegar. It is **not** the YariZan key, and no YariZan key, product ID or trust anchor is reused (the YariZan ES256-P1363 profile was read only as a design reference).

**Production status:**
- No production key exists.
- No production key generation was run.
- `Config/trust/Production.json` is empty.

Generating the key, choosing its custody location and enrolling its public key are **owner-controlled steps that need separate approval** (DECISION-REGISTER O-KEY-CER, O-PROD-ANCHOR).

## 1. Key types

| Kid prefix | Environment | Where it may exist |
|---|---|---|
| `pn-prod-YYYY-NN` | production | Offline signing machine and sealed backups only |
| `pn-dev-*` | development | Developer PC, `.local/` (Git-ignored). Trusted only by Development builds |
| `pn-test-*` | test | Generated inside test runs; deleted at the end of the run |

## 2. Where private keys must never be

The following never contain a production private key, a development private key or a test private key:
- Git history,
- customer binaries, the installer or the compatibility kit,
- test fixtures,
- build artifacts and CI logs,
- application configuration,
- engineering reports.

Enforced by:
- `tools/check-issuer-isolation.ps1` (key-like files and private key blocks in Git; key files in outputs);
- `tools/check-repo-hygiene.ps1`;
- `.gitignore` (`.local/`, `*.key`, `*.pem`, `*.pfx`, `secrets/`);
- the CI hygiene job, which runs on every change.

## 3. Key file format (PNSK v1)

The private key (PKCS#8) is encrypted with AES-256-GCM under HKDF-SHA256(Argon2id(passphrase), info `"ParvNegar/v1/kek/signing-key"`). The AAD is the canonical JSON of every other field (kid, environment, SPKI, KDF parameters and salt, nonce). Wrong passphrases and edited fields are rejected (tested).

- The passphrase must be at least 20 bytes. It comes from `PARVNEGAR_SIGNING_PASSPHRASE`, from `--passphrase-file`, or from a hidden console prompt.
- The issuer refuses to overwrite an existing key or trust file.
- The issuer refuses `--environment production` without `--ceremony-approved`, and always refuses it when `CI=true`.
- Every artifact is verified with the public key immediately after signing.

## 4. Production key ceremony (to be approved and scheduled by the owner)

1. **Machine:** a dedicated offline PC with no network and full-disk encryption. Fresh Windows; the issuer is built from a reviewed commit and its hash is recorded.
2. **People:** the owner (key holder) and a witness. The passphrase is generated with dice or a password manager offline: at least 6 words or 20 random characters. It is split between two people, or stored in two sealed envelopes.
3. **Generation:**
   ```
   ParvNegar.Issuer keygen --kid pn-prod-2027-01 --environment production --ceremony-approved --out pn-prod-2027-01.pnsk --trust-out production-anchors.json
   ```
   Record the printed SPKI SHA-256 in the ceremony log, signed on paper by both people.
4. **Backups:** two copies of the `.pnsk` file on encrypted USB media, kept in two separate physical locations (for example an office safe and a bank deposit box). The passphrase is never stored with the media.
5. **Enrollment:** copy only `production-anchors.json` (public key) to a developer PC and commit it as `Config/trust/Production.json` in a reviewed pull request. Production builds use `-p:ParvTrustProfile=Production`.
6. **Test:** issue a test license for an internal PC request, verify it with `ParvNegar.Issuer verify`, and import it into a production build.
7. **Recovery drill:** once a year, verify that a backup copy opens with the passphrase, on the offline machine.

## 5. Rotation and retirement

- **Rotation:** generate `pn-prod-YYYY-NN+1` in a new ceremony and add it to `Production.json` as `active`. Mark the old key `retired`: it still verifies existing perpetual licenses, but the publisher stops signing with it. Ship the new anchors in the next release.
- **Retirement:** stop issuing with the key, keep its entry as `retired`, and keep its backups for reissue verification.
- **Compromise:** mark the key `revoked` in the anchors of the next release (it then verifies nothing) and reissue every license signed with it under a new key. PCs that do not update keep trusting the old key: an offline limit, stated in LICENSING-ARCHITECTURE §6.
