[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Developer reproduction regressions require the canonical container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1') -Force

$commit = 'a' * 40
$image = 'ghcr.io/alexyorke/sharpproof@sha256:' + ('b' * 64)
$checks = 0
foreach ($output in @('/tmp/reproduction-a/packages', '/tmp/independent longer path/checkout-b/packages')) {
    foreach ($name in @('Tool', 'CodeFixes', 'Templates')) {
        $plan = Get-SharpProofDeveloperPackageBuildPlan -Name $name -OutputDirectory $output -RepositoryCommit $commit -ConsumerImage $image
        if ($plan.Length -ne 3 -or ($plan.Phase -join '|') -cne 'restore|build|pack') {
            throw 'Each optional package must restore, build, and then pack in its independent checkout.'
        }
        foreach ($phase in $plan) {
            $arguments = $phase.Arguments
            if ($arguments[0] -cne $phase.Phase -or $arguments[1] -cne "src/SharpProof.$name/SharpProof.$name.csproj") {
                throw 'Developer reproduction changed the selected package project or phase.'
            }
            foreach ($property in @('-p:ContinuousIntegrationBuild=true', '-p:GeneratePackageOnBuild=false',
                    '-p:SharpProofDeveloperPackages=true', '-p:SharpProofPackTool=true',
                    "-p:RepositoryCommit=$commit", "-p:SourceRevisionId=$commit", "-p:SharpProofConsumerImage=$image")) {
                if ($arguments -cnotcontains $property) { throw 'A developer phase lost its exact source or build configuration.' }
            }
            $checks++
        }
        if ($plan[0].Arguments -cnotcontains '--locked-mode' -or $plan[0].Arguments -cnotcontains '-p:Configuration=Release' -or
            $plan[0].Arguments -ccontains '--no-restore' -or $plan[0].Arguments -ccontains '--no-build' -or
            $plan[1].Arguments -cnotcontains '--no-restore' -or $plan[1].Arguments -ccontains '--no-build' -or
            $plan[2].Arguments -cnotcontains '--no-restore' -or $plan[2].Arguments -cnotcontains '--no-build') {
            throw 'Developer reproduction may not skip preparation or rebuild while packing the prepared output.'
        }
        foreach ($phase in @($plan[1], $plan[2])) {
            $configuration = $phase.Arguments.IndexOf('-c')
            if ($configuration -lt 0 -or $phase.Arguments[$configuration + 1] -cne 'Release') {
                throw 'Developer compilation and packing must use Release configuration.'
            }
        }
        $destination = $plan[2].Arguments.IndexOf('-o')
        if ($destination -lt 0 -or $plan[2].Arguments[$destination + 1] -cne $output) {
            throw 'The package output must remain one argument in its independent checkout, including spaces.'
        }
        $checks++
    }
}

foreach ($invalid in @(
        @{ Name = 'tool'; OutputDirectory = '/tmp/packages'; RepositoryCommit = $commit; ConsumerImage = $image },
        @{ Name = 'Tool'; OutputDirectory = ' '; RepositoryCommit = $commit; ConsumerImage = $image },
        @{ Name = 'Tool'; OutputDirectory = '/tmp/packages'; RepositoryCommit = 'master'; ConsumerImage = $image },
        @{ Name = 'Tool'; OutputDirectory = '/tmp/packages'; RepositoryCommit = $commit; ConsumerImage = 'ghcr.io/alexyorke/sharpproof:latest' })) {
    $rejected = $false
    try { $null = Get-SharpProofDeveloperPackageBuildPlan @invalid }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Developer reproduction accepted an unbound package, source, output, or image.' }
    $checks++
}

$fixture = "error NU1000: failed https://fixture-user:fixture-password@example.invalid/feed?token=fixture-query`nAuthorization: Bearer fixture-header`naccess_token=fixture-access`nfinal diagnostic"
$redacted = Get-SharpProofReproductionDiagnosticText -Text $fixture
$summary = Get-SharpProofReproductionDiagnosticText -Text $fixture -Tail
foreach ($diagnostic in @($redacted, $summary)) {
    if ($diagnostic -match 'fixture-(?:user|password|query|header|access)' -or -not $diagnostic.Contains('error NU1000: failed') -or
        -not $diagnostic.EndsWith('final diagnostic', [StringComparison]::Ordinal)) {
        throw 'Failure diagnostics must retain the error while redacting URL and credential values.'
    }
    $checks++
}
$tail = Get-SharpProofReproductionDiagnosticText -Text ((0..39 | ForEach-Object { 'line-' + $_ + ':' + ('x' * 300) }) -join "`n") -Tail
if ($tail.Length -gt 4000 -or @($tail -split "`n").Count -gt 20 -or $tail.Contains('line-0:') -or -not $tail.Contains('line-39:')) {
    throw 'Failure diagnostics must preserve a bounded tail instead of dumping the full build log.'
}
if ((Get-SharpProofReproductionDiagnosticText -Text '' -Tail) -cne 'No diagnostic output was captured.') {
    throw 'An empty failure log must have an explicit diagnostic result.'
}
$checks += 2
Write-Host "Developer reproduction regressions passed: $checks preparation, identity, independent-path, and redacted diagnostic controls."
