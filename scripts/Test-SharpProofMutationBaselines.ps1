[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationBaselines.psm1') -Force

function Assert-Throws {
    param([scriptblock]$Action, [string]$Message)
    try { & $Action }
    catch { return }
    throw $Message
}

function New-Mutation([string]$Name, [string]$Project, [string]$Filter) {
    [pscustomobject]@{ Name = $Name; Project = $Project; Filter = $Filter }
}

$first = New-Mutation first Project.Tests 'FullyQualifiedName~Fixture.First'
$second = New-Mutation second Project.Tests 'FullyQualifiedName~Fixture.Second'
$duplicate = New-Mutation duplicate Project.Tests 'FullyQualifiedName~Fixture.Second'
$plan = @(Get-SharpProofMutationBaselinePlan `
        -Mutations @($first, $second, $duplicate) -Configuration Release)
if ($plan.Count -ne 2 -or
    @($plan | Where-Object { $_.Mutations.Count -eq 2 }).Count -ne 1 -or
    @($plan.Invocation.Filter) -contains
        'FullyQualifiedName~Fixture.First|FullyQualifiedName~Fixture.Second') {
    throw 'Focused baselines were incorrectly batched or duplicate filters were not shared.'
}

$reversed = @(Get-SharpProofMutationBaselinePlan `
        -Mutations @($duplicate, $second, $first) -Configuration Release)
if (($plan.Invocation.Sha256 -join ',') -cne
    ($reversed.Invocation.Sha256 -join ',')) {
    throw 'Parallel baseline planning is not canonical.'
}

$otherProject = Get-SharpProofMutationBaselineInvocation `
    -Project Other.Tests -Filter $second.Filter -Configuration Release
$otherConfiguration = Get-SharpProofMutationBaselineInvocation `
    -Project $second.Project -Filter $second.Filter -Configuration Debug
if ($otherProject.Sha256 -eq $plan[1].Invocation.Sha256 -or
    $otherConfiguration.Sha256 -eq $plan[1].Invocation.Sha256) {
    throw 'Project or configuration was omitted from baseline identity.'
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-baseline-' + [Guid]::NewGuid().ToString('N') + '.trx')
try {
    [IO.File]::WriteAllText($fixture, '<TestRun />', [Text.UTF8Encoding]::new($false))
    Assert-SharpProofMutationBaselineResult `
        -ExitCode 0 -TrxPath $fixture -EvidenceName valid
    Assert-Throws {
        Assert-SharpProofMutationBaselineResult `
            -ExitCode 1 -TrxPath $fixture -EvidenceName failed
    } 'A failed focused baseline was accepted.'
    Assert-Throws {
        Assert-SharpProofMutationBaselineResult `
            -ExitCode 124 -TrxPath $fixture -EvidenceName timeout
    } 'A timed-out focused baseline was accepted.'
    Assert-Throws {
        Assert-SharpProofMutationBaselineResult `
            -ExitCode 0 -TrxPath ($fixture + '.missing') -EvidenceName missing
    } 'A baseline without TRX evidence was accepted.'

    # A batched invocation could pass after First initializes shared state. The
    # exact plan necessarily invokes Second alone, exposing the setup failure.
    $outcomes = @{
        'FullyQualifiedName~Fixture.First|FullyQualifiedName~Fixture.Second' = 0
        'FullyQualifiedName~Fixture.First' = 0
        'FullyQualifiedName~Fixture.Second' = 1
    }
    $secondInvocation = $plan | Where-Object {
        $_.Invocation.Filter -eq 'FullyQualifiedName~Fixture.Second'
    }
    Assert-Throws {
        Assert-SharpProofMutationBaselineResult `
            -ExitCode $outcomes[$secondInvocation.Invocation.Filter] `
            -TrxPath $fixture -EvidenceName order-contamination
    } 'Focused setup/order contamination was hidden by batching.'
}
finally {
    Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
}

Write-Host 'Mutation baseline invocation fixtures passed.'
