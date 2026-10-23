[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageSource,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$RepositoryCommit
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
Assert-SharpProofContainer 'Record developer packages in the canonical container.'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
[xml]$versions = Get-Content -LiteralPath (Join-Path $repository 'eng/Versions.props') -Raw
$version = ([string]$versions.Project.PropertyGroup.SharpProofPackageVersion).Replace('$(SharpProofVersionPrefix)', [string]$versions.Project.PropertyGroup.SharpProofVersionPrefix)
$output = Join-Path $repository 'artifacts/developer-packages'
[void][IO.Directory]::CreateDirectory($output)
if (@(Get-ChildItem -LiteralPath $output -File).Count -ne 0) { throw 'Developer release output must be empty.' }
foreach ($id in @('SharpProof.CodeFixes', 'SharpProof.Templates')) {
    $files = @(Get-ChildItem -LiteralPath $PackageSource -Filter "$id.*.nupkg" -File)
    if ($files.Count -ne 1) { throw "Expected exactly one qualified $id archive." }
    $identity = Get-SharpProofPackageIdentity -Path $files[0].FullName -RequireRepository -RequireSingleIdentity
    if ($identity.Id -cne $id -or $identity.Version -cne $version -or $identity.RepositoryCommit -cne $RepositoryCommit) {
        throw 'Developer package identity, version, or source commit differs from qualification.'
    }
    Copy-Item -LiteralPath $files[0].FullName -Destination $output
}
& (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $output
$rows = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
})
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS'), ($rows -join "`n") + "`n", [Text.UTF8Encoding]::new($false))