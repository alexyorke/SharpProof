Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofMutationBaselineInvocation {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$Filter,
        [Parameter(Mandatory = $true)][string]$Configuration
    )

    if ([string]::IsNullOrWhiteSpace($Project) -or
        [string]::IsNullOrWhiteSpace($Filter) -or
        [string]::IsNullOrWhiteSpace($Configuration)) {
        throw 'Mutation baseline invocation fields must be non-empty.'
    }
    $arguments = @(
        'test', $Project, '-c', $Configuration, '--no-restore',
        '--filter', $Filter, '--logger', 'console;verbosity=minimal')
    $identity = [string]::Join('|', $arguments)
    [pscustomobject]@{
        Project = $Project
        Filter = $Filter
        Configuration = $Configuration
        Identity = $identity
    }
}

function Get-SharpProofMutationBaselinePlan {
    param(
        [Parameter(Mandatory = $true)][object[]]$Mutations,
        [Parameter(Mandatory = $true)][string]$Configuration
    )

    $groups = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
    foreach ($mutation in $Mutations) {
        $invocation = Get-SharpProofMutationBaselineInvocation `
            -Project ([string]$mutation.Project) `
            -Filter ([string]$mutation.Filter) `
            -Configuration $Configuration
        if (-not $groups.ContainsKey($invocation.Identity)) {
            $groups.Add($invocation.Identity, [pscustomobject]@{
                    Invocation = $invocation
                    Mutations = [Collections.Generic.List[object]]::new()
                })
        }
        elseif ($groups[$invocation.Identity].Invocation.Project -cne
                $invocation.Project -or
            $groups[$invocation.Identity].Invocation.Filter -cne
                $invocation.Filter -or
            $groups[$invocation.Identity].Invocation.Configuration -cne
                $invocation.Configuration) {
            throw 'Mutation baseline invocation identities collided.'
        }
        $groups[$invocation.Identity].Mutations.Add($mutation)
    }
    return @($groups.Values | Sort-Object {
            $_.Invocation.Project
        }, {
            $_.Invocation.Filter
        }, {
            $_.Invocation.Configuration
        })
}

function Assert-SharpProofMutationBaselineResult {
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][string]$TrxPath,
        [Parameter(Mandatory = $true)][string]$EvidenceName
    )

    if ($ExitCode -eq 124) {
        throw "Baseline '$EvidenceName' timed out."
    }
    if ($ExitCode -ne 0) {
        throw "Baseline '$EvidenceName' failed with exit code $ExitCode."
    }
    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
        throw "Baseline '$EvidenceName' did not produce TRX evidence."
    }
}

Export-ModuleMember -Function `
    Get-SharpProofMutationBaselineInvocation, `
    Get-SharpProofMutationBaselinePlan, `
    Assert-SharpProofMutationBaselineResult
