[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'registry-inherited','registry-distinct','targetless','fixture','http',
        'relative','userinfo','query','fragment','symbol-without-main',
        'fixture-uri-conflict','missing-fixture','changed-fixture',
        'removed-symbol-projection')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanTopology.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PublicationDestination.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-destination-' + [Guid]::NewGuid().ToString('N'))
$packages = Join-Path $root 'packages'
$fixture = Join-Path $root 'fixture'
try {
    [IO.Directory]::CreateDirectory($packages) | Out-Null
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $packages 'SharpProof.release.json'), '{}')
    [IO.File]::WriteAllText((Join-Path $fixture 'catalog.json'), '{}')
    $main = 'https://api.example.test/v3/index.json'
    $symbols = $null
    $fixturePath = $null
    switch ($Mutation) {
        'registry-distinct' { $symbols = 'https://symbols.example.test/v3/index.json' }
        'targetless' { $main = $null }
        'fixture' { $main = $null; $fixturePath = $fixture }
        'http' { $main = 'http://api.example.test/v3/index.json' }
        'relative' { $main = 'feeds/index.json' }
        'userinfo' { $main = 'https://user:pass@api.example.test/v3/index.json' }
        'query' { $main = 'https://api.example.test/v3/index.json?q=1' }
        'fragment' { $main = 'https://api.example.test/v3/index.json#x' }
        'symbol-without-main' {
            $main = $null
            $symbols = 'https://symbols.example.test/v3/index.json'
        }
        'fixture-uri-conflict' { $fixturePath = $fixture }
        'missing-fixture' {
            $main = $null
            $fixturePath = Join-Path $root 'missing'
        }
        'changed-fixture' { $main = $null; $fixturePath = $fixture }
        'removed-symbol-projection' { $symbols = 'https://symbols.example.test/v3/index.json' }
    }
    $snapshot = New-SharpProofPublicationInputSnapshot `
        -PackageSource $packages -FixtureDirectory $fixturePath
    $authority = New-SharpProofPublicationDestinationAuthority `
        -Source $main -SymbolSource $symbols `
        -FixtureDirectory $fixturePath -InputSnapshot $snapshot
    if ($Mutation -eq 'changed-fixture') {
        [IO.File]::AppendAllText((Join-Path $fixture 'catalog.json'), 'changed')
    }
    if ($Mutation -eq 'removed-symbol-projection') {
        $authority.PSObject.Properties.Remove('symbolDestination')
    }
    Test-SharpProofPublicationDestinationAuthority `
        -Authority $authority -Source $main -SymbolSource $symbols `
        -FixtureDirectory $fixturePath -InputSnapshot $snapshot
    if ($Mutation -eq 'registry-inherited' -and
        [string]$authority.mainDestination -cne
            [string]$authority.symbolDestination) {
        throw 'Inherited symbol destination was not projected exactly.'
    }
    Write-Host "Publication destination fixture passed: $Mutation"
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
