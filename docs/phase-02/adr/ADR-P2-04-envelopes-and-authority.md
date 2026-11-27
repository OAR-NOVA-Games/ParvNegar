# ADR-P2-04: PNKE v2 envelopes; the encrypted database is the authority

Status: Accepted (engineering). Date: 2026-11-27. Builds on ADR-P1-04 (layered unlock with DPAPI LocalMachine).

## Decision

- **Envelope v2:** the AES-GCM AAD binds the envelope ID, revision and username hash, in addition to v1's account and installation IDs.
- **Discovery without an index:** the login scans the envelope headers. These headers are unauthenticated and are used only to find candidates.
- **Database authority:** after unlock, the database decides: account status and role, the current envelope ID and revision, and permissions.
- **Crash-safe updates:** password changes and resets update the file first, then the database transaction, then delete the old file.

## Rationale

- Stale, swapped, transplanted and deleted envelopes are rejected through normal flows.
- Roles and permissions are never readable or editable before unlock.
- There is no pre-unlock index that could drift or be edited.

## Consequences

- **Not prevented:** a password change does not revoke old envelope copies cryptographically, because the LMK stays the same. LMK rotation is the remedy (planned).
- **Recovery:** the recovery envelope is bound to its envelope ID and revision, and the database holds the current pair, so a stale recovery envelope is rejected.
