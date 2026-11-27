# Phase 2 — Platform matrix

**Legend:**
- **PASS:** runtime evidence on that OS.
- **STATIC:** build and static analysis only.
- **NOT RUN:** no machine was available.
- **BLOCKED:** outside the approved scope.

| Platform | Modern x64 | Modern x86 | Legacy x64 | Legacy x86 | Notes |
|---|---|---|---|---|---|
| Windows 11 x64 (host 10.0.26300) | PASS | PASS (WOW64) | PASS | PASS (WOW64) | Build, 946 tests, cross-edition 185/185, shell smoke ×4, UI flow (Modern x64, Legacy x86), kit 24/24 + 4 signal checks + 4 smokes |
| Windows 10 1607+ (x64/x86) | NOT RUN | NOT RUN | NOT RUN | NOT RUN | No machine. DWM dark title bar 1809+ |
| Windows 8.1 Update | — (Modern not supported) | — | NOT RUN | NOT RUN | Legacy only. STATIC: no Windows 7/8.1 native blockers |
| Windows 7 SP1 | — | — | NOT RUN | NOT RUN | Legacy only, needs .NET Framework 4.8. STATIC as above. The disk-serial format may differ (DEVICE-BINDING §4) |
| Windows 8 | BLOCKED | BLOCKED | BLOCKED | BLOCKED | Owner decision D-01: upgrade to 8.1 |
| Windows 10 1507/1511 | BLOCKED | BLOCKED | BLOCKED | BLOCKED | Owner decision D-03 |

## APIs added in Phase 2 and their minimum Windows version

| API | Minimum Windows | Used for |
|---|---|---|
| `GetSystemFirmwareTable` | Vista SP1 / Server 2008 | SMBIOS UUID |
| `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`, `IOCTL_STORAGE_QUERY_PROPERTY` | XP / 2000 | Disk serial |
| `RegOpenKeyEx` + `KEY_WOW64_64KEY` | XP x64 / Vista | MachineGuid |
| DPAPI LocalMachine | 2000 | Device secret, guard key |
| `SystemEvents.SessionSwitch` / `PowerModeChanged` | All | Locks |
| `DwmSetWindowAttribute(20 or 19)` | Windows 10 1809+; fails harmlessly before | Dark title bar |
| `WindowFromPoint`, `GetMonitorInfo` (smoke capture only) | 2000 | Capture privacy guard |
| Named mutex in `Global\` | All (falls back to `Local\`) | Single instance |

## DPI and resolution

| Check | Status |
|---|---|
| Layout at 1024×768, 1366×768 and 1920×1080 × 100/125/150/200% (DIP work areas) | PASS, automated (`ShellTests` and shell smoke) |
| Rendering at 125% on 1920×1080 | PASS (screenshots) |
| Real rendering at 100%, 150% and 200%, and real 1024×768 and 1366×768 displays | NOT RUN |
| Screen readers (Narrator, NVDA) | NOT RUN. AutomationProperties names and IDs are set on inputs, buttons and logos |

## Resources

- **Peak private bytes:** Modern x64 about 290–374 MB; Legacy x86 about 130–207 MB, in the UI flow including setup.
- **Argon2id:** 334–950 ms depending on build.
- **2 GB / dual-core PCs:** NOT RUN.

**Release qualification remains blocked** on runtime evidence from Windows 7 SP1, 8.1 and 10. Run the kit on those machines.
