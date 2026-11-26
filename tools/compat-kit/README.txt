ParvNegar Phase 1 compatibility kit (internal engineering build - NOT for customers)

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
  Double-click RUN-COMPAT.cmd. A ParvNegar window opens and closes by itself for each build.

Send back
  The folder results-<COMPUTERNAME>. It contains OS information, the hotfix list, check results,
  screenshots of the development shell and synthetic test data sets (scratch keys, no real data).
  Please also note: Windows edition, RAM, screen resolution, and any error dialog you saw.

The interop folders let the engineering team open data created on this machine with the other
editions (cross-machine interoperability). They contain only invented test records.
