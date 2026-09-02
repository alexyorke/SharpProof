[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('canonical','changed-symbol','stale-manifest','stale-sbom',
        'missing-identity','duplicate-identity',
        'version-syntax','commit-syntax','string-schema','decimal-bytes',
        'array-version','array-commit','array-artifact-text',
        'destination-tamper','package-action-tamper','fixture-canonical',
        'fixture-authority-tamper','fixture-nonexistent-archive',
        'registry-canonical',
        'registry-url-tamper','targetless-publish-tamper',
        'json-roundtrip','two-bundle')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanIdentity.psm1') -Force
. (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanTopology.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PublicationDestination.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-plan-identity-' + [Guid]::NewGuid().ToString('N'))
$version = '1.0.0-preview.1'
$commit = '0123456789abcdef0123456789abcdef01234567'
try {
    if ($Mutation -eq 'version-syntax') {
        $module = Get-Module SharpProof.PublicationPlanIdentity
        $canonicalAccepted = & $module {
            param($Candidate)
            Test-SharpProofPublicationVersionSyntax -Version $Candidate
        } $version
        $lineFeedAccepted = & $module {
            param($Candidate)
            Test-SharpProofPublicationVersionSyntax -Version $Candidate
        } "$version`n"
        $unicodeAccepted = & $module {
            param($Candidate)
            Test-SharpProofPublicationVersionSyntax -Version $Candidate
        } ('1.2.3-' + [char]0x212a)
        if (-not $canonicalAccepted -or $lineFeedAccepted -or
            $unicodeAccepted) {
            throw 'Publication version syntax is not strictly anchored.'
        }
        Write-Host 'Publication plan identity fixture passed: version-syntax'
        return
    }
    if ($Mutation -eq 'commit-syntax') {
        $module = Get-Module SharpProof.PublicationPlanIdentity
        $canonicalAccepted = & $module {
            param($Candidate)
            Test-SharpProofPublicationCommitSyntax -Commit $Candidate
        } $commit
        $lineFeedAccepted = & $module {
            param($Candidate)
            Test-SharpProofPublicationCommitSyntax -Commit $Candidate
        } "$commit`n"
        $uppercaseAccepted = & $module {
            param($Candidate)
            Test-SharpProofPublicationCommitSyntax -Commit $Candidate
        } ('A' * 40)
        if (-not $canonicalAccepted -or $lineFeedAccepted -or
            $uppercaseAccepted) {
            throw 'Publication commit syntax is not strictly anchored.'
        }
        Write-Host 'Publication plan identity fixture passed: commit-syntax'
        return
    }
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $packages = [Collections.Generic.List[object]]::new()
    $artifactRows = [Collections.Generic.List[object]]::new()
    foreach ($id in @('SharpProof.Attributes','SharpProof','SharpProof.Verifier')) {
        $main = Join-Path $root "$id.$version.nupkg"
        $symbols = Join-Path $root "$id.$version.snupkg"
        [IO.File]::WriteAllText($main, "main:$id")
        [IO.File]::WriteAllText($symbols, "symbols:$id")
        $packages.Add([pscustomobject]@{ mainPath = $main; symbolsPath = $symbols })
        foreach ($path in @($main,$symbols)) {
            $file = Get-Item -LiteralPath $path
            $artifactRows.Add([pscustomobject][ordered]@{
                fileName = $file.Name
                bytes = [int64]$file.Length
            })
        }
    }
    $sbom = Join-Path $root 'SharpProof.spdx.json'
    [IO.File]::WriteAllText($sbom, '{"spdxVersion":"SPDX-2.3"}')
    $sbomFile = Get-Item $sbom
    $artifactRows.Add([pscustomobject][ordered]@{
        fileName = $sbomFile.Name
        bytes = [int64]$sbomFile.Length
    })
    $manifestPath = Join-Path $root 'SharpProof.release.json'
    $manifest = [pscustomobject][ordered]@{
        packageVersion = $version
        versionAuthority = [pscustomobject][ordered]@{
            schemaVersion = 1
            path = 'SharpProof.Release.props'
            property = 'SharpProofPackageVersion'
            version = $version
        }
        repository = [pscustomobject][ordered]@{ commit = $commit }
        artifacts = @($artifactRows)
    }
    [IO.File]::WriteAllText(
        $manifestPath,
        (($manifest | ConvertTo-Json -Depth 5) -replace "`r`n","`n") + "`n")
    $identities = @(New-SharpProofPublicationPlanIdentities `
        -Packages @($packages) -Directory $root -Version $version `
        -RepositoryCommit $commit)
    $plan = [pscustomobject][ordered]@{
        schemaVersion = 2
        planOnly = $true
        packageVersion = $version
        versionAuthority = [pscustomobject][ordered]@{
            schemaVersion = 1
            path = 'SharpProof.Release.props'
            property = 'SharpProofPackageVersion'
            version = $version
        }
        repositoryCommit = $commit
        publicationDestination = [pscustomobject][ordered]@{
            schemaVersion = 1
            mode = 'targetless'
            mainDestination = $null
            symbolDestination = $null
            packageBaseAddress = $null
            fixture = $null
        }
        packages = @($packages | ForEach-Object -Begin { $index = 0 } -Process {
            $id = @('SharpProof.Attributes','SharpProof','SharpProof.Verifier')[$index]
            $index++
            [pscustomobject][ordered]@{
                packageId = $id
                version = $version
                mainFileName = [IO.Path]::GetFileName($_.mainPath)
                symbolsFileName = [IO.Path]::GetFileName($_.symbolsPath)
                availabilityMode = 'targetless'
                remoteState = $null
                fixtureState = $null
                remoteUrl = $null
                mainState = 'NotTargeted'
                mainAction = 'None'
                symbolsState = 'NotTargeted'
                symbolsAction = 'None'
            }
        })
        artifacts = $identities
    }
    if ($Mutation -in @(
            'fixture-canonical','fixture-authority-tamper',
            'fixture-nonexistent-archive')) {
        $fixtureRoot = Join-Path $root 'fixture'
        [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
        $fixtureSnapshot = New-SharpProofPublicationInputSnapshot `
            -PackageSource $root -FixtureDirectory $fixtureRoot
        $plan.publicationDestination.mode = 'fixture'
        $plan.publicationDestination.fixture =
            Get-SharpProofPublicationFixtureAuthority `
                -FixtureDirectory $fixtureRoot `
                -InputSnapshot $fixtureSnapshot
        foreach ($package in $plan.packages) {
            $package.availabilityMode = 'fixture'
            $package.fixtureState = 'FixtureAbsent'
            $package.mainState = 'FixtureAbsent'
            $package.mainAction = 'Push'
            $package.symbolsState = 'FixtureAbsent'
            $package.symbolsAction = 'Push'
        }
    }
    if ($Mutation -in @('registry-canonical','registry-url-tamper')) {
        $plan.planOnly = $false
        $plan.publicationDestination.mode = 'registry'
        $plan.publicationDestination.mainDestination =
            'https://api.example.test/v3/index.json'
        $plan.publicationDestination.symbolDestination =
            'https://api.example.test/v3/index.json'
        $plan.publicationDestination.packageBaseAddress =
            'https://api.example.test/v3-flatcontainer'
        foreach ($package in $plan.packages) {
            $normalizedId = $package.packageId.ToLowerInvariant()
            $package.availabilityMode = 'registry'
            $package.remoteState = 'Absent'
            $package.remoteUrl =
                'https://api.example.test/v3-flatcontainer/' +
                "$normalizedId/$version/" +
                "$normalizedId.$version.nupkg"
            $package.mainState = 'Absent'
            $package.mainAction = 'Push'
            $package.symbolsState = 'Unchecked'
            $package.symbolsAction = 'CollisionOnPush'
        }
    }
    switch ($Mutation) {
        'changed-symbol' { [IO.File]::AppendAllText($packages[0].symbolsPath, 'changed') }
        'stale-manifest' { [IO.File]::AppendAllText($manifestPath, 'changed') }
        'stale-sbom' { [IO.File]::AppendAllText($sbom, 'changed') }
        'missing-identity' { $plan.artifacts = @($plan.artifacts | Select-Object -Skip 1) }
        'duplicate-identity' { $plan.artifacts[1].path = $plan.artifacts[0].path }
        'string-schema' { $plan.schemaVersion = '2' }
        'array-version' { $plan.packageVersion = @($version) }
        'array-commit' { $plan.repositoryCommit = @($commit) }
        'array-artifact-text' {
            $plan.artifacts[0].fileName = @($plan.artifacts[0].fileName)
        }
        'destination-tamper' {
            $plan.publicationDestination.mode = 'registry'
        }
        'package-action-tamper' {
            $plan.packages[0].mainAction = 'Push'
        }
        'fixture-authority-tamper' {
            $plan.publicationDestination.fixture = 'tampered'
        }
        'fixture-nonexistent-archive' {
            $fixture = $plan.publicationDestination.fixture
            $fixture.archives = @([pscustomobject][ordered]@{
                path = Join-Path $fixture.path 'missing.nupkg'
                packageId = $plan.packages[0].packageId
                version = $version
                role = 'main'
            })
            $plan.packages[0].fixtureState = 'FixturePresent'
            $plan.packages[0].mainState = 'FixturePresent'
            $plan.packages[0].mainAction = 'Collision'
        }
        'registry-url-tamper' {
            $id = $plan.packages[0].packageId.ToLowerInvariant()
            $plan.packages[0].remoteUrl =
                'https://attacker.invalid/v3-flatcontainer/' +
                "$id/$version/$id.$version.nupkg"
        }
        'targetless-publish-tamper' {
            $plan.planOnly = $false
        }
        'json-roundtrip' {
            $plan = $plan | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        }
        'decimal-bytes' {
            $plan.artifacts[0].bytes =
                [double]$plan.artifacts[0].bytes + 0.4
        }
        'two-bundle' {
            $first = ($plan.artifacts | ConvertTo-Json -Depth 4)
            [IO.File]::AppendAllText($packages[0].mainPath, 'other')
            $second = @(New-SharpProofPublicationPlanIdentities `
                -Packages @($packages) -Directory $root -Version $version `
                -RepositoryCommit $commit) | ConvertTo-Json -Depth 4
            if ($first -ceq $second) { throw 'Distinct bundle bytes produced one plan identity.' }
            Write-Host 'Publication plan identity fixture passed: two-bundle'
            return
        }
    }
    try {
        Test-SharpProofPublicationPlanIdentity -Plan $plan
        if ($Mutation -eq 'fixture-nonexistent-archive') {
            throw 'Fixture replay accepted a nonexistent archive.'
        }
    }
    catch {
        if ($Mutation -eq 'fixture-nonexistent-archive') {
            if ($_.Exception.Message -notlike
                    '*Fixture publication authority changed*') {
                throw "Fixture replay failed for the wrong reason: $($_.Exception.Message)"
            }
            Write-Host (
                'Publication plan identity fixture passed: ' +
                'fixture-nonexistent-archive rejected')
            return
        }
        throw
    }
    Write-Host "Publication plan identity fixture passed: $Mutation"
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
