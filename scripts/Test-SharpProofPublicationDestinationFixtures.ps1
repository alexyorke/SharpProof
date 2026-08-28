[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'registry-inherited','registry-distinct','targetless','fixture','http',
        'relative','userinfo','query','fragment','symbol-without-main',
        'fixture-uri-conflict','missing-fixture','changed-fixture',
        'removed-symbol-projection','actions-targetless','actions-fixture',
        'actions-registry-unchecked','actions-registry-absent',
        'actions-registry-exact','actions-registry-collision',
        'actions-symbol-preflight','actions-swapped',
        'actions-removed-projection','mocked-main-missing',
        'mocked-main-collision',
        'mocked-main-exists','mocked-main-error','mocked-main-query-base',
        'zero-symbol-preflight',
        'fixture-empty','fixture-foreign','fixture-main-case-collision',
        'fixture-symbol-case-collision','fixture-arbitrary-name',
        'fixture-wrong-id','fixture-wrong-version','fixture-nested-collision',
        'fixture-malformed','fixture-cross-role','fixture-duplicate')]
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

function New-FixtureArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)]
        [ValidateSet('main','symbols','cross')][string]$Role
    )
    $content = Join-Path $root ([Guid]::NewGuid().ToString('N'))
    try {
        [IO.Directory]::CreateDirectory($content) | Out-Null
        [IO.File]::WriteAllText(
            (Join-Path $content "$Id.nuspec"),
            "<package><metadata><id>$Id</id><version>$Version</version></metadata></package>")
        if ($Role -in @('main','cross')) {
            $dll = Join-Path $content 'lib/net8.0/payload.dll'
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dll)) |
                Out-Null
            [IO.File]::WriteAllText($dll, 'managed')
        }
        if ($Role -in @('symbols','cross')) {
            $pdb = Join-Path $content 'lib/net8.0/payload.pdb'
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($pdb)) |
                Out-Null
            [IO.File]::WriteAllText($pdb, 'symbols')
        }
        [IO.Compression.ZipFile]::CreateFromDirectory($content, $Path)
    }
    finally {
        if (Test-Path -LiteralPath $content) {
            Remove-Item -LiteralPath $content -Recurse -Force
        }
    }
}

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
        'fixture-empty' { $main = $null; $fixturePath = $fixture }
        'fixture-foreign' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'foreign.nupkg') `
                Other.Package 9.9.9 main
        }
        'fixture-wrong-id' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'wrong-id.nupkg') `
                Other.Package 1.0.0-preview.1 main
        }
        'fixture-wrong-version' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'wrong-version.nupkg') `
                SharpProof 9.9.9 main
        }
        'fixture-main-case-collision' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'renamed.bin.nupkg') `
                sharpproof 1.0.0-PREVIEW.1 main
        }
        'fixture-symbol-case-collision' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'symbols-any-name.snupkg') `
                SHARPPROOF 1.0.0-preview.1 symbols
        }
        'fixture-arbitrary-name' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'totally-renamed.nupkg') `
                SharpProof 1.0.0-preview.1 main
        }
        'fixture-nested-collision' {
            $main = $null; $fixturePath = $fixture
            $nested = Join-Path $fixture 'nested/feed'
            [IO.Directory]::CreateDirectory($nested) | Out-Null
            New-FixtureArchive (Join-Path $nested 'nested-package.nupkg') `
                SharpProof 1.0.0-preview.1 main
        }
        'fixture-malformed' {
            $main = $null; $fixturePath = $fixture
            [IO.File]::WriteAllText((Join-Path $fixture 'bad.nupkg'), 'not zip')
        }
        'fixture-cross-role' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'cross.nupkg') `
                SharpProof 1.0.0-preview.1 cross
        }
        'fixture-duplicate' {
            $main = $null; $fixturePath = $fixture
            New-FixtureArchive (Join-Path $fixture 'one.nupkg') `
                SharpProof 1.0.0-preview.1 main
            New-FixtureArchive (Join-Path $fixture 'two.nupkg') `
                sharpproof 1.0.0-PREVIEW.1 main
        }
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
    if ($Mutation.StartsWith('fixture-', [StringComparison]::Ordinal) -and
        $Mutation -notin @('fixture-uri-conflict')) {
        $catalog = @($authority.fixture.archives)
        $state = Get-SharpProofPublicationFixturePackageState `
            -Catalog $catalog `
            -PackageId 'SharpProof' `
            -Version '1.0.0-preview.1'
        $expectedMain = if ($Mutation -in @(
                'fixture-main-case-collision','fixture-arbitrary-name',
                'fixture-nested-collision')) {
            'FixturePresent'
        }
        else { 'FixtureAbsent' }
        $expectedSymbols = if (
            $Mutation -eq 'fixture-symbol-case-collision') {
            'FixturePresent'
        }
        else { 'FixtureAbsent' }
        if ($state.mainState -cne $expectedMain -or
            $state.symbolsState -cne $expectedSymbols) {
            throw 'Fixture package identity state was not derived from archive content.'
        }
        $fixtureAction = New-SharpProofPublicationActionAuthority `
            -Mode fixture `
            -FixtureMainState $state.mainState `
            -FixtureSymbolsState $state.symbolsState
        if (($expectedMain -ceq 'FixturePresent' -and
                $fixtureAction.mainAction -cne 'Collision') -or
            ($expectedMain -ceq 'FixtureAbsent' -and
                $fixtureAction.mainAction -cne 'Push') -or
            ($expectedSymbols -ceq 'FixturePresent' -and
                $fixtureAction.symbolsAction -cne 'Collision') -or
            ($expectedSymbols -ceq 'FixtureAbsent' -and
                $fixtureAction.symbolsAction -cne 'Push')) {
            throw 'Fixture package identity was not projected into exact actions.'
        }
    }
    if ($Mutation.StartsWith('actions-', [StringComparison]::Ordinal)) {
        $mode = switch ($Mutation) {
            'actions-targetless' { 'targetless' }
            'actions-fixture' { 'fixture' }
            default { 'registry' }
        }
        $mainState = switch ($Mutation) {
            'actions-registry-unchecked' { 'Unchecked' }
            'actions-registry-exact' { 'ExactPresent' }
            'actions-registry-collision' { 'Collision' }
            default {
                if ($mode -ceq 'registry') { 'Absent' } else { $null }
            }
        }
        $action = New-SharpProofPublicationActionAuthority `
            -Mode $mode -MainState $mainState
        if ($Mutation -eq 'actions-symbol-preflight') {
            $action.symbolsAction = 'PreflightThenPush'
        }
        elseif ($Mutation -eq 'actions-swapped') {
            $temporary = $action.mainAction
            $action.mainAction = $action.symbolsAction
            $action.symbolsAction = $temporary
        }
        elseif ($Mutation -eq 'actions-removed-projection') {
            $action.PSObject.Properties.Remove('symbolsState')
        }
        Test-SharpProofPublicationActionAuthority `
            -Authority $action -Mode $mode -MainState $mainState
    }
    if ($Mutation.StartsWith('mocked-main-', [StringComparison]::Ordinal) -or
        $Mutation -eq 'zero-symbol-preflight') {
        $script:preflightCalls = [Collections.Generic.List[string]]::new()
        $status = switch ($Mutation) {
            'mocked-main-exists' { 200 }
            'mocked-main-collision' { 200 }
            'mocked-main-error' { 503 }
            default { 404 }
        }
        $package = [pscustomobject]@{
            packageId = 'SharpProof'
            version = '1.0.0-preview.1'
            mainPath = Join-Path $packages 'SharpProof.1.0.0-preview.1.nupkg'
        }
        [IO.File]::WriteAllText($package.mainPath, 'expected package bytes')
        $baseAddress = if ($Mutation -eq 'mocked-main-query-base') {
            'https://packages.example.test/v3-flatcontainer?q=1'
        }
        else { 'https://packages.example.test/v3-flatcontainer' }
        $result = Invoke-SharpProofMainPackagePreflight `
            -Package $package `
            -BaseAddress $baseAddress `
            -Get {
                param($uri, $outputPath)
            $script:preflightCalls.Add([string]$uri)
            if ($status -eq 200) {
                $remoteBytes = if ($Mutation -eq 'mocked-main-exists') {
                    [IO.File]::ReadAllBytes($package.mainPath)
                }
                else {
                    [Text.Encoding]::UTF8.GetBytes('different package bytes')
                }
                [IO.File]::WriteAllBytes($outputPath, $remoteBytes)
            }
            return [pscustomobject]@{ StatusCode = $status }
        }
        $expectedState = switch ($Mutation) {
            'mocked-main-exists' { 'ExactPresent' }
            'mocked-main-collision' { 'Collision' }
            default { 'Absent' }
        }
        if ($result.state -cne $expectedState -or
            $script:preflightCalls.Count -ne 1 -or
            $script:preflightCalls[0] -notmatch '\.nupkg$' -or
            $script:preflightCalls[0] -match '\.snupkg$') {
            throw 'Only the exact main package may be preflighted.'
        }
    }
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
