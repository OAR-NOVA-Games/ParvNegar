# ADR-P2-02: Device binding v1 — three hashed signals, 2-of-3

Status: Proposed. It needs review and real Windows 7, 8.1 and 10 measurements before production release. Date: 2026-11-27.

## Decision

- **Signals:** SMBIOS UUID, system-disk serial, MachineGuid (64-bit view), read with Win32 calls only. Each is normalized and hashed with SHA-256 and a domain label.
- **Rule:** a license is valid when 2 of its claims equal the current claims. An unreadable signal never lowers the threshold. Placeholder values are unusable.
- **Not used:** MAC address, IP address, WMI.

## Rationale

- Each common maintenance event changes one signal; a new PC changes all three.
- WMI was avoided because it is slow, can be broken by repository corruption, and differs on Windows 7.
- Hashing keeps raw serials out of requests, licenses and records.

## Consequences

- Cloned systems and VMs are not prevented.
- On Windows 7 the disk serial format may differ, so the claim can change on an OS upgrade (tolerated by 2-of-3).
- Measuring real machines with the kit's `license-signals` is part of release qualification.
