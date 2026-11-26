@echo off
rem ParvNegar Phase 1 compatibility kit. Synthetic data and scratch test keys only. No network needed.
rem Run on a real Windows 7 SP1 / 8.1 / 10 / 11 machine (x86 or x64). Results are written next to this file.
setlocal EnableDelayedExpansion
set KIT=%~dp0
set OUT=%KIT%results-%COMPUTERNAME%
if not exist "%OUT%" mkdir "%OUT%"

ver > "%OUT%\os.txt"
for %%K in (ProductName CSDVersion CurrentBuild UBR DisplayVersion EditionID) do reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v %%K >> "%OUT%\os.txt" 2>&1
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release >> "%OUT%\os.txt" 2>&1
echo PROCESSOR_ARCHITECTURE=%PROCESSOR_ARCHITECTURE% PROCESSOR_ARCHITEW6432=%PROCESSOR_ARCHITEW6432% >> "%OUT%\os.txt"
wmic os get Caption,Version,OSArchitecture,TotalVisibleMemorySize /value >> "%OUT%\os.txt" 2>&1
wmic cpu get Name,NumberOfCores,NumberOfLogicalProcessors /value >> "%OUT%\os.txt" 2>&1
wmic desktopmonitor get ScreenWidth,ScreenHeight /value >> "%OUT%\os.txt" 2>&1
wmic qfe get HotFixID > "%OUT%\hotfixes.txt" 2>&1

for %%E in (legacy-x86 legacy-x64 modern-x86 modern-x64) do (
  if exist "%KIT%%%E\devtool\ParvNegar.DevTool.exe" (
    echo [%%E] isolated checks ...
    "%KIT%%%E\devtool\ParvNegar.DevTool.exe" compat "%OUT%\%%E\checks" > "%OUT%\%%E-checks.tsv" 2>&1
    echo exit=!ERRORLEVEL! >> "%OUT%\%%E-checks.tsv"
    echo [%%E] interop data set ...
    "%KIT%%%E\devtool\ParvNegar.DevTool.exe" interop-produce "%OUT%\%%E\interop" > "%OUT%\%%E-interop.txt" 2>&1
    echo exit=!ERRORLEVEL! >> "%OUT%\%%E-interop.txt"
  )
  if exist "%KIT%%%E\app\ParvNegar.exe" (
    echo [%%E] shell smoke ^(a window opens and closes by itself^) ...
    start /wait "" "%KIT%%%E\app\ParvNegar.exe" --smoke "%OUT%\%%E\shell"
    echo exit=!ERRORLEVEL! > "%OUT%\%%E-shell-exit.txt"
  )
)
echo.
echo Done. Send back the folder: %OUT%
echo It contains OS information, test results, screenshots and synthetic test data only.
pause
