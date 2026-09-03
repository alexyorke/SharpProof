[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'clean',
        'stale-win-x64',
        'package-version-drift',
        'support-drift',
        'stale-contract-api-silence',
        'stale-language-subset-path',
        'old-eight-mutation-lanes',
        'wrong-container-cpu',
        'wrong-container-memory',
        'missing-resource-claim',
        'duplicate-resource-claim',
        'resource-claim-case',
        'resource-claim-spacing',
        'catalog-resource-drift',
        'duplicate-acceptance-property',
        'check-plan-drift',
        'missing-vacuous-entry',
        'wrong-unavailable-meaning',
        'extra-certainty-member',
        'certainty-member-case',
        'certainty-member-order',
        'protocol-certainty-schema-drift')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$relativePath = switch ($Mutation) {
    'stale-contract-api-silence' { 'docs\diagnostic-examples.md' }
    'stale-language-subset-path' { 'docs\README.md' }
    'catalog-resource-drift' { 'eng\acceptance\contract.json' }
    'duplicate-acceptance-property' { 'eng\acceptance\contract.json' }
    'protocol-certainty-schema-drift' {
        'SharpProof.Worker.Protocol\ProtocolModel.schema.json'
    }
    { $_ -in @(
            'missing-vacuous-entry',
            'wrong-unavailable-meaning',
            'extra-certainty-member',
            'certainty-member-case',
            'certainty-member-order') } {
        'docs\unknown-reasons.md'
    }
    { $_ -in @(
            'old-eight-mutation-lanes',
            'wrong-container-cpu',
            'wrong-container-memory',
            'missing-resource-claim',
            'duplicate-resource-claim',
            'resource-claim-case',
            'resource-claim-spacing') } {
        'docs\container-development.md'
    }
    default { 'README.md' }
}
$sourcePath = Join-Path $repositoryRoot $relativePath
$sourceBytes = [IO.File]::ReadAllBytes($sourcePath)
$overridePath = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-documentation-' + [Guid]::NewGuid().ToString('N') + '.txt')
$acceptanceContract = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng\acceptance\contract.json') -Raw |
    ConvertFrom-Json
$containerMemoryMiB = [int]$acceptanceContract.container.defaultMemoryMiB
$containerResourceClaim =
    "Containers use all CPUs available to Docker and up to " +
    "$containerMemoryMiB MiB by default."

function Replace-Required {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InputText,

        [Parameter(Mandatory = $true)]
        [string]$OldValue,

        [Parameter(Mandatory = $true)]
        [string]$NewValue
    )

    $replacement = $InputText.Replace(
        $OldValue,
        $NewValue,
        [StringComparison]::Ordinal)
    if ($replacement -ceq $InputText) {
        throw (
            "Mutation '$Mutation' could not apply its expected text " +
            "replacement in '$relativePath'.")
    }
    return $replacement
}

try {
    $text = [Text.Encoding]::UTF8.GetString($sourceBytes)
    switch ($Mutation) {
        'stale-win-x64' {
            $text += "`nSharpProof.Verifier.Win-x64 is supported.`n"
        }
        'package-version-drift' {
            [xml]$release = Get-Content -LiteralPath (
                Join-Path $repositoryRoot 'SharpProof.Release.props') -Raw
            $prefix = [string]$release.Project.PropertyGroup.SharpProofVersionPrefix
            $version = ([string]$release.Project.PropertyGroup.SharpProofPackageVersion).
                Replace('$(SharpProofVersionPrefix)', $prefix)
            $text = Replace-Required `
                -InputText $text `
                -OldValue $version `
                -NewValue '99.99.99-stale'
        }
        'support-drift' {
            $text += "`nThe verifier is supported only on Windows x64.`n"
        }
        'stale-contract-api-silence' {
            $text += (
                "`nA readable wrong-payload SharpProof.Attributes assembly " +
                "disables contract analysis without a diagnostic.`n")
        }
        'stale-language-subset-path' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue 'SharpProof.Analyzer.Core/LanguageSubsetGate.cs' `
                -NewValue 'SharpProof.Analyzer/LanguageSubsetGate.cs'
        }
        'old-eight-mutation-lanes' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue 'Trusted mutations use 4 deterministic weighted lanes.' `
                -NewValue 'Trusted mutations use 8 deterministic weighted lanes.'
        }
        'wrong-container-cpu' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue $containerResourceClaim `
                -NewValue 'Containers use 12 CPUs and up to 40960 MiB by default.'
        }
        'wrong-container-memory' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue $containerResourceClaim `
                -NewValue 'Containers use all CPUs available to Docker and up to 32768 MiB by default.'
        }
        'missing-resource-claim' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue $containerResourceClaim `
                -NewValue ''
        }
        'duplicate-resource-claim' {
            $text += "`n$containerResourceClaim`n"
        }
        'resource-claim-case' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue $containerResourceClaim `
                -NewValue 'Containers use all cpus available to Docker and up to 40960 MiB by default.'
        }
        'resource-claim-spacing' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue $containerResourceClaim `
                -NewValue 'Containers use all CPUs  available to Docker and up to 40960 MiB by default.'
        }
        'catalog-resource-drift' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue '"mutationParallelism": 4' `
                -NewValue '"mutationParallelism": 5'
        }
        'duplicate-acceptance-property' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue '"mutationParallelism": 4' `
                -NewValue '"mutationParallelism": 99,`n        "mutationParallelism": 4'
        }
        'check-plan-drift' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue ('The default Debug check concurrently performs one Debug ' +
                    'solution build and one Release package-product build, then ' +
                    'runs 3') `
                -NewValue ('The default Debug check reuses one build for every package ' +
                    'and test phase, with 3')
        }
        'missing-vacuous-entry' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue '| `VacuousEntry` | Contradictory entry preconditions prove the effect claim vacuously |' `
                -NewValue ''
        }
        'wrong-unavailable-meaning' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue '| `Unavailable` | An `Unknown` effect claim for any schema-admitted unknown reason when no more specific certainty applies |' `
                -NewValue '| `Unavailable` | Only backend infrastructure failures |'
        }
        'extra-certainty-member' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue '| `VacuousEntry` | Contradictory entry preconditions prove the effect claim vacuously |' `
                -NewValue "| ``VacuousEntry`` | Contradictory entry preconditions prove the effect claim vacuously |`n| ``Future`` | Fabricated member |"
        }
        'certainty-member-case' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue '`VacuousEntry`' `
                -NewValue '`vacuousEntry`'
        }
        'certainty-member-order' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue "| ``Unavailable`` | An ``Unknown`` effect claim for any schema-admitted unknown reason when no more specific certainty applies |`n| ``VacuousEntry`` | Contradictory entry preconditions prove the effect claim vacuously |" `
                -NewValue "| ``VacuousEntry`` | Contradictory entry preconditions prove the effect claim vacuously |`n| ``Unavailable`` | An ``Unknown`` effect claim for any schema-admitted unknown reason when no more specific certainty applies |"
        }
        'protocol-certainty-schema-drift' {
            $text = Replace-Required `
                -InputText $text `
                -OldValue '["Proven","None","VacuousEntry"],' `
                -NewValue '["Proven","None","VacuousEntry"],["Unknown","None","Unavailable"],'
        }
    }
    [IO.File]::WriteAllText(
        $overridePath,
        $text,
        [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'Test-SharpProofReadme.ps1') `
        -TextOverrideRelativePath $relativePath `
        -TextOverridePath $overridePath
}
finally {
    if ([IO.File]::Exists($overridePath)) {
        [IO.File]::Delete($overridePath)
    }
}
