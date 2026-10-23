[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Developer release regressions require the canonical container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1') -Force
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-developer-release-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)
$commit = 'a' * 40
$version = '1.2.3-preview.7'
$image = 'ghcr.io/alexyorke/sharpproof@sha256:' + ('b' * 64)
$checks = 0

function New-Package {
    param(
        [string]$Directory, [string]$Id, [string]$IdentityId = $Id,
        [string]$PackageVersion = $version, [string]$SourceCommit = $commit,
        [string]$RepositoryUrl = 'https://github.com/alexyorke/SharpProof',
        [string]$MetadataValue = 'first', [string]$PayloadValue = 'fixture',
        [string]$VendorValue = 'fixture-vendor', [string]$ExtraEntry = '', [switch]$OmitRequired
    )
    [void][IO.Directory]::CreateDirectory($Directory)
    $path = Join-Path $Directory "$Id.$version.nupkg"
    if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $required = switch ($Id) {
            'SharpProof.Tool' { 'tools/net10.0/any/SharpProof.Tool.dll' }
            'SharpProof.CodeFixes' { 'analyzers/dotnet/cs/SharpProof.CodeFixes.dll' }
            'SharpProof.Templates' { 'content/container/compose.yaml.template' }
        }
        $content = [ordered]@{
            "$Id.nuspec"                                               = '<package><metadata><id>' + $IdentityId + '</id><version>' + $PackageVersion + '</version><repository type="git" url="' + $RepositoryUrl + '" commit="' + $SourceCommit + '" /></metadata></package>'
            '_rels/.rels'                                              = $MetadataValue
            '[Content_Types].xml'                                      = $MetadataValue
            'package/services/metadata/core-properties/fixture.psmdcp' = $MetadataValue
        }
        if (-not $OmitRequired) { $content[$required] = $PayloadValue }
        if ($Id -ceq 'SharpProof.Tool') { $content['tools/net10.0/any/Vendor.dll'] = $VendorValue }
        foreach ($name in $content.Keys) {
            $stream = $archive.CreateEntry($name).Open()
            try { $bytes = [Text.Encoding]::UTF8.GetBytes($content[$name]); $stream.Write($bytes) }
            finally { $stream.Dispose() }
        }
        if ($ExtraEntry) {
            $stream = $archive.CreateEntry($ExtraEntry).Open()
            try { $stream.Write([Text.Encoding]::UTF8.GetBytes('extra')) }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

function Assert-Rejected([scriptblock]$Operation, [string]$Expected) {
    $rejected = $false
    try { & $Operation | Out-Null }
    catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Invalid developer release accepted: $Expected" }
    $script:checks++
}

function New-RecordFixture([string]$Name) {
    $root = Join-Path $temporary $Name
    $scripts = Join-Path $root 'eng/scripts'
    [void][IO.Directory]::CreateDirectory($scripts)
    foreach ($file in @('Record-SharpProofDeveloperPackages.ps1', 'New-SharpProofPackageManifest.ps1',
            'SharpProof.ContainerExecution.psm1', 'SharpProof.PackageIdentity.psm1', 'Test-SharpProofPublishedPackages.sh')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $scripts
    }
    [IO.File]::WriteAllText((Join-Path $root 'eng/Versions.props'), '<Project><PropertyGroup><SharpProofVersionPrefix>1.2.3</SharpProofVersionPrefix><SharpProofPackageVersion>$(SharpProofVersionPrefix)-preview.7</SharpProofPackageVersion></PropertyGroup></Project>')
    $source = Join-Path $root 'packages'
    foreach ($id in @('SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')) { New-Package -Directory $source -Id $id }
    return , ([ordered]@{ Script = (Join-Path $scripts 'Record-SharpProofDeveloperPackages.ps1'); Source = $source; Output = (Join-Path $root 'artifacts/developer-packages') }).AsReadOnly()
}
function Invoke-Record($Fixture) {
    & $Fixture.Script -PackageSource $Fixture.Source -RepositoryCommit $commit -ConsumerImage $image
}
function Get-Snapshot([string]$Directory) {
    return Get-SharpProofDeveloperPackageSnapshot -PackageSource $Directory -PackageVersion $version -RepositoryCommit $commit
}

try {
    $fixture = New-RecordFixture 'valid'
    Invoke-Record $fixture | Out-Null
    $manifest = Get-Content -LiteralPath (Join-Path $fixture.Output 'developer-release-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.repositoryCommit -cne $commit -or $manifest.packageVersion -cne $version -or $manifest.consumerImage -cne $image -or
        @($manifest.packages).Count -ne 2 -or ((@($manifest.packages.id | Sort-Object) -join '|') -cne 'SharpProof.CodeFixes|SharpProof.Templates')) {
        throw 'The qualified developer receipt lost its exact package/source/image relationship.'
    }
    foreach ($package in $manifest.packages) {
        $copied = Join-Path $fixture.Output $package.fileName
        $original = Join-Path $fixture.Source $package.fileName
        if ($package.sha256 -cne (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant() -or
            $package.sha256 -cne (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash.ToLowerInvariant() -or
            $package.bytes -ne (Get-Item -LiteralPath $copied).Length) { throw 'The receipt does not describe the exact qualified archive bytes.' }
    }
    $rows = @(Get-Content -LiteralPath (Join-Path $fixture.Output 'SHA256SUMS'))
    if ($rows.Count -ne 6) { throw 'Developer checksums must cover both archives and every identity/verification sidecar.' }
    foreach ($row in $rows) {
        if ($row -cnotmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$' -or
            $Matches[1] -cne (Get-FileHash -LiteralPath (Join-Path $fixture.Output $Matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant()) {
            throw 'Developer checksums do not authenticate the recorded output.'
        }
    }
    $checks++
    Assert-Rejected { Invoke-Record $fixture } 'Developer release output must be empty.'
    foreach ($mutation in @('identity', 'version', 'commit', 'repository')) {
        $fixture = New-RecordFixture "wrong-$mutation"
        switch ($mutation) {
            'identity' { New-Package -Directory $fixture.Source -Id 'SharpProof.Templates' -IdentityId 'Unqualified.Templates' }
            'version' { New-Package -Directory $fixture.Source -Id 'SharpProof.Templates' -PackageVersion '9.9.9' }
            'commit' { New-Package -Directory $fixture.Source -Id 'SharpProof.Templates' -SourceCommit ('c' * 40) }
            'repository' { New-Package -Directory $fixture.Source -Id 'SharpProof.Templates' -RepositoryUrl 'https://example.invalid/other' }
        }
        $message = if ($mutation -eq 'repository') { 'invalid repository metadata' } else { 'identity, version, or source commit differs' }
        Assert-Rejected { Invoke-Record $fixture } $message
        if (@(Get-ChildItem -LiteralPath $fixture.Output -File).Count -ne 0) { throw 'Identity rejection copied a partial release feed.' }
    }
    $fixture = New-RecordFixture 'missing'
    [IO.File]::Delete((Join-Path $fixture.Source "SharpProof.Templates.$version.nupkg"))
    Assert-Rejected { Invoke-Record $fixture } 'Expected exactly one qualified SharpProof.Templates archive.'
    $fixture = New-RecordFixture 'duplicate'
    Copy-Item -LiteralPath (Join-Path $fixture.Source "SharpProof.CodeFixes.$version.nupkg") -Destination (Join-Path $fixture.Source 'SharpProof.CodeFixes.duplicate.nupkg')
    Assert-Rejected { Invoke-Record $fixture } 'Expected exactly one qualified SharpProof.CodeFixes archive.'
    foreach ($entry in @('.signature.p7s', '../escape', 'SharpProof.Templates.nuspec')) {
        $fixture = New-RecordFixture ('zip-' + [Guid]::NewGuid().ToString('N'))
        New-Package -Directory $fixture.Source -Id 'SharpProof.Templates' -ExtraEntry $entry
        $message = if ($entry -eq '.signature.p7s') { 'Original release archives must be unsigned' } elseif ($entry -eq '../escape') { 'unsafe ZIP member' } else { 'exactly one nuspec' }
        Assert-Rejected { Invoke-Record $fixture } $message
    }
    $fixture = New-RecordFixture 'invalid-image'
    Assert-Rejected { & $fixture.Script -PackageSource $fixture.Source -RepositoryCommit $commit -ConsumerImage 'ghcr.io/alexyorke/sharpproof:latest' } 'ConsumerImage'
    $first = Join-Path $temporary 'reproduction-a'
    $second = Join-Path $temporary 'independent longer path/reproduction-b'
    foreach ($id in @('SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')) {
        New-Package -Directory $first -Id $id -MetadataValue 'first'
        New-Package -Directory $second -Id $id -MetadataValue 'different-container-metadata'
    }
    $baseline = Get-Snapshot $first
    $comparison = Compare-SharpProofDeveloperPackageSnapshots -First $baseline -Second (Get-Snapshot $second)
    if (-not $comparison.PayloadsEqual -or $comparison.PackageBytesEqual -or $comparison.PayloadCount -lt 7) {
        throw 'Payload reproduction must distinguish product bytes from separately measured ZIP bytes.'
    }
    $checks++
    foreach ($id in @('SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')) {
        New-Package -Directory $second -Id $id -MetadataValue 'different-container-metadata' -PayloadValue 'mutated-product'
        $comparison = Compare-SharpProofDeveloperPackageSnapshots -First $baseline -Second (Get-Snapshot $second)
        if ($comparison.PayloadsEqual -or $comparison.PayloadMismatches.Length -eq 0) { throw "Changed $id payload was accepted." }
        New-Package -Directory $second -Id $id -MetadataValue 'different-container-metadata'
        $checks++
    }
    New-Package -Directory $second -Id 'SharpProof.Tool' -VendorValue 'mutated-dependency'
    $comparison = Compare-SharpProofDeveloperPackageSnapshots -First $baseline -Second (Get-Snapshot $second)
    if ($comparison.PayloadsEqual -or -not ($comparison.PayloadMismatches -contains 'SharpProof.Tool/tools/net10.0/any/Vendor.dll')) {
        throw 'Optional tool dependency bytes escaped reproduction coverage.'
    }
    $checks++
    New-Package -Directory $second -Id 'SharpProof.Tool' -OmitRequired
    Assert-Rejected { Get-Snapshot $second } 'missing the required SharpProof.Tool payload'
    New-Package -Directory $second -Id 'SharpProof.Tool' -SourceCommit ('d' * 40)
    Assert-Rejected { Get-Snapshot $second } 'source differs from the exact checkout'
    New-Package -Directory $second -Id 'SharpProof.Tool' -ExtraEntry 'tools/net10.0/any/Vendor.dll'
    Assert-Rejected { Get-Snapshot $second } 'unsafe or duplicate ZIP member'
    Write-Host "Developer release regressions passed: $checks identity/image/hash/archive-policy and optional-payload controls. These fixture controls do not claim a published image, signed attestation, or full independent build reproduction."
}
finally {
    $resolvedTemporary = [IO.Path]::GetFullPath($temporary)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $resolvedTemporary.StartsWith($temporaryParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($resolvedTemporary) -notlike 'sharpproof-developer-release-*') { throw 'Refusing to remove an unexpected developer-release fixture.' }
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
