[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
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
        'registry-inherited' { }
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
        'actions-targetless' { }
        'actions-fixture' { }
        'actions-registry-absent' { }
        'actions-registry-unchecked' { }
        'actions-registry-verified' { }
        'mocked-main-missing' { }
        'mocked-main-exists' { }
        'mocked-main-exists-match' {
            $main = 'https://api.nuget.org/v3/index.json'
            $symbols = $main
        }
        'mocked-main-exists-mismatch' {
            $main = 'https://api.nuget.org/v3/index.json'
            $symbols = $main
        }
        'mocked-main-error' { }
        'mocked-main-query-base' { }
        default {
            throw "Unknown publication destination mutation: $Mutation"
        }
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
        $mainState = if ($Mutation -eq 'actions-registry-unchecked') {
            'Unchecked'
        }
        elseif ($Mutation -eq 'actions-registry-verified') {
            'VerifiedPresent'
        }
        elseif ($mode -ceq 'registry') { 'Absent' }
        else { $null }
        $action = New-SharpProofPublicationActionAuthority `
            -Mode $mode -MainState $mainState
        if ($mode -ceq 'registry' -and
            ($action.symbolsState -cne 'Unchecked' -or
                $action.symbolsAction -cne 'CollisionOnPush')) {
            throw 'Registry symbols must remain unchecked until collision-aware push.'
        }
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
    if ($Mutation.StartsWith('mocked-main-', [StringComparison]::Ordinal)) {
        $script:preflightCalls = [Collections.Generic.List[string]]::new()
        $localMainPath = Join-Path $root 'validated-main.nupkg'
        $localSymbolsPath = Join-Path $root 'validated-symbols.snupkg'
        [IO.File]::WriteAllBytes(
            $localMainPath, [Text.Encoding]::UTF8.GetBytes('validated-main'))
        [IO.File]::WriteAllBytes(
            $localSymbolsPath, [Text.Encoding]::UTF8.GetBytes('validated-symbols'))
        $status = switch ($Mutation) {
            { $_ -in @(
                'mocked-main-exists',
                'mocked-main-exists-match',
                'mocked-main-exists-mismatch') } { 200 }
            'mocked-main-error' { 503 }
            default { 404 }
        }
        $package = [pscustomobject]@{
            packageId = 'SharpProof'
            version = '1.0.0-preview.1'
            mainPath = $localMainPath
            symbolsPath = $localSymbolsPath
        }
        $baseAddress = if ($Mutation -eq 'mocked-main-query-base') {
            'https://packages.example.test/v3-flatcontainer?q=1'
        }
        else { 'https://packages.example.test/v3-flatcontainer' }
        $resources = if ($Mutation -in @(
                'mocked-main-exists-match',
                'mocked-main-exists-mismatch')) {
            @([pscustomobject]@{
                '@type' = 'SymbolPackagePublish/4.9.0'
                '@id' = 'https://www.nuget.org/api/v2/symbolpackage'
            })
        }
        else { @() }
        $canResume = {
            Test-SharpProofNuGetOrgSymbolPublishCapability `
                -MainDestination $main `
                -SymbolDestination $(if ($null -eq $symbols) { $main } else { $symbols }) `
                -Resources $resources
        }
        $preflightError = $null
        try {
            $result = Invoke-SharpProofMainPackagePreflight `
                -Package $package `
                -BaseAddress $baseAddress `
                -Get {
                    param($uri, $method, $outputPath)
                    $script:preflightCalls.Add("$method|$uri")
                    if ($method -ceq 'Get') {
                        $remoteBytes = if (
                            $Mutation -eq 'mocked-main-exists-mismatch') {
                            [Text.Encoding]::UTF8.GetBytes('different-main')
                        }
                        else {
                            [IO.File]::ReadAllBytes($localMainPath)
                        }
                        [IO.File]::WriteAllBytes($outputPath, $remoteBytes)
                    }
                    return [pscustomobject]@{ StatusCode = $status }
                } `
                -CanReuseExisting $canResume
        }
        catch {
            $preflightError = $_
        }
        if ($Mutation -eq 'mocked-main-missing') {
            if ($null -ne $preflightError -or
                $result.state -cne 'Absent' -or
                $script:preflightCalls.Count -ne 1) {
                throw 'An absent main package must remain publishable.'
            }
            $pushes = [Collections.Generic.List[string]]::new()
            $action = New-SharpProofPublicationActionAuthority `
                -Mode registry -MainState $result.state
            Invoke-SharpProofPublicationPushSequence `
                -Package $package -MainAction $action.mainAction `
                -PushMain { param($path) $pushes.Add("main|$path") } `
                -PushSymbols { param($path) $pushes.Add("symbols|$path") }
            if ($pushes.Count -ne 2 -or
                $pushes[0] -cnotmatch '^main\|' -or
                $pushes[1] -cnotmatch '^symbols\|') {
                throw 'An absent package must publish main then symbols.'
            }
        }
        elseif ($Mutation -eq 'mocked-main-exists-match') {
            $action = New-SharpProofPublicationActionAuthority `
                -Mode registry -MainState $result.state
            $pushes = [Collections.Generic.List[string]]::new()
            Invoke-SharpProofPublicationPushSequence `
                -Package $package -MainAction $action.mainAction `
                -PushMain { param($path) $pushes.Add("main|$path") } `
                -PushSymbols { param($path) $pushes.Add("symbols|$path") }
            if ($null -ne $preflightError -or
                $result.state -cne 'VerifiedPresent' -or
                $result.verifiedMainSha256 -notmatch '^[0-9a-f]{64}$' -or
                $script:preflightCalls.Count -ne 2 -or
                $script:preflightCalls[0] -notmatch '^Head\|.*\.nupkg$' -or
                $script:preflightCalls[1] -notmatch '^Get\|.*\.nupkg$' -or
                $pushes.Count -ne 1 -or
                $pushes[0] -cnotmatch '^symbols\|') {
                throw 'Exact NuGet.org bytes must resume with the staged symbols package only.'
            }
        }
        elseif ($Mutation -eq 'mocked-main-exists-mismatch') {
            if ($null -eq $preflightError -or
                $script:preflightCalls.Count -ne 2) {
                throw 'Mismatched existing main bytes must fail closed.'
            }
        }
        elseif ($Mutation -eq 'mocked-main-exists') {
            if ($null -eq $preflightError -or
                $script:preflightCalls.Count -ne 1) {
                throw 'An unverified feed must reject an existing main package.'
            }
        }
        elseif ($Mutation -eq 'mocked-main-error') {
            if ($null -eq $preflightError -or
                $script:preflightCalls.Count -ne 1) {
                throw 'Unknown main package status must fail closed.'
            }
        }
        elseif ($Mutation -eq 'mocked-main-query-base') {
            if ($null -eq $preflightError -or
                $script:preflightCalls.Count -ne 0) {
                throw 'Invalid package base address must fail before network access.'
            }
        }
        if ($Mutation -notin @(
                'mocked-main-missing','mocked-main-exists-match') -and
            $null -eq $preflightError) {
            throw 'An unsafe preflight state was unexpectedly accepted.'
        }
        if ($Mutation -in @('mocked-main-missing','mocked-main-exists-match') -and
            $null -ne $preflightError) {
            throw $preflightError
        }
        if ($Mutation -ne 'mocked-main-query-base' -and
            $script:preflightCalls.Count -gt 0 -and
            ($script:preflightCalls[0] -notmatch '^Head\|.*\.nupkg$' -or
             $script:preflightCalls[0] -match '\.snupkg$')) {
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
    Remove-SharpProofOwnedDirectory -Directory $root
}
