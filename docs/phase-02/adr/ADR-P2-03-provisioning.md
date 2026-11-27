# ADR-P2-03: Owner provisioning with a committed bootstrap code

Status: Accepted (engineering). Date: 2026-11-27.

## Decision

- **No default credentials.** The publisher signs a provisioning file bound to the license, the activation request and the owner username. The file carries only an HMAC-SHA256 commitment to a 160-bit bootstrap code. The code is delivered by a separate channel and replaced at setup by the owner's own password.
- **Binding:** setup accepts the file only together with the pending request created on that PC, and only once.

## Alternatives rejected

| Alternative | Why rejected |
|---|---|
| Temporary password inside the signed file | Readable by anyone who gets the file |
| Encrypting the file to the customer | No customer key exists before setup |
| Password hash in the file | Allows offline guessing; the commitment to a 160-bit random secret does not |

## Consequences

- Losing the bootstrap code before setup requires a new provisioning file (a support step).
- After a Windows reinstall before setup, a new request and a new provisioning file are needed.
