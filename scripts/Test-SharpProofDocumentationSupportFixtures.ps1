[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'clean',
        'stale-win-x64',
        'package-version-drift',
        'support-drift',
        'stale-contract-api-silence',
        'old-eight-mutation-lanes',
        'wrong-container-cpu',
        'wrong-container-memory',
        'missing-resource-claim',
        'duplicate-resource-claim',
        'resource-claim-case',
        'resource-claim-spacing',
        'catalog-resource-drift',
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
    'catalog-resource-drift' { 'eng\acceptance\contract.json' }
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
$readmePath = Join-Path $repositoryRoot $relativePath
$originalBytes = [IO.File]::ReadAllBytes($readmePath)
try {
    $text = [Text.Encoding]::UTF8.GetString($originalBytes)
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
            $text = $text.Replace(
                $version,
                '99.99.99-stale',
                [StringComparison]::Ordinal)
        }
        'support-drift' {
            $text += "`nThe verifier is supported only on Windows x64.`n"
        }
        'stale-contract-api-silence' {
            $text += (
                "`nA readable wrong-payload SharpProof.Attributes assembly " +
                "disables contract analysis without a diagnostic.`n")
        }
        'old-eight-mutation-lanes' {
            $text = $text.Replace(
                'Trusted mutations use 4 deterministic weighted lanes.',
                'Trusted mutations use 8 deterministic weighted lanes.',
                [StringComparison]::Ordinal)
        }
        'wrong-container-cpu' {
            $text = $text.Replace(
                'The default container budget is 16 CPUs and 40960 MiB.',
                'The default container budget is 12 CPUs and 40960 MiB.',
                [StringComparison]::Ordinal)
        }
        'wrong-container-memory' {
            $text = $text.Replace(
                'The default container budget is 16 CPUs and 40960 MiB.',
                'The default container budget is 16 CPUs and 32768 MiB.',
                [StringComparison]::Ordinal)
        }
        'missing-resource-claim' {
            $text = $text.Replace(
                'The default container budget is 16 CPUs and 40960 MiB.',
                '',
                [StringComparison]::Ordinal)
        }
        'duplicate-resource-claim' {
            $text += "`nThe default container budget is 16 CPUs and 40960 MiB.`n"
        }
        'resource-claim-case' {
            $text = $text.Replace(
                'The default container budget is 16 CPUs and 40960 MiB.',
                'The default container budget is 16 cpus and 40960 MiB.',
                [StringComparison]::Ordinal)
        }
        'resource-claim-spacing' {
            $text = $text.Replace(
                'The default container budget is 16 CPUs and 40960 MiB.',
                'The default container budget is 16 CPUs  and 40960 MiB.',
                [StringComparison]::Ordinal)
        }
        'catalog-resource-drift' {
            $text = $text.Replace(
                '"mutationParallelism": 4',
                '"mutationParallelism": 5',
                [StringComparison]::Ordinal)
        }
        'check-plan-drift' {
            $text = $text.Replace(
                ('The default Debug check performs one Debug solution build, ' +
                 'one additional Debug package-test build, and 3'),
                ('The default Debug check reuses one build for every package ' +
                 'and test phase, with 3'),
                [StringComparison]::Ordinal)
        }
        'missing-vacuous-entry' {
            $text = $text.Replace(
                '| `VacuousEntry` | Contradictory entry preconditions prove the effect claim vacuously |',
                '',
                [StringComparison]::Ordinal)
        }
        'wrong-unavailable-meaning' {
            $text = $text.Replace(
                '| `Unavailable` | An `Unknown` effect claim for any schema-admitted unknown reason when no more specific certainty applies |',
                '| `Unavailable` | Only backend infrastructure failures |',
                [StringComparison]::Ordinal)
        }
        'extra-certainty-member' {
            $text = $text.Replace(
                '| `VacuousEntry` | Contradictory entry preconditions prove the effect claim vacuously |',
                "| ``VacuousEntry`` | Contradictory entry preconditions prove the effect claim vacuously |`n| ``Future`` | Fabricated member |",
                [StringComparison]::Ordinal)
        }
        'certainty-member-case' {
            $text = $text.Replace(
                '`VacuousEntry`',
                '`vacuousEntry`',
                [StringComparison]::Ordinal)
        }
        'certainty-member-order' {
            $text = $text.Replace(
                "| ``Unavailable`` | An ``Unknown`` effect claim for any schema-admitted unknown reason when no more specific certainty applies |`n| ``VacuousEntry`` | Contradictory entry preconditions prove the effect claim vacuously |",
                "| ``VacuousEntry`` | Contradictory entry preconditions prove the effect claim vacuously |`n| ``Unavailable`` | An ``Unknown`` effect claim for any schema-admitted unknown reason when no more specific certainty applies |",
                [StringComparison]::Ordinal)
        }
        'protocol-certainty-schema-drift' {
            $text = $text.Replace(
                '["Proven","None","VacuousEntry"],',
                '["Proven","None","VacuousEntry"],["Unknown","None","Unavailable"],',
                [StringComparison]::Ordinal)
        }
    }
    [IO.File]::WriteAllText(
        $readmePath,
        $text,
        [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'Generate-Readme.ps1') -Verify
}
finally {
    [IO.File]::WriteAllBytes($readmePath, $originalBytes)
}
