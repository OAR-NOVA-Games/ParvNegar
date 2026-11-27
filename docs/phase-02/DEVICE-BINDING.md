# Device binding (version 1)

Status: implemented (`DeviceIdentity.cs`, `WindowsDeviceSignals.cs`). It is an engineering proposal and needs review before production release. Tested with unit tests and on the Windows 11 host in all four builds; **not run** on Windows 7, 8.1 or 10.

## 1. Signals

| Signal | Source | Notes |
|---|---|---|
| `smbios` | SMBIOS type-1 System UUID via `GetSystemFirmwareTable('RSMB')` (Windows Vista SP1+). Formatted like `Win32_ComputerSystemProduct.UUID` | Identifies the mainboard. Some OEMs ship placeholder values |
| `disk` | Serial of the physical disk holding the Windows volume: `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`, then `IOCTL_STORAGE_QUERY_PROPERTY` | No administrator rights needed (handles opened with no data access) |
| `machine_guid` | `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`, **64-bit registry view** (`KEY_WOW64_64KEY`) | Changes on Windows reinstall. x86 processes read the same value: verified in all four builds (CROSS-EDITION-EVIDENCE §3) |

Not used: MAC addresses, IP addresses, network, GPU, RAM, CPU identifiers, volume serials and WMI.

- **Why these three:** they are reasonably independent; each one changes on a different maintenance event (mainboard, disk, OS reinstall).
- **Not counted:** fields that change together (for example several SMBIOS strings of the same board) are not counted as separate signals.

## 2. Normalization and minimization

- **Normalization:** NFKC, then control characters removed, whitespace collapsed and lower-cased. UUID signals must parse as UUIDs and are re-formatted canonically.
- **Placeholders**, treated as unusable and never guessed around:
  - empty values, `0`, all-zero and all-F values, a single repeated character;
  - known OEM template UUIDs (`03000200-0400-0500-0006-000700080009`, …);
  - text such as "To be filled by O.E.M.", "Default string", "None" or "System Serial Number";
  - disk serials shorter than 4 characters.
- **Claim:** `SHA-256("ParvNegar/v1/device/<name>\n" ‖ normalized)` as lower-case hex. Raw values never leave the process: they are not in the request, license, logs or reports. The DevTool prints only 12-hex prefixes.

## 3. Matching policy

- The license holds up to 3 claims. A license with fewer than 2 claims is refused by both issuer and reader.
- **Valid when** at least **2** license claims equal the current claims (constant-time compare). A null claim on either side never matches.
- An unreadable current signal counts as a mismatch. **The threshold is never lowered**; with fewer than 2 readable current signals the result is `InsufficientDeviceIdentity`.
- Request generation refuses to run with fewer than 2 usable signals. This is shown as an explicit activation problem.

| Maintenance event | Changed signals | Result (tested) |
|---|---|---|
| RAM, GPU, network card, monitor | 0 | Valid |
| Disk replaced (Windows restored from image) | disk | Valid (2/3) |
| Windows reinstalled on the same disk | machine_guid | Valid (2/3) |
| Mainboard replaced | smbios | Valid (2/3) |
| Disk replaced **and** Windows reinstalled | disk + machine_guid | Mismatch → reissue |
| New PC | all | Mismatch → reissue |
| One signal unreadable, other two unchanged | 1 unavailable | Valid (2 real matches) |
| One signal unreadable and one changed | — | Mismatch |

## 4. Known limitations

- **Disk serial format by Windows version:** Windows 7 may report some ATA serials hex-encoded or byte-swapped. After an in-place upgrade from 7 to 10 the disk claim may change. The 2-of-3 policy tolerates this, but it must be measured on real Windows 7 machines (compatibility kit, `license-signals`).
- **Virtual machines:** values depend on the hypervisor; a clone has identical values.
- **Hardware RAID, USB boot disks and some NVMe bridges** may expose no serial. The PC then has 2 usable signals and needs both to match.
- **Cloning, spoofing and patching:** LICENSING-ARCHITECTURE §6.
