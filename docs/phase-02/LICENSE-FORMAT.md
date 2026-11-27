# License, request and provisioning formats (v1)

Status: implemented and tested (`src/ParvNegar.Licensing`). Every format is strict: a reader rejects anything it does not understand exactly. Source: `Artifacts.cs`, `SignedArtifact.cs`, `StrictJson.cs`.

## 1. Strict JSON subset

Every licensing file is UTF-8 JSON limited to:
- objects, arrays and strings;
- integers in the IEEE-754 safe range (|n| ≤ 2^53 − 1);
- `true`, `false` and `null`.

The parser rejects:
- duplicate keys, fractions, exponents, leading zeros and `-0`;
- unpaired surrogates and raw control characters in strings;
- a byte order mark, invalid UTF-8 and trailing data;
- nesting deeper than 16 levels, and documents over 64 KiB.

**Canonical form:**
- no insignificant whitespace;
- object keys sorted by ordinal UTF-16 code units;
- strings escape only `"`, `\` and U+0000–U+001F (as `\b \f \n \r \t` or lower-case `\u00XX`);
- integers in plain decimal.

Every signed payload and every activation request must be byte-identical to the canonical serialization of its parsed value. A payload therefore has exactly one accepted byte form; the tests check this.

## 2. Signed envelope (`ES256-P1363`)

```json
{"algorithm":"ES256-P1363","kid":"<key id>","payload":"<base64url(canonical payload)>","signature":"<base64url(r||s)>"}
```

- **Signed bytes:** UTF-8 of a domain prefix, followed by the payload bytes. Each artifact type has its own prefix:
  - `ParvNegar-License-V1\n`
  - `ParvNegar-Owner-Provisioning-V1\n`

  A signature of one type therefore never verifies as another type.
- **Algorithm:** ECDSA over NIST P-256 with SHA-256. The issuer signs deterministically (RFC 6979).
- **Signature encoding:** exactly 64 bytes, `r ‖ s`, each 32 bytes big-endian.
- **Signature checks:**
  - 1 ≤ r, s < n;
  - only low-S signatures are accepted (s ≤ n/2), so each signature has one valid form.
- **base64url:** strict; no padding; must re-encode to the same text.
- **`kid`:** 3–64 characters of `a-z 0-9 -`. It must equal the `kid` inside the payload.
- **Keys:** pinned SubjectPublicKeyInfo values from the build's trust store (§5). An artifact never carries its own trust root. Unknown `algorithm` values are rejected.

## 3. License payload (`.pnlic`)

All fields are required. Unknown fields are rejected.

| Field | Rule |
|---|---|
| `format` | `"PARVNEGAR-LICENSE-V1"` |
| `kid` | Equals the envelope `kid` |
| `issued_at` | `yyyy-MM-ddTHH:mm:ssZ`, UTC. **Informational only:** it is never compared with any clock |
| `license_id` | Lower-case UUID |
| `license_revision` | Integer ≥ 1. A reissue raises it (LICENSE-REISSUE-POLICY) |
| `product_id` | `"parvnegar"`. Any other value gives `WrongProduct` |
| `customer_ref` | 1–64 characters of `A–Z a–z 0–9 . _ -`. An opaque publisher reference: no personal data |
| `policy` | `"perpetual"` |
| `expires_at` | `null`. Any other value is rejected |
| `editions` | Sorted, unique, non-empty subset of `["legacy","modern"]` |
| `architectures` | Sorted, unique, non-empty subset of `["x64","x86"]` |
| `request_id` | UUID of the activation request this license answers |
| `device_binding` | `{"claims":{"disk":H\|null,"machine_guid":H\|null,"smbios":H\|null},"match":"2-of-3","version":1}`. `H` = 64 lower-case hex digits. At least 2 claims must be non-null |

**Verification order** (`LicenseVerifier.Verify`):
1. Envelope: `Malformed`.
2. Trusted key: `UnknownKey`.
3. Signature over the exact bytes: `InvalidSignature`.
4. Canonical payload, exact fields and versions: `Malformed` / `UnsupportedFormat`.
5. Product: `WrongProduct`.
6. Edition and architecture: `WrongEdition`.
7. Current device identity complete: `InsufficientDeviceIdentity`.
8. Device match: `DeviceMismatch`.
9. Otherwise: `Valid`.

The verifier takes no clock parameter; a test asserts this.

## 4. Activation request (`.pnreq`)

An unsigned canonical JSON file. It contains no secrets, no school data and no raw hardware identifiers.

```
{"app_version","architecture","created_at","device_binding":{"claims":{...},"version":1},"edition","format":"PARVNEGAR-ACTIVATION-REQUEST-V1","os_version":"major.minor.build","product_id":"parvnegar","request_id"}
```

- The app refuses to create a request with fewer than 2 usable signals (`InsufficientDeviceIdentityException`).
- The confirmation code is the first 16 hex digits of SHA-256 of the file, in groups of 4. It can be read over the phone.

## 5. Owner provisioning (`.pnprov`)

```
{"bootstrap":{"algorithm":"HMAC-SHA256","commitment":H},"format":"PARVNEGAR-OWNER-PROVISIONING-V1","issued_at","kid","license_id","owner_username","product_id":"parvnegar","provisioning_id","request_id"}
```

- `commitment` = HMAC-SHA256(key = 20-byte bootstrap secret, message = `"ParvNegar/v1/provisioning-commitment\n" ‖ provisioning_id ‖ "\n" ‖ owner_username`).
- The bootstrap secret never appears in the file, in issuance records or in logs. It is delivered separately as a typed code (§6).
- `owner_username` is canonical: 3–32 characters of `a-z 0-9 . _ -`, starting with a letter.

## 6. Typed codes (recovery key and bootstrap code)

Defined in `TypedCode.cs`:
- **Encoding:** data ‖ 2-byte check value, in Crockford base32, in groups of 5.
  - Recovery key: 32 bytes, giving 55 characters.
  - Bootstrap code: 20 bytes, giving 36 characters.
- **Check value:** the first 2 bytes of SHA-256(purpose label ‖ data). A recovery key cannot be typed into the bootstrap field, and vice versa.
- **Tolerant input:** case-insensitive; spaces, hyphens and ZWNJ are ignored; O→0 and I/L→1; Persian and Arabic-Indic digits are accepted.
- **Error detection:** a typing error is detected with probability 1 − 1/65,536. Padding bits must be zero.

## 7. Trust-anchor file

```
{"environment":"production|development|test","format":"PARVNEGAR-TRUST-ANCHORS-V1","keys":[{"kid","spki":"<base64url DER>","status":"active|retired|revoked"}]}
```

- Production stores accept only `pn-prod-*` keys; other stores never accept `pn-prod-*` keys.
- Retired keys still verify perpetual licenses. Revoked keys verify nothing (SIGNING-KEY-CUSTODY §6).

## 8. Versioning

Any field or rule change needs a new `format` string, a new domain prefix where signatures are affected, reader support or an explicit refusal, and an update to this document and to the tests.
