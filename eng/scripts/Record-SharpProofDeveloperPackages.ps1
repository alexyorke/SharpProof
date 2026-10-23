[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageSource,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$RepositoryCommit,
    [Parameter(Mandatory)][ValidatePattern('^ghcr\.io/alexyorke/sharpproof(?:-preview)?@sha256:[a-f0-9]{64}$')][string]$ConsumerImage
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReleaseConfiguration.psm1')
Assert-SharpProofContainer 'Record developer packages in the canonical container.'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$version = Get-SharpProofEffectivePackageVersion -RepositoryRoot $repository
$output = Join-Path $repository 'artifacts/developer-packages'
[void][IO.Directory]::CreateDirectory($output)
if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Developer release output must be empty.' }
$qualified = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
foreach ($id in @('SharpProof.CodeFixes', 'SharpProof.Templates')) {
    $files = @(Get-ChildItem -LiteralPath $PackageSource -Filter "$id.*.nupkg" -File)
    if ($files.Count -ne 1) { throw "Expected exactly one qualified $id archive." }
    $identity = Get-SharpProofPackageIdentity -Path $files[0].FullName -RequireRepository -RequireSingleIdentity
    if ($identity.Id -cne $id -or $identity.Version -cne $version -or $identity.RepositoryCommit -cne $RepositoryCommit) {
        throw 'Developer package identity, version, or source commit differs from qualification.'
    }
    $qualified.Add(([ordered]@{ File = $files[0]; Identity = $identity }).AsReadOnly())
}
foreach ($package in $qualified.ToImmutable()) {
    Copy-Item -LiteralPath $package.File.FullName -Destination $output
}
& (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $output
$receipts = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
foreach ($package in $qualified.ToImmutable()) {
    $recorded = Get-Item -LiteralPath (Join-Path $output $package.File.Name)
    $receipts.Add(([ordered]@{
                id = $package.Identity.Id; version = $package.Identity.Version; fileName = $recorded.Name
                bytes = $recorded.Length; sha256 = (Get-FileHash -LiteralPath $recorded.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }).AsReadOnly())
}
$manifest = ([ordered]@{
        schemaVersion = 1; repositoryCommit = $RepositoryCommit; packageVersion = $version
        consumerImage = $ConsumerImage; packages = $receipts.ToImmutable()
        scope = 'Qualified optional editor and template originals plus their consumer-image input. The core-package SBOM describes only the separate core graph; this is archive provenance, not an optional-package or image SBOM.'
    }).AsReadOnly()
[IO.File]::WriteAllText((Join-Path $output 'developer-release-manifest.json'), ($manifest | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
$rows = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    })
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS'), ($rows -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
