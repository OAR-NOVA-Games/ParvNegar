# Threat model (updated for Phase 2)

Extends the Phase 0/1 security architecture. Assets:
- children's records, in the encrypted database (from Phase 3);
- the LMK and the keys derived from it;
- account credentials;
- the license entitlement;
- the publisher signing key.

| ID | Threat | Mitigation in Phase 2 | Residual risk |
|---|---|---|---|
| T-01 | Copying the data folder to another PC | The DB is encrypted under the LMK; the LMK is wrapped under password + device secret (DPAPI LocalMachine of the original Windows installation) | An attacker with the files **and** the password **and** code execution on the original Windows installation |
| T-02 | Another standard Windows user on a shared PC reads the files | Protected DACL on the newly created data root (SESSION-SECURITY §7); encrypted DB | Administrators; folders created by older builds keep their ACL |
| T-03 | Online password guessing in the UI | Persistent progressive throttling, global and per name; Argon2id 64 MiB | A local admin can reset the throttle file |
| T-04 | Offline guessing with copied envelopes | Argon2id 64 MiB with t = 3; the device secret is needed (DPAPI on the same Windows installation); 15-character minimum and a blocklist | Weak but "long" passwords; the blocklist is small (O-PWLIST) |
| T-05 | Envelope swap, stale or transplanted envelope | PNKE v2 AAD binding; the DB is the authority for envelope ID and revision; installation ID in the KEK | Full-folder rollback by an administrator |
| T-06 | Privilege escalation by a delegate | Authorization in services, inside transactions; owner-only operations not grantable; last-owner protection | A delegate who is also a Windows admin can attack the process |
| T-07 | A former insider keeps access (copied the LMK while authorized) | Envelope removal and audit | **Needs LMK rotation** (DB rekey exists; UI later). Documented |
| T-08 | Forged or tampered license | ES256-P1363, pinned anchors per build profile, canonical payload, low-S, domain separation, strict parser | Patched binaries |
| T-09 | License shared to other PCs | 2-of-3 device binding; reissue workflow | Cloned PCs and VMs; spoofed identifiers; offline PCs cannot be revoked |
| T-10 | Provisioning replay to take over a PC | Bound to license, request and the pending request on this PC; one-time; refused after initialization; bootstrap commitment | Theft of both the provisioning file and the separately delivered code **before** setup |
| T-11 | Leak of the publisher signing key | Offline ceremony, encrypted key file, no keys in Git or CI, isolation gate, rotation and revocation process | Leak before revocation reaches customer builds |
| T-12 | Development or test key trusted in production | Profile-specific embedded anchors; TrustStore prefix rules; tests and the isolation gate | — |
| T-13 | Unlocked session left unattended | 15-minute inactivity lock; Windows lock, switch and suspend lock; lock clears pages and closes windows | Up to 15 minutes of exposure; 15 s timer granularity |
| T-14 | Secrets in logs, screenshots or files | Audit content rules; no plaintext key on disk (tested); smoke and UI evidence use synthetic roots; capture privacy guard | Managed-memory copies; Windows crash dumps |
| T-15 | Crash during setup, password change or recovery | Staging folder with an atomic move; ordered file/DB/file updates; orphan and stale cleanup; pending device secret promotion | Disk-level corruption |
| T-16 | Clock manipulation | No expiry; monotonic inactivity; a backward clock means the maximum throttle delay | — |
| T-17 | Concurrent instances corrupting data | Named mutex; setup lock file; single Argon2id gate | Two Windows sessions when the Global mutex is refused (Local fallback) |
| T-18 | Smoke or debug modes bypassing authentication | Smoke always uses a synthetic root inside its output folder, refuses overlap with the real root, and passes through the real gates; no compiled credentials (hygiene gate) | — |
| T-19 | Public repository exposing implementation details | No secrets or keys in Git (gates); the owner chose to stay public | Attackers can read the code (no security by obscurity is assumed) |

The trust boundaries are drawn in ARCHITECTURE §3.
