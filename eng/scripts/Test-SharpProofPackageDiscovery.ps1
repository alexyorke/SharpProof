[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.PackageTestDiscovery.ps1')

$className = 'Fixture.PackageTests'
$arguments = @{
    ClassNames    = @($className)
    MinimumCounts = @{ $className = 1 }
    Descriptions  = @{ $className = 'Fixture' }
}
$methods = Get-SharpProofPackageMethodsFromDiscovery @arguments -FullyQualifiedNames @(
    'Fixture.PackageTests.First',
    'Fixture.PackageTests.First(1)',
    'Fixture.PackageTests.First(2)',
    'Fixture.PackageTestsExtra.Foreign',
    'Other.PackageTests.Foreign')
if (@($methods[$className]).Count -ne 1 -or $methods[$className][0] -cne 'First') {
    throw 'Package discovery did not preserve exact class ownership and distinct method identity.'
}
$caseSensitiveMethods = Get-SharpProofPackageMethodsFromDiscovery @arguments -FullyQualifiedNames @(
    'Fixture.PackageTests.First',
    'Fixture.PackageTests.First(1)',
    'Fixture.PackageTests.first')
if (@($caseSensitiveMethods[$className]).Count -ne 2 -or
    @($caseSensitiveMethods[$className] | Where-Object { $_ -ceq 'First' }).Count -ne 1 -or
    @($caseSensitiveMethods[$className] | Where-Object { $_ -ceq 'first' }).Count -ne 1) {
    throw 'Package discovery merged distinct case-sensitive C# method identities.'
}
foreach ($names in @(@(), @('Other.PackageTests.First'))) {
    $rejected = $false
    try {
        Get-SharpProofPackageMethodsFromDiscovery @arguments -FullyQualifiedNames $names | Out-Null
    }
    catch {
        if ($_.Exception.Message -cne 'Fixture discovery returned only 0 test methods.') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Missing package test methods were accepted.' }
}
$arguments.MinimumCounts[$className] = 2
try {
    Get-SharpProofPackageMethodsFromDiscovery @arguments -FullyQualifiedNames @('Fixture.PackageTests.First') | Out-Null
    throw 'The package method minimum was not enforced.'
}
catch {
    if ($_.Exception.Message -cne 'Fixture discovery returned only 1 test methods.') { throw }
}
Write-Host 'Package discovery regressions passed: class ownership, parameter cases, cardinality, and minimum counts.'
