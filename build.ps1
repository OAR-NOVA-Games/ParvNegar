<#
.SYNOPSIS
  Sequential, memory-conscious build of all four primary combinations:
  net10.0-windows x64/x86 (Modern) and net48 x64/x86 (Legacy).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build.ps1
#>
param(
    [string]$Configuration = "Release",
    [switch]$LockedRestore
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$restoreArgs = @("restore", "ParvNegar.sln", "-v", "q", "--disable-parallel")
if ($LockedRestore) { $restoreArgs += "-p:ParvLockedRestore=true" }
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "restore failed ($LASTEXITCODE)" }

$results = @()
foreach ($tfm in @("net10.0-windows", "net48")) {
    foreach ($rid in @("win-x64", "win-x86")) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        foreach ($proj in @("src\ParvNegar.App\ParvNegar.App.csproj", "tools\ParvNegar.DevTool\ParvNegar.DevTool.csproj",
                            "tests\ParvNegar.Domain.Tests\ParvNegar.Domain.Tests.csproj", "tests\ParvNegar.Security.Tests\ParvNegar.Security.Tests.csproj",
                            "tests\ParvNegar.Storage.Tests\ParvNegar.Storage.Tests.csproj", "tests\ParvNegar.App.Tests\ParvNegar.App.Tests.csproj",
                            "tests\ParvNegar.Licensing.Tests\ParvNegar.Licensing.Tests.csproj", "tests\ParvNegar.Application.Tests\ParvNegar.Application.Tests.csproj")) {
            & dotnet build $proj -c $Configuration -f $tfm -r $rid --no-restore -nologo -v q -m:1 -nodeReuse:false
            if ($LASTEXITCODE -ne 0) { throw "build failed: $proj $tfm $rid ($LASTEXITCODE)" }
        }
        $results += "{0,-16} {1,-8} OK ({2:N0} s)" -f $tfm, $rid, $sw.Elapsed.TotalSeconds
    }
}
$results | ForEach-Object { Write-Host $_ }
Write-Host "BUILD_OK"
