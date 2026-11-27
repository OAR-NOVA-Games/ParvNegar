<#
.SYNOPSIS
  Runs every test project on all four combinations, one at a time. PARVNEGAR_EXPECTED_ARCH makes each test
  assert the real process architecture, so an x64 process can never report x86 results.
  Results: artifacts/test-results/<tfm>-<rid>/*.trx and a summary table.
#>
param(
    [string]$Configuration = "Release",
    [switch]$NoBuild
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$out = Join-Path $PSScriptRoot "artifacts\test-results"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
$projects = @("ParvNegar.Domain.Tests", "ParvNegar.Security.Tests", "ParvNegar.Storage.Tests", "ParvNegar.Licensing.Tests", "ParvNegar.Application.Tests", "ParvNegar.App.Tests")
$summary = New-Object System.Collections.Generic.List[string]
$failed = 0

foreach ($tfm in @("net10.0-windows", "net48")) {
    foreach ($rid in @("win-x64", "win-x86")) {
        $env:PARVNEGAR_EXPECTED_ARCH = $rid.Substring(4)
        foreach ($p in $projects) {
            $dir = Join-Path $out "$tfm-$rid"
            $targs = @("test", "tests\$p\$p.csproj", "-c", $Configuration, "-f", $tfm, "-r", $rid, "-nologo",
                      "--logger", "trx;LogFileName=$p.trx", "--results-directory", $dir)
            $targs += "--no-restore"
            if ($NoBuild) { $targs += "--no-build" }
            $text = & dotnet @targs 2>&1 | Out-String
            $code = $LASTEXITCODE
            $line = ($text -split "`n" | Where-Object { $_ -match "(Passed!|Failed!)" } | Select-Object -Last 1)
            if (-not $line) { $line = "NO SUMMARY (exit $code)" }
            $summary.Add(("{0,-16} {1,-8} {2,-26} exit={3} {4}" -f $tfm, $rid, $p, $code, $line.Trim()))
            if ($code -ne 0) { $failed++; Write-Host $text }
        }
    }
}
Remove-Item Env:\PARVNEGAR_EXPECTED_ARCH -ErrorAction SilentlyContinue
$summary | ForEach-Object { Write-Host $_ }
[System.IO.File]::WriteAllLines((Join-Path $out "summary.txt"), $summary)
Write-Host "FAILED_RUNS=$failed"
exit $failed
