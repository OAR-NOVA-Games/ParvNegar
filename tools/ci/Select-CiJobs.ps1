<#
.SYNOPSIS
  Path-aware CI classification (docs/phase-02/CI-STRATEGY.md).
  Decides whether a change needs the FULL gate (build x4, tests x4, cross-edition, isolation, native and
  dependency checks) or only the HYGIENE gate (documentation-only changes).
  Rules, in order:
    1. Manual full validation (workflow_dispatch) or a push to main without a usable base  -> full
    2. Any path that is not explicitly documentation                                         -> full
       (unknown and unmapped paths are conservative: they always trigger the full gate)
    3. Only documentation paths                                                               -> hygiene only
  The hygiene gate (repository hygiene + issuer isolation) runs for every change.
.EXAMPLE
  ./tools/ci/Select-CiJobs.ps1 -Files docs/phase-02/X.md,README.md      -> full=false
#>
param(
    [string[]]$Files,
    [string]$Base,
    [string]$Head = "HEAD",
    [switch]$ForceFull
)
$ErrorActionPreference = "Stop"

# Documentation-only paths. Everything else (src, tests, tools, assets, build files, lock files,
# workflows, scripts, configuration, unknown files) needs the full gate.
$DocPatterns = @(
    '^docs/',
    '^[^/]+\.md$',                 # README.md, AGENTS.md, CHANGELOG.md ... at the root
    '^assets/brand-kit/00-Readme/',
    '^assets/publisher/README\.md$',
    '^LICENSE$'
)

function Test-IsDocumentation([string]$path) {
    $p = $path -replace '\\', '/'
    foreach ($re in $DocPatterns) { if ($p -match $re) { return $true } }
    return $false
}

function Get-CiDecision([string[]]$changed, [bool]$force) {
    if ($force) { return [pscustomobject]@{ Full = $true; Reason = "manual full validation" } }
    if ($null -eq $changed) { return [pscustomobject]@{ Full = $true; Reason = "changed files unknown" } }
    $list = @($changed | Where-Object { $_ -and $_.Trim() })
    if ($list.Count -eq 0) { return [pscustomobject]@{ Full = $true; Reason = "no changed files reported" } }
    $code = @($list | Where-Object { -not (Test-IsDocumentation $_) })
    if ($code.Count -gt 0) { return [pscustomobject]@{ Full = $true; Reason = "build-affecting or unmapped: " + ($code | Select-Object -First 5) -join ", " } }
    return [pscustomobject]@{ Full = $false; Reason = "documentation only (" + $list.Count + " file(s))" }
}

if ($MyInvocation.InvocationName -ne '.') {
    $changed = $Files
    if (-not $changed -and $Base) {
        $changed = @(git diff --name-only "$Base...$Head")
        if ($LASTEXITCODE -ne 0) { $changed = $null }
    }
    $d = Get-CiDecision $changed $ForceFull.IsPresent
    Write-Host ("full=" + $d.Full.ToString().ToLowerInvariant() + "  (" + $d.Reason + ")")
    if ($env:GITHUB_OUTPUT) { Add-Content -Path $env:GITHUB_OUTPUT -Value ("full=" + $d.Full.ToString().ToLowerInvariant()) }
}
