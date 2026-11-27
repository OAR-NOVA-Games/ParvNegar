<#
.SYNOPSIS
  License and issuer isolation gate (LICENSING-ARCHITECTURE.md section 5, SIGNING-KEY-CUSTODY.md).
  Fails when:
    - a customer-side project references the publisher issuer, or customer sources contain signing code;
    - a customer build output, publish folder or compatibility kit contains the issuer or a signing-key file;
    - a signing-key file (*.pnsk) or private key material is tracked by Git;
    - the Production trust anchors contain non-production keys, or the Development anchors contain production keys.
  Prints ISOLATION_OK or ISOLATION_PROBLEMS=<n>.
#>
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
$problems = New-Object System.Collections.Generic.List[string]

# 1. Project references: nothing under src/ may reference the issuer.
Get-ChildItem src -Recurse -Filter *.csproj | ForEach-Object {
    if ((Get-Content $_.FullName -Raw) -match "<ProjectReference[^>]*ParvNegar\.Issuer") { $problems.Add("customer project references the issuer: $($_.FullName)") }
}

# 2. Signing code in customer sources (verification only is allowed).
Get-ChildItem src -Recurse -Include *.cs | ForEach-Object {
    $t = Get-Content $_.FullName -Raw
    if ($t -match "GenerateSignature|HMacDsaKCalculator|ECKeyPairGenerator|PrivateKeyFactory|ECPrivateKeyParameters") { $problems.Add("signing or private-key code in customer source: $($_.FullName)") }
}

# 3. Build outputs, publish folders and the compatibility kit.
$scan = @("src\ParvNegar.App\bin", "artifacts\compat-kit") | Where-Object { Test-Path $_ }
foreach ($d in $scan) {
    Get-ChildItem $d -Recurse -File | Where-Object { $_.Name -like "ParvNegar.Issuer*" -or $_.Extension -eq ".pnsk" } |
        ForEach-Object { $problems.Add("issuer or signing key in customer output: $($_.FullName)") }
}

# 4. Git: no signing keys or private key blocks.
git ls-files | ForEach-Object {
    if ($_ -match "\.pnsk$|\.pem$|\.pfx$|\.p12$|\.key$") { $problems.Add("key-like file tracked by Git: $_") }
}
$pk = git grep -l -I -E 'BEGIN (EC |ENCRYPTED )?PRIVATE KEY|"format":"PARVNEGAR-SIGNING-KEY-V1"' -- . ':!tools/ParvNegar.Issuer/*' ':!docs/*' ':!tools/check-issuer-isolation.ps1' 2>$null
foreach ($f in $pk) { $problems.Add("private key material in tracked file: $f") }

# 5. Trust anchors per build profile.
$prod = Get-Content "src\ParvNegar.App\Config\trust\Production.json" -Raw
if ($prod -match '"kid"\s*:\s*"(?!pn-prod-)') { $problems.Add("Production trust anchors contain a non-production key") }
if ($prod -notmatch '"environment"\s*:\s*"production"') { $problems.Add("Production.json is not a production trust file") }
$dev = Get-Content "src\ParvNegar.App\Config\trust\Development.json" -Raw
if ($dev -match '"kid"\s*:\s*"pn-prod-') { $problems.Add("Development trust anchors contain a production key") }

if ($problems.Count -eq 0) { Write-Host "ISOLATION_OK"; exit 0 }
$problems | ForEach-Object { Write-Host "ISOLATION FAIL: $_" }
Write-Host "ISOLATION_PROBLEMS=$($problems.Count)"
exit 1
