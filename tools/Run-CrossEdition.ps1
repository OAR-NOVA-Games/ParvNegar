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

# ---- Licensing (Phase 2): every build must read the same binding claims, and a license issued for a
# request made by one build must verify in all four. Ephemeral TEST signing key created for this run only.
$issuer = Get-ChildItem (Join-Path $root "tools\ParvNegar.Issuer\bin\$Configuration") -Recurse -Filter ParvNegar.Issuer.exe | Select-Object -First 1
if (-not $issuer) { throw "Missing issuer build (dotnet build tools\ParvNegar.Issuer)" }
$lic = Join-Path $OutDir "licensing"
New-Item -ItemType Directory -Force $lic | Out-Null
$claimsSeen = @{}
foreach ($b in $builds) {
    $exe = Join-Path $root $b.Path
    $sig = & $exe license-signals x
    $claimLine = ($sig | Where-Object { $_ -like "CLAIMS*" }) -replace "^CLAIMS\t[^\t]+\t", ""
    $claimsSeen[$b.Name] = $claimLine
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) { $failures++ }
    $lines.Add("$($b.Name)`t-`tLICENSE_SIGNALS`t$(if ($ok) {'PASS'} else {'FAIL'})`t$(($sig | Where-Object { $_ -like 'SIGNALS*' }) -replace '^SIGNALS\t[^\t]+\t','')")
}
$distinct = @($claimsSeen.Values | Sort-Object -Unique)
if ($distinct.Count -ne 1) { $failures++; $lines.Add("all`t-`tLICENSE_CLAIMS_IDENTICAL`tFAIL`t$($distinct -join ' | ')") }
else { $lines.Add("all`t-`tLICENSE_CLAIMS_IDENTICAL`tPASS`tdigest prefixes $($distinct[0])") }

$env:PARVNEGAR_SIGNING_PASSPHRASE = [Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Maximum 256) }))
& $issuer.FullName keygen --kid pn-test-crossedition --environment test --out (Join-Path $lic "test.pnsk") --trust-out (Join-Path $lic "test-anchors.json") | Out-Null
foreach ($p in $builds) {
    $req = Join-Path $lic "$($p.Name).pnreq"
    & (Join-Path $root $p.Path) license-request $req | Out-Null
    $licFile = Join-Path $lic "$($p.Name).pnlic"
    & $issuer.FullName issue --request $req --key (Join-Path $lic "test.pnsk") --customer-ref crossedition-test --out $licFile | Out-Null
    if ($LASTEXITCODE -ne 0) { $failures++; $lines.Add("$($p.Name)`t-`tLICENSE_ISSUE`tFAIL`texit=$LASTEXITCODE"); continue }
    foreach ($c in $builds) {
        $v = & (Join-Path $root $c.Path) license-verify $licFile (Join-Path $lic "test-anchors.json")
        $ok = $LASTEXITCODE -eq 0
        if (-not $ok) { $failures++ }
        $lines.Add("$($p.Name)`t$($c.Name)`tLICENSE_VERIFY`t$(if ($ok) {'PASS'} else {'FAIL'})`t$(($v -replace '^LICENSE\t[^\t]+\t',''))")
    }
}
Remove-Item Env:\PARVNEGAR_SIGNING_PASSPHRASE
Remove-Item (Join-Path $lic "test.pnsk") -Force   # the ephemeral test key does not outlive the run

$tsv = Join-Path $OutDir "cross-edition-matrix.tsv"
[System.IO.File]::WriteAllLines($tsv, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Matrix: $tsv"
Write-Host "FAILED_CHECKS=$failures"
exit $failures
