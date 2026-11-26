<#
.SYNOPSIS
  Cross-edition / cross-architecture interoperability test (Phase 1, CROSS-EDITION-EVIDENCE.md).
  Each of the four builds (Modern net10 x64/x86, Legacy net48 x64/x86) produces a synthetic encrypted data set
  and backup; every build then consumes every data set (16 pairs). Synthetic data and scratch keys only.
.PARAMETER Configuration
  Build configuration of tools/ParvNegar.DevTool (default Release). Build first with build.ps1.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\artifacts\interop")
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$builds = @(
    @{ Name = "modern-net10-x64"; Path = "tools\ParvNegar.DevTool\bin\$Configuration\net10.0-windows\win-x64\ParvNegar.DevTool.exe" },
    @{ Name = "modern-net10-x86"; Path = "tools\ParvNegar.DevTool\bin\$Configuration\net10.0-windows\win-x86\ParvNegar.DevTool.exe" },
    @{ Name = "legacy-net48-x64"; Path = "tools\ParvNegar.DevTool\bin\$Configuration\net48\win-x64\ParvNegar.DevTool.exe" },
    @{ Name = "legacy-net48-x86"; Path = "tools\ParvNegar.DevTool\bin\$Configuration\net48\win-x86\ParvNegar.DevTool.exe" }
)
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("producer`tconsumer`tcheck`tstatus`tdetail")
$failures = 0

foreach ($b in $builds) {
    $exe = Join-Path $root $b.Path
    if (-not (Test-Path $exe)) { throw "Missing build: $exe (run build.ps1 first)" }
    $out = & $exe interop-produce (Join-Path $OutDir $b.Name)
    if ($LASTEXITCODE -ne 0) { $failures++; $lines.Add("$($b.Name)`t-`tPRODUCE`tFAIL`texit=$LASTEXITCODE $out") }
    else { $lines.Add("$($b.Name)`t-`tPRODUCE`tPASS`t$out") }
}

foreach ($p in $builds) {
    foreach ($c in $builds) {
        $exe = Join-Path $root $c.Path
        $result = & $exe interop-consume (Join-Path $OutDir $p.Name)
        $code = $LASTEXITCODE
        foreach ($r in $result) {
            if ($r -match "^CONTEXT") { continue }
            $lines.Add("$($p.Name)`t$($c.Name)`t$r")
        }
        if ($code -ne 0) { $failures += $code }
        Write-Host ("{0,-18} -> {1,-18} exit={2}" -f $p.Name, $c.Name, $code)
    }
}

$tsv = Join-Path $OutDir "cross-edition-matrix.tsv"
[System.IO.File]::WriteAllLines($tsv, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Matrix: $tsv"
Write-Host "FAILED_CHECKS=$failures"
exit $failures
