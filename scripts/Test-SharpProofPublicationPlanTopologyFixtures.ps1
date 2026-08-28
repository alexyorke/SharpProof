[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'valid-disjoint','existing-output','main-package','symbol-package',
        'manifest','sbom','checksums','fixture-input','relative-dot-alias',
        'absolute-alias','symlink-alias','hardlink-alias','reserved-name',
        'package-subdirectory','fixture-subdirectory',
        'writer-failure','post-write-mutation')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanTopology.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-plan-topology-' + [Guid]::NewGuid().ToString('N'))
$packages = Join-Path $root 'packages'
$fixture = Join-Path $root 'fixture'
$outputRoot = Join-Path $root 'output'
try {
    [IO.Directory]::CreateDirectory($packages) | Out-Null
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $packages 'plans')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'plans')) | Out-Null
    foreach ($name in @(
            'SharpProof.1.0.0-preview.1.nupkg',
            'SharpProof.1.0.0-preview.1.snupkg',
            'SharpProof.Attributes.1.0.0-preview.1.nupkg',
            'SharpProof.Attributes.1.0.0-preview.1.snupkg',
            'SharpProof.Verifier.1.0.0-preview.1.nupkg',
            'SharpProof.Verifier.1.0.0-preview.1.snupkg',
            'SharpProof.release.json','SharpProof.spdx.json','SHA256SUMS')) {
        [IO.File]::WriteAllText((Join-Path $packages $name), "input:$name")
    }
    $fixtureInput = Join-Path $fixture 'remote.json'
    [IO.File]::WriteAllText($fixtureInput, 'fixture')
    $planPath = Join-Path $outputRoot 'publication-plan.json'
    if ($Mutation -in @('existing-output','writer-failure','post-write-mutation')) {
        [IO.File]::WriteAllText($planPath, 'old-plan')
    }
    switch ($Mutation) {
        'main-package' { $planPath = Join-Path $packages 'SharpProof.1.0.0-preview.1.nupkg' }
        'symbol-package' { $planPath = Join-Path $packages 'SharpProof.1.0.0-preview.1.snupkg' }
        'manifest' { $planPath = Join-Path $packages 'SharpProof.release.json' }
        'sbom' { $planPath = Join-Path $packages 'SharpProof.spdx.json' }
        'checksums' { $planPath = Join-Path $packages 'SHA256SUMS' }
        'fixture-input' { $planPath = $fixtureInput }
        'relative-dot-alias' {
            $planPath = Join-Path $packages './SharpProof.release.json'
        }
        'absolute-alias' {
            $planPath = [IO.Path]::GetFullPath((Join-Path $packages 'SHA256SUMS'))
        }
        'symlink-alias' {
            $planPath = Join-Path $outputRoot 'link.json'
            [IO.File]::CreateSymbolicLink(
                $planPath,
                (Join-Path $packages 'SharpProof.spdx.json')) | Out-Null
        }
        'hardlink-alias' {
            $planPath = Join-Path $outputRoot 'hard.json'
            & ln (Join-Path $packages 'SharpProof.release.json') $planPath
            if ($LASTEXITCODE -ne 0) { throw 'Could not create hardlink fixture.' }
        }
        'reserved-name' { $planPath = Join-Path $outputRoot 'SharpProof.release.json' }
        'package-subdirectory' {
            $planPath = Join-Path $packages 'plans/publication-plan.json'
        }
        'fixture-subdirectory' {
            $planPath = Join-Path $fixture 'plans/publication-plan.json'
        }
    }
    $resolved = Resolve-SharpProofPublicationPlanOutput -Path $planPath
    $snapshot = New-SharpProofPublicationInputSnapshot `
        -PackageSource $packages -FixtureDirectory $fixture
    Assert-SharpProofPublicationPlanTopology `
        -OutputPath $resolved -InputSnapshot $snapshot
    $before = if ($Mutation -eq 'writer-failure') {
        { throw 'injected writer failure' }
    } else { $null }
    $after = if ($Mutation -eq 'post-write-mutation') {
        { [IO.File]::AppendAllText(
            (Join-Path $packages 'SharpProof.release.json'), 'changed') }
    } else { $null }
    try {
        Write-SharpProofPublicationPlanAtomic `
            -OutputPath $resolved `
            -Json "{`"schemaVersion`":1}`n" `
            -InputSnapshot $snapshot `
            -BeforePublish $before `
            -AfterPublish $after
    }
    catch {
        if ($Mutation -in @('writer-failure','post-write-mutation')) {
            if ([IO.File]::ReadAllText($resolved) -cne 'old-plan' -or
                @(Get-ChildItem $outputRoot -Filter '.sharpproof-plan-*').Count -ne 0) {
                throw 'Atomic plan failure did not restore and clean owned files.'
            }
        }
        throw
    }
    if ([IO.File]::ReadAllText($resolved) -cne "{`"schemaVersion`":1}`n") {
        throw 'Publication plan bytes are incorrect.'
    }
    Write-Host "Publication plan topology fixture passed: $Mutation"
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
