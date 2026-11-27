<#
.SYNOPSIS
  Trigger-mapping tests for tools/ci/Select-CiJobs.ps1. Every relevant path class must map to the gate
  it needs; unknown paths must map to the full gate. Prints CI_MAPPING_OK or the failing cases.
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Select-CiJobs.ps1")

$cases = @(
    @{ Files = @("docs/phase-02/CI-STRATEGY.md"); Full = $false },
    @{ Files = @("README.md", "docs/phase-01/PHASE-01-REPORT.md"); Full = $false },
    @{ Files = @("AGENTS.md"); Full = $false },
    @{ Files = @("LICENSE"); Full = $false },
    @{ Files = @("src/ParvNegar.Security/Primitives.cs"); Full = $true },
    @{ Files = @("src/ParvNegar.App/Views/MainWindow.xaml"); Full = $true },
    @{ Files = @("src/ParvNegar.Storage/Migrations/0002_accounts_and_audit.sql"); Full = $true },
    @{ Files = @("src/ParvNegar.App/Config/school-profile.xml"); Full = $true },
    @{ Files = @("src/ParvNegar.App/Config/trust/Production.json"); Full = $true },
    @{ Files = @("tests/ParvNegar.Application.Tests/SecurityFlowTests.cs"); Full = $true },
    @{ Files = @("tools/ParvNegar.Issuer/Program.cs"); Full = $true },
    @{ Files = @("tools/ParvNegar.DevTool/Program.cs"); Full = $true },
    @{ Files = @("tools/check-repo-hygiene.ps1"); Full = $true },
    @{ Files = @("tools/compat-kit/RUN-COMPAT.cmd"); Full = $true },
    @{ Files = @("tools/ci/Select-CiJobs.ps1"); Full = $true },
    @{ Files = @(".github/workflows/ci.yml"); Full = $true },
    @{ Files = @("Directory.Packages.props"); Full = $true },
    @{ Files = @("Directory.Build.props"); Full = $true },
    @{ Files = @("src/ParvNegar.App/packages.lock.json"); Full = $true },
    @{ Files = @("global.json"); Full = $true },
    @{ Files = @("ParvNegar.sln"); Full = $true },
    @{ Files = @("build.ps1"); Full = $true },
    @{ Files = @("test.ps1"); Full = $true },
    @{ Files = @(".gitignore"); Full = $true },
    @{ Files = @(".gitattributes"); Full = $true },
    @{ Files = @("assets/publisher/OAR-NOVA-Games-Logo-White.png"); Full = $true },
    @{ Files = @("assets/brand-kit/01-App-Icon/ICO/ParvNegar-AppIcon.ico"); Full = $true },
    @{ Files = @("docs/phase-02/X.md", "src/ParvNegar.Domain/Student.cs"); Full = $true },     # mixed change
    @{ Files = @("something/new/unknown.bin"); Full = $true },                                  # unmapped -> full
    @{ Files = @("docs\phase-02\WINDOWS-PATH.md"); Full = $false },                             # backslashes
    @{ Files = @(); Full = $true },                                                             # nothing reported -> full
    @{ Files = $null; Full = $true }                                                            # unknown -> full
)

$fail = 0
foreach ($c in $cases) {
    $d = Get-CiDecision $c.Files $false
    if ($d.Full -ne $c.Full) { $fail++; Write-Host ("MAPPING FAIL: [" + ($c.Files -join ", ") + "] expected full=" + $c.Full + " got " + $d.Full) }
}
if ((Get-CiDecision @("docs/a.md") $true).Full -ne $true) { $fail++; Write-Host "MAPPING FAIL: manual full validation" }
if ($fail -eq 0) { Write-Host ("CI_MAPPING_OK cases=" + ($cases.Count + 1)); exit 0 }
Write-Host "CI_MAPPING_FAILURES=$fail"
exit 1
