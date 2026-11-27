# Licensing architecture

Status: implemented and tested. The production signing key and production trust anchor are **not created** yet; they need separate owner approval (SIGNING-KEY-CUSTODY.md).

## 1. Goals and non-goals

- Perpetual, offline, one licensed device with limited hardware tolerance. The same license works in Modern and Legacy, x86 and x64.
- Verification never decrypts, opens or touches school data. License activation and account recovery are independent.
- Not a goal: preventing a determined attacker who can patch binaries or clone a whole PC (§6).

## 2. Components

| Component | Location | Role |
|---|---|---|
| Strict JSON and canonical form | `Licensing/StrictJson.cs` | One accepted byte form per payload; ambiguity rejected |
| Signed envelope and trust store | `Licensing/SignedArtifact.cs` | ES256-P1363 verification against pinned keys of the build profile |
| Formats | `Licensing/Artifacts.cs` | Activation request, license, owner provisioning; `LicenseVerifier` |
| Device binding | `Licensing/DeviceIdentity.cs`, `WindowsDeviceSignals.cs` | Signals → normalized → hashed claims; 2-of-3 policy |
| Typed codes | `Security/TypedCode.cs` | Bootstrap code and recovery key encoding |
| App integration | `Application/Security/SessionManager.cs` | Installed-license check on every state refresh; import never replaces a valid license with an invalid one |
| Publisher issuer | `tools/ParvNegar.Issuer` | Key files, issuance, reissue, provisioning, verification, privacy-minimized records |

## 3. Activation workflow (offline)

1. The app starts. With no valid license the state is `Unlicensed` and the License page is shown. The branded home screen is the only screen available.
2. «ساخت فایل درخواست» writes a `.pnreq` file:
   - request ID, edition, architecture, OS version and app version;
   - three hashed device claims.
   - No secrets and no school data. A 16-hex confirmation code is shown for phone verification.
3. The file reaches OAR NOVA by any channel.
4. The publisher checks the commercial entitlement (outside the software), then runs `ParvNegar.Issuer issue`. The result is a signed `.pnlic` file and an issuance record.
5. The customer imports the license. The app verifies, offline: the signature, the product, the edition and architecture, and the device match.
6. The publisher also issues the owner provisioning file `.pnprov`, and delivers the one-time bootstrap code by a **separate channel** (PROVISIONING-ARCHITECTURE.md).
7. Setup creates the installation. From then on, every start and every state refresh re-verifies the installed license before any protected access.

Errors appear as clear Persian messages: `Strings.For(LicenseCheck)` covers missing, malformed, wrong product, unknown key, invalid signature, wrong edition, device mismatch and insufficient identity.

## 4. Trust anchors and build profiles

- The app embeds exactly one anchor file: `Config/trust/<ParvTrustProfile>.json` (`ParvTrustProfile` = `Development` by default, or `Production`).
- A Production build trusts only `pn-prod-*` keys. A Development build never trusts `pn-prod-*` keys. `TrustStore` enforces both rules, and tests and `check-issuer-isolation.ps1` check them.
- `Production.json` has **no keys** until the ceremony, so a production build accepts no license at all. This fails closed.
- Test keys exist only inside test runs. Each cross-edition run creates its own ephemeral key and deletes it afterwards.

## 5. Issuer isolation

`tools/ParvNegar.Issuer` targets .NET 10 only and is not part of any customer publish, installer or compatibility kit. `tools/check-issuer-isolation.ps1` fails when:
- a project under `src/` references the issuer, or customer source contains signing or private-key code;
- an issuer binary or `.pnsk` file appears in app outputs or in the kit;
- Git tracks key-like files or private key blocks;
- the trust anchors break the profile rules.

The issuer holds no master password and cannot open school data.

## 6. Limits (stated, not hidden)

- **Offline licensing cannot revoke** a license on a disconnected PC. A revoked or reissued license stays usable on the old PC until that PC is wiped. Revoking a signing key only stops future installs that trust the updated anchors.
- **Cloned disks and VM snapshots** carry MachineGuid, and a VM can present the same SMBIOS UUID and disk serial. Binding raises the effort; it does not prevent cloning.
- **Spoofed identifiers and patched binaries:** a local administrator can intercept the Win32 calls or patch the verification. Signatures protect the license file, not the code that checks it.
- **Disk-image rollback** restores an old license state. That is harmless for perpetual licenses.
- **Clock independence:** no expiry and no time check, so clock changes cannot invalidate a license or extend it (tested).
