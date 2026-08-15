[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-release-authority-' + [Guid]::NewGuid().ToString('N'))

function Write-FixtureFile([string]$Path, [string]$Text) {
    $full = Join-Path $fixture $Path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
    [IO.File]::WriteAllText($full, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-FixtureClosure {
    . (Join-Path $fixture 'scripts/Get-SharpProofReleaseAuthorityClosure.ps1')
    return @(Get-SharpProofReleaseAuthorityClosure -RepositoryRoot $fixture)
}

function Get-ClosureDigest {
    $records = @(Get-FixtureClosure | ForEach-Object {
            $_ + "`n" + (Get-FileHash -LiteralPath (Join-Path $fixture $_) `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join "`n"))
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

try {
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Get-SharpProofReleaseAuthorityClosure.ps1') `
        -Destination (New-Item -ItemType Directory -Force (
                Join-Path $fixture 'scripts')).FullName
    Write-FixtureFile '.github/workflows/package-consumers.yml' @'
jobs:
  publish:
    uses: ./.github/actions/build-tooling
'@
    Write-FixtureFile '.github/actions/build-tooling/action.yml' "name: build`n"
    Write-FixtureFile 'eng/container/entrypoint.sh' "pwsh scripts/Invoke-SharpProofContainer.ps1`n"
    Write-FixtureFile 'scripts/Invoke-SharpProofContainer.ps1' @'
& 'scripts/New-SharpProofReleaseEvidence.ps1'
& 'scripts/Test-SharpProofReleaseArtifacts.ps1'
& 'scripts/Publish-SharpProofRelease.ps1'
& 'scripts/Invoke-SharpProofReleaseContainer.ps1'
$manifest = 'SharpProof.Verifier/SharpProof.Verifier.nuspec'
'@
    Write-FixtureFile 'scripts/Invoke-SharpProofReleaseContainer.ps1' @'
& 'scripts/Test-SharpProofReleaseArtifacts.ps1'
& 'scripts/Publish-SharpProofRelease.ps1'
'@
    foreach ($leaf in @(
            'scripts/New-SharpProofReleaseEvidence.ps1',
            'scripts/Test-SharpProofReleaseArtifacts.ps1',
            'scripts/Publish-SharpProofRelease.ps1',
            'scripts/SharpProof.PublicationPlanIdentity.psm1',
            'scripts/Test-SharpProofPublicationPlan.ps1',
            'scripts/Test-SharpProofPublicationPlanIdentityFixtures.ps1')) {
        Write-FixtureFile $leaf "# $leaf`n"
    }
    Write-FixtureFile 'SharpProof.Verifier/SharpProof.Verifier.nuspec' '<package />'
    & git -c init.defaultBranch=master -C $fixture init --quiet
    & git -C $fixture config user.email fixture@sharpproof.test
    & git -C $fixture config user.name 'SharpProof Fixture'
    & git -C $fixture add -- .
    & git -C $fixture commit --quiet -m canonical
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize closure fixture.' }

    $canonical = @(Get-FixtureClosure)
    $requiredLeaves = @(
        '.github/workflows/package-consumers.yml',
        'scripts/New-SharpProofReleaseEvidence.ps1',
        'scripts/Test-SharpProofReleaseArtifacts.ps1',
        'scripts/Publish-SharpProofRelease.ps1',
        'scripts/SharpProof.PublicationPlanIdentity.psm1',
        'scripts/Test-SharpProofPublicationPlan.ps1',
        'scripts/Test-SharpProofPublicationPlanIdentityFixtures.ps1',
        'SharpProof.Verifier/SharpProof.Verifier.nuspec')
    foreach ($leaf in $requiredLeaves) {
        if ($canonical -cnotcontains $leaf) { throw "Canonical closure omitted '$leaf'." }
    }
    $canonicalDigest = Get-ClosureDigest
    foreach ($leaf in $requiredLeaves) {
        $full = Join-Path $fixture $leaf
        $original = [IO.File]::ReadAllText($full)
        [IO.File]::AppendAllText($full, "`nchanged", [Text.UTF8Encoding]::new($false))
        if ((Get-ClosureDigest) -ceq $canonicalDigest -or
            @(& git -C $fixture diff --name-only -- $leaf) -cnotcontains $leaf) {
            throw "Changing '$leaf' did not alter digest and changed-TCB selection."
        }
        [IO.File]::WriteAllText($full, $original, [Text.UTF8Encoding]::new($false))
        Remove-Item -LiteralPath $full
        try { Get-FixtureClosure | Out-Null; throw "Deleting '$leaf' was accepted." }
        catch { if ($_.Exception.Message -like "Deleting '*") { throw } }
        [IO.File]::WriteAllText($full, $original, [Text.UTF8Encoding]::new($false))
        $moved = $full + '.moved'
        Move-Item -LiteralPath $full -Destination $moved
        try { Get-FixtureClosure | Out-Null; throw "Moving '$leaf' was accepted." }
        catch { if ($_.Exception.Message -like "Moving '*") { throw } }
        Move-Item -LiteralPath $moved -Destination $full
    }

    Write-FixtureFile 'scripts/UninvokedDecoy.ps1' "# decoy`n"
    & git -C $fixture add -- scripts/UninvokedDecoy.ps1
    if ((Get-FixtureClosure) -ccontains 'scripts/UninvokedDecoy.ps1') {
        throw 'An uninvoked wrapper decoy entered the closure.'
    }
    Write-FixtureFile 'scripts/AddedAuthority.ps1' "# added`n"
    Add-Content -LiteralPath (Join-Path $fixture 'scripts/Invoke-SharpProofContainer.ps1') `
        -Value "& 'scripts/AddedAuthority.ps1'"
    & git -C $fixture add -- scripts/AddedAuthority.ps1
    if ((Get-FixtureClosure) -cnotcontains 'scripts/AddedAuthority.ps1') {
        throw 'A newly invoked authority did not enter the closure.'
    }
    Add-Content -LiteralPath (Join-Path $fixture 'scripts/New-SharpProofReleaseEvidence.ps1') `
        -Value "& 'scripts/Test-SharpProofReleaseArtifacts.ps1'"
    Add-Content -LiteralPath (Join-Path $fixture 'scripts/Test-SharpProofReleaseArtifacts.ps1') `
        -Value "& 'scripts/New-SharpProofReleaseEvidence.ps1'"
    $cycled = @(Get-FixtureClosure)
    if ($cycled.Count -ne @($cycled | Select-Object -Unique).Count) {
        throw 'A cycle or duplicate invocation duplicated closure paths.'
    }
    . (Join-Path $repositoryRoot 'scripts/Get-SharpProofTcbPaths.ps1')
    $duplicateInventory = [pscustomobject]@{
        trustedKernel = [pscustomobject]@{ paths = @('scripts/leaf.ps1') }
        trustedComputingBase = [pscustomobject]@{
            components = @([pscustomobject]@{
                    name = 'duplicate'
                    paths = @('scripts/leaf.ps1')
                })
        }
    }
    try {
        Get-SharpProofTcbPaths -Contract $duplicateInventory | Out-Null
        throw 'A duplicate TCB path was accepted.'
    }
    catch {
        if ($_.Exception.Message -eq 'A duplicate TCB path was accepted.') {
            throw
        }
    }
    Write-Host 'Release-authority closure fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
