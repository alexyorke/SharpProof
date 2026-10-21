[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Package preparation regressions require the canonical Linux container.'
# Run this probe in its own PowerShell process: the helper stubs are process-local.
$packageScript = Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1'
$parseTokens = $null
$parseErrors = $null
$tree = [Management.Automation.Language.Parser]::ParseFile($packageScript, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Package script does not parse.' }
$assignment = $tree.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$preparePackageTest' }, $true)
$expression = $assignment.Right.Find({ param($node) $node -is [Management.Automation.Language.ScriptBlockExpressionAst] }, $true)
if ($null -eq $expression) { throw 'Actual package preparation callback not found.' }
function global:New-SharpProofIsolatedTestOutput {
    param([string]$SourceDirectory, [string]$DestinationDirectory)
    if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { throw 'Missing source output.' }
    return $DestinationDirectory
}
function global:Add-SharpProofCoverageArguments {
    param([Collections.Immutable.ImmutableArray[string]]$ImmutableArguments, [bool]$Enabled, [string]$Settings)
    if ([string]::IsNullOrWhiteSpace($Settings)) { throw 'Missing coverage settings.' }
    if ($Enabled) { return , $ImmutableArguments.Add('--fixture-coverage') }
    return , $ImmutableArguments
}
$fixture = ([ordered]@{
        results                  = '/tmp/fixture-results'
        feed                     = '/tmp/fixture-feed'
        repositoryRoot           = '/tmp/fixture-repository'
        Configuration            = 'Release'
        isolatedOutputRoot       = '/tmp/fixture-isolation'
        testProject              = '/tmp/fixture.csproj'
        testAssembly             = '/tmp/fixture.dll'
        resolvedDotnetHost       = '/usr/share/dotnet/dotnet'
        resolvedCoverageSettings = '/tmp/fixture.runsettings'
        environment              = $null
        parallelism              = 4
    }).AsReadOnly()
# Seed the variable names consumed by the actual extracted callback.
foreach ($fixtureEntry in $fixture.GetEnumerator()) {
    Set-Variable -Name $fixtureEntry.Key -Value $fixtureEntry.Value
}
foreach ($coverageEnabled in @($false, $true)) {
    foreach ($exclusive in @($false, $true)) {
        $callback = $expression.ScriptBlock.GetScriptBlock().GetNewClosure()
        $invocation = & $callback ([pscustomobject]@{ Name = 'fixture'; Filter = 'Name=Fixture'; Exclusive = $exclusive })
        if ($invocation.Environment['SHARPPROOF_PACKAGE_SOURCE'] -ne $fixture['feed']) { throw 'Package source was lost.' }
        if (($invocation.Arguments -contains '--fixture-coverage') -ne $coverageEnabled) { throw 'Coverage collector routing changed.' }
        if ($coverageEnabled) {
            if ($invocation.Environment['UseSharedCompilation'] -cne 'false') { throw 'Instrumented consumers may retain compiler servers.' }
            if ($invocation.Arguments[0] -cne 'test' -or @($invocation.Arguments | Where-Object { $_ -like '-p:OutDir=*fixture/Release/net10.0/' }).Count -ne 1) { throw 'Coverage output isolation was lost.' }
        }
        elseif ($invocation.Environment.ContainsKey('UseSharedCompilation')) { throw 'Non-coverage compilation settings changed.' }
        if (-not $coverageEnabled -and -not $exclusive) {
            if ($invocation.Arguments[0] -cne 'vstest' -or $invocation.Environment['DOTNET_HOST_PATH'] -ne $fixture['resolvedDotnetHost']) { throw 'Direct test host binding changed.' }
        }
    }
}
Write-Host 'Actual package preparation callback passed four coverage/exclusivity modes.'
