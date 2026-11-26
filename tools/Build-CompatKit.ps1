<#
.SYNOPSIS
  Publishes the development shell and DevTool for the four combinations into artifacts\compat-kit and zips it.
  Framework-dependent; Legacy needs .NET Framework 4.8, Modern needs the .NET 10 Desktop Runtime.
#>
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$kit = Join-Path $root "artifacts\compat-kit"
if (Test-Path $kit) { Remove-Item -Recurse -Force $kit }
New-Item -ItemType Directory -Force $kit | Out-Null
$map = @(
    @{ Name = "legacy-x86"; Tfm = "net48"; Rid = "win-x86" },
    @{ Name = "legacy-x64"; Tfm = "net48"; Rid = "win-x64" },
    @{ Name = "modern-x86"; Tfm = "net10.0-windows"; Rid = "win-x86" },
    @{ Name = "modern-x64"; Tfm = "net10.0-windows"; Rid = "win-x64" }
)
foreach ($m in $map) {
    foreach ($p in @(@{ Proj = "src\ParvNegar.App\ParvNegar.App.csproj"; Sub = "app" }, @{ Proj = "tools\ParvNegar.DevTool\ParvNegar.DevTool.csproj"; Sub = "devtool" })) {
        $dest = Join-Path $kit "$($m.Name)\$($p.Sub)"
        & dotnet publish (Join-Path $root $p.Proj) -c $Configuration -f $m.Tfm -r $m.Rid --self-contained false --no-restore -nologo -v q -o $dest
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $($p.Proj) $($m.Tfm) $($m.Rid)" }
        Get-ChildItem $dest -Recurse -Include *.pdb | Remove-Item -Force
    }
}
Copy-Item (Join-Path $PSScriptRoot "compat-kit\RUN-COMPAT.cmd"), (Join-Path $PSScriptRoot "compat-kit\README.txt") $kit
$sums = Get-ChildItem $kit -Recurse -Include *.exe, *.dll | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($kit.Length + 1)
}
[System.IO.File]::WriteAllLines((Join-Path $kit "SHA256SUMS.txt"), $sums)
$zip = Join-Path $root "artifacts\ParvNegar-Phase1-CompatKit.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $kit "*") -DestinationPath $zip
$h = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("KIT {0} bytes={1} sha256={2}" -f $zip, (Get-Item $zip).Length, $h)
