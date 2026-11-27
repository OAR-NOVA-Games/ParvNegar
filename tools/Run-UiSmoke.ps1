<#
.SYNOPSIS
  Light/Dark UI evidence run (UI-UX-REPORT.md). Creates a SYNTHETIC installation for one build:
    1. ParvNegar.exe --smoke <out> --prepare      activation request for <out>\synthetic-root
    2. ParvNegar.Issuer issue / issue-provisioning with the DEVELOPMENT key (.local\dev-signing, never committed)
    3. ParvNegar.exe --smoke <out> --flow         drives license -> provisioning -> setup -> recovery key ->
                                                  workspace -> accounts -> security -> settings -> lock -> login ...
  and captures every screen in both themes. Close other windows first: captures are refused when another
  window covers ParvNegar (privacy guard).
#>
param(
    [string]$Tfm = "net10.0-windows",
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\artifacts\ui-smoke")
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$exe = Join-Path $root "src\ParvNegar.App\bin\$Configuration\$Tfm\$Rid\ParvNegar.exe"
$issuer = Get-ChildItem (Join-Path $root "tools\ParvNegar.Issuer\bin\$Configuration") -Recurse -Filter ParvNegar.Issuer.exe | Select-Object -First 1
$key = Join-Path $root ".local\dev-signing\pn-dev-2026-01.pnsk"
$pass = Join-Path $root ".local\dev-signing\passphrase.txt"
foreach ($f in @($exe, $key, $pass)) { if (-not (Test-Path $f)) { throw "Missing: $f" } }
if (-not $issuer) { throw "Missing issuer build" }

$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$p = Start-Process -FilePath $exe -ArgumentList "--smoke `"$OutDir`" --prepare" -PassThru -Wait
if ($p.ExitCode -ne 0 -or -not (Test-Path "$OutDir\request.pnreq")) { throw "prepare failed ($($p.ExitCode))" }

& $issuer.FullName issue --request "$OutDir\request.pnreq" --key $key --passphrase-file $pass --customer-ref ui-smoke-synthetic --out "$OutDir\license.pnlic" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "issue failed" }
$prov = & $issuer.FullName issue-provisioning --license "$OutDir\license.pnlic" --key $key --passphrase-file $pass --owner-username owner.synthetic --out "$OutDir\owner.pnprov"
if ($LASTEXITCODE -ne 0) { throw "issue-provisioning failed" }
# The bootstrap code is the last output line; synthetic installation only.
[IO.File]::WriteAllText("$OutDir\bootstrap.txt", ($prov | Select-Object -Last 1))

$p = Start-Process -FilePath $exe -ArgumentList "--smoke `"$OutDir`" --flow" -PassThru -Wait
Get-Content "$OutDir\ui-flow.tsv" | ForEach-Object { Write-Host $_ }
Remove-Item "$OutDir\bootstrap.txt" -Force
Write-Host "UI_FLOW_EXIT=$($p.ExitCode)"
exit $p.ExitCode
