# License reissue policy

Owner-approved principle: substantial hardware changes or a replacement PC require a **publisher-approved reissue**. The commercial rules are the owner's: who qualifies, how often, and whether a fee applies. This document covers only the technical workflow.

## 1. When a reissue is needed

- The app shows the Persian `DeviceMismatch` message: 2 or more signals changed (DEVICE-BINDING §3), or the license belongs to another PC.
- The customer moves the installation to a replacement PC.
- The edition or architecture rights change: for example a license issued for Legacy only must now cover Modern as well.

## 2. Workflow

1. The customer generates a new activation request on the target PC (License page, or Workspace → مجوز). They send the `.pnreq` file and read out the confirmation code.
2. The publisher:
   - verifies the customer's entitlement and that the request is for the same customer;
   - finds the original issuance record (`license_id`, `customer_ref`);
   - records the reason for the reissue.
3. The publisher runs:
   ```
   ParvNegar.Issuer issue --request new.pnreq --key <key.pnsk> --customer-ref <same ref> \
       --license-id <original license id> --revision <previous + 1> --out reissued.pnlic --records <dir>
   ```
   The record is written with kind `license-reissue`. The same `license_id` and a higher `license_revision` make the history traceable.
4. The customer imports the new license. An invalid file never replaces a valid installed license.

## 3. School data after a replacement PC

A license reissue **never** decrypts or moves school data. Moving data to a new PC uses the backup and restore path (Phase 6), or the owner recovery path with device re-binding when the data folder is moved as a whole (RECOVERY-POLICY §4).

## 4. Revocation limits

The old PC keeps its old license offline (LICENSING-ARCHITECTURE §6). The policy relies on the customer agreement. The issuance records show which licenses were superseded.

## 5. Records

`issuance-records.jsonl` holds one canonical JSON line per issuance:
- kind, `license_id`, revision, `customer_ref`, `request_id`, `kid`, `issued_at`, editions and architectures;
- the **hashed** device claims.

Provisioning records hold the provisioning ID, owner username and the first 8 hex digits of the commitment, **never** the bootstrap code. Records belong to the publisher's protected storage, never to Git.
