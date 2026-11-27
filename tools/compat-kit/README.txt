ParvNegar Phase 1 compatibility kit (follow-up build) (internal engineering build - NOT for customers)

What it contains
  legacy-x86, legacy-x64   .NET Framework 4.8 builds (Windows 7 SP1, 8.1, 10, 11)
  modern-x86, modern-x64   .NET 10 builds, framework-dependent (Windows 10 1607+ / 11).
                           Needs the .NET 10 Desktop Runtime installed; skip on Windows 7/8.1.
  Each folder: app\ParvNegar.exe (development shell) and devtool\ParvNegar.DevTool.exe (checks).

Before running
  Windows 7 SP1 / 8.1: install .NET Framework 4.8 (offline installer ndp48-x86-x64-allos-enu.exe).
  On 32-bit Windows only the x86 folders can run; x64 failures there are expected.
  No administrator rights and no internet connection are needed.

Run
  Double-click RUN-COMPAT.cmd. For each build the OAR NOVA splash appears for 2 seconds, then a
  ParvNegar window opens, resizes itself several times and closes by itself.
  Close or minimize other windows first and do not use the computer while it runs: screenshots are
  copied from the screen, so the shell refuses to save one when another window covers it
  (the result then shows a capture failure instead of a picture of your other windows).

Send back
  The folder results-<COMPUTERNAME>. It contains OS information, the hotfix list, check results,
  screenshots of the development shell and synthetic test data sets (scratch keys, no real data).
  The license-signals files show only whether each hardware signal could be read and short hash
  prefixes; they contain no serial numbers or other raw hardware identifiers.
  Please also note: Windows edition, RAM, screen resolution, and any error dialog you saw.

The interop folders let the engineering team open data created on this machine with the other
editions (cross-machine interoperability). They contain only invented test records.
