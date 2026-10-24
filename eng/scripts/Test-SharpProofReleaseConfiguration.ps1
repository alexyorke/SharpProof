[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Release configuration regressions require the canonical Linux container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReleaseConfiguration.psm1')

$environmentNames = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
        'SHARPPROOF_CI_PACKAGE_VERSION', 'SHARPPROOF_PRIVATE_PREVIEW',
        'SHARPPROOF_REPOSITORY_URL', 'GITHUB_REPOSITORY',
        'SHARPPROOF_SOURCE_REPOSITORY', 'SHARPPROOF_SOURCE_COMMIT', 'SHARPPROOF_WORKFLOW_COMMIT'))
$originalEnvironmentBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, string]()
foreach ($name in $environmentNames) {
    $originalEnvironmentBuilder.Add($name, [Environment]::GetEnvironmentVariable($name, 'Process'))
}
$originalEnvironment = $originalEnvironmentBuilder.ToImmutable()
$emptyEnvironment = [Collections.Immutable.ImmutableDictionary[string, string]]::Empty
$privateEnvironment = $emptyEnvironment.
Add('SHARPPROOF_PRIVATE_PREVIEW', 'true').
Add('SHARPPROOF_REPOSITORY_URL', 'https://github.com/alexyorke/SharpProof-preview').
Add('GITHUB_REPOSITORY', 'alexyorke/SharpProof-preview').
Add('SHARPPROOF_SOURCE_REPOSITORY', 'alexyorke/SharpProof').
Add('SHARPPROOF_SOURCE_COMMIT', ('a' * 40)).
Add('SHARPPROOF_WORKFLOW_COMMIT', ('b' * 40))
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-release-configuration-' + [guid]::NewGuid().ToString('N'))
$versionPath = Join-Path $fixture 'eng/Versions.props'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($versionPath)) | Out-Null
$versionFixture = '<Project><PropertyGroup>' +
'<SharpProofVersionPrefix>2.3.4</SharpProofVersionPrefix>' +
'<SharpProofPackageVersion>$(SharpProofVersionPrefix)-preview.9</SharpProofPackageVersion>' +
'<SharpProofPackageVersion>$(SHARPPROOF_CI_PACKAGE_VERSION)</SharpProofPackageVersion>' +
'</PropertyGroup><PropertyGroup>' +
'<SharpProofVersionPrefix>9.8.7</SharpProofVersionPrefix>' +
'<SharpProofPackageVersion>9.8.7-other.1</SharpProofPackageVersion>' +
'</PropertyGroup></Project>'
$script:checks = 0

function Set-ReleaseFixtureEnvironment {
    param([Collections.Generic.IReadOnlyDictionary[string, string]]$Values)
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    foreach ($entry in $Values.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}

function Assert-ReleaseFixtureValue {
    param([string]$Actual, [string]$Expected, [string]$Name)
    if ($Actual -cne $Expected) { throw "Release configuration returned the wrong value: $Name" }
    $script:checks++
}

function Assert-ReleaseFixtureFailure {
    param([scriptblock]$Action, [string]$Expected, [string]$Name)
    $rejected = $false
    try { & $Action | Out-Null }
    catch {
        if (-not $_.Exception.Message.Contains($Expected)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Release configuration accepted invalid input: $Name" }
    $script:checks++
}

try {
    Set-ReleaseFixtureEnvironment -Values $emptyEnvironment
    [IO.File]::WriteAllText($versionPath, $versionFixture, [Text.UTF8Encoding]::new($false))
    Assert-ReleaseFixtureValue -Actual (Get-SharpProofEffectivePackageVersion -RepositoryRoot $fixture) `
        -Expected '2.3.4-preview.9' -Name 'first XML values across repeated properties and groups'
    # Fresh processes reproduce the real root/dependency imports, rather than
    # inheriting helper bindings from this test's already imported module.
    $compositionProbe = {
        param([string]$ModuleRoot, [string]$VersionRoot, [string]$Order)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        $releaseModule = Join-Path $ModuleRoot 'SharpProof.ReleaseConfiguration.psm1'
        $packageModule = Join-Path $ModuleRoot 'SharpProof.PackageIdentity.psm1'
        $developerModule = Join-Path $ModuleRoot 'SharpProof.DeveloperPackageReproduction.psm1'
        switch ($Order) {
            'release-first' {
                Import-Module $releaseModule
                Import-Module $developerModule -Force
            }
            'dependency-first' {
                Import-Module $developerModule -Force
                Import-Module $releaseModule
            }
            'repeat-parents' {
                Import-Module $releaseModule
                Import-Module $packageModule
                Import-Module $developerModule -Force
                Import-Module $packageModule
                Import-Module $developerModule -Force
                Import-Module $releaseModule
            }
            'copied-dependency' {
                Import-Module $releaseModule
                Import-Module $developerModule -Force
                $replica = Join-Path $VersionRoot 'copied-modules'
                [IO.Directory]::CreateDirectory($replica) | Out-Null
                foreach ($modulePath in @($releaseModule, $packageModule)) {
                    Copy-Item -LiteralPath $modulePath -Destination $replica
                }
                # Reproducibility inspects package identities from each clone.
                Import-Module (Join-Path $replica 'SharpProof.PackageIdentity.psm1') -Force
            }
            default { throw 'Unknown module composition fixture.' }
        }
        if ((Get-SharpProofEffectivePackageVersion -RepositoryRoot $VersionRoot) -cne '2.3.4-preview.9' -or
            (Get-SharpProofExpectedRepositoryUrl) -cne 'https://github.com/alexyorke/SharpProof') {
            throw 'Root release helpers were lost or changed after dependency imports.'
        }
        $feed = Join-Path $VersionRoot ('module-feed-' + $Order)
        [IO.Directory]::CreateDirectory($feed) | Out-Null
        function New-CompositionPackage([string]$Id, [string]$RepositoryUrl = 'https://github.com/alexyorke/SharpProof') {
            $packagePath = Join-Path $feed ($Id + '.2.3.4-preview.9.nupkg')
            [IO.File]::Delete($packagePath)
            $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
            try {
                $writer = [IO.StreamWriter]::new($archive.CreateEntry($Id + '.nuspec').Open())
                try {
                    $writer.Write('<package><metadata><id>' + $Id + '</id><version>2.3.4-preview.9</version><repository type="git" url="' +
                        $RepositoryUrl + '" commit="' + ('a' * 40) + '" /></metadata></package>')
                }
                finally { $writer.Dispose() }
                $payload = switch ($Id) {
                    'SharpProof.Tool' { 'tools/net10.0/any/SharpProof.Tool.dll' }
                    'SharpProof.CodeFixes' { 'analyzers/dotnet/cs/SharpProof.CodeFixes.dll' }
                    'SharpProof.Templates' { 'content/container/compose.yaml.template' }
                }
                $writer = [IO.StreamWriter]::new($archive.CreateEntry($payload).Open())
                try { $writer.Write('fixture') }
                finally { $writer.Dispose() }
            }
            finally { $archive.Dispose() }
        }
        foreach ($id in @('SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')) { New-CompositionPackage $id }
        $snapshot = Get-SharpProofDeveloperPackageSnapshot -PackageSource $feed -PackageVersion '2.3.4-preview.9' -RepositoryCommit ('a' * 40)
        if ($snapshot.Archives.Count -ne 3 -or $snapshot.Payloads.Count -ne 6) {
            throw 'Nested package/release helper calls lost their original composition.'
        }
        New-CompositionPackage 'SharpProof.Tool' -RepositoryUrl 'https://example.invalid/wrong'
        $rejected = $false
        try { $null = Get-SharpProofDeveloperPackageSnapshot -PackageSource $feed -PackageVersion '2.3.4-preview.9' -RepositoryCommit ('a' * 40) }
        catch {
            if (-not $_.Exception.Message.Contains('invalid repository metadata')) { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw 'Module composition weakened wrong-repository rejection.' }
        if ((Get-SharpProofEffectivePackageVersion -RepositoryRoot $VersionRoot) -cne '2.3.4-preview.9') {
            throw 'Nested identity validation removed the root version helper.'
        }
        Write-Output "Module composition passed: $Order"
    }.ToString()
    $compositionPath = Join-Path $fixture 'ReleaseModuleComposition.ps1'
    [IO.File]::WriteAllText($compositionPath, $compositionProbe, [Text.UTF8Encoding]::new($false))
    foreach ($compositionOrder in @('release-first', 'dependency-first', 'repeat-parents', 'copied-dependency')) {
        $probeOutput = @(& pwsh -NoLogo -NoProfile -File $compositionPath -ModuleRoot $PSScriptRoot -VersionRoot $fixture -Order $compositionOrder 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ("Release module composition failed: $compositionOrder`n" + (($probeOutput | Select-Object -Last 12) -join "`n")) }
        Write-Host ($probeOutput -join "`n")
        $script:checks++
    }
    foreach ($version in @('0.0.0-ci.42.1', '0.0.0-ci.9876543210.12')) {
        Set-ReleaseFixtureEnvironment -Values $emptyEnvironment.Add('SHARPPROOF_CI_PACKAGE_VERSION', $version)
        Assert-ReleaseFixtureValue -Actual (Get-SharpProofEffectivePackageVersion -RepositoryRoot $fixture) `
            -Expected $version -Name 'exact CI validation version'
    }
    foreach ($version in @('0.0.0-ci.0.1', '0.0.0-ci.1.0', '0.0.0-ci.01.1', '0.0.0-ci.1.01',
            '1.0.0', '0.0.0-ci.1', '0.0.0-ci.1.1+build', '0.0.0-CI.1.1', ' ',
            ' 0.0.0-ci.1.1', '0.0.0-ci.1.1 ', "0.0.0-ci.1.1`n", "0.0.0-ci.1.1`r`n")) {
        Set-ReleaseFixtureEnvironment -Values $emptyEnvironment.Add('SHARPPROOF_CI_PACKAGE_VERSION', $version)
        Assert-ReleaseFixtureFailure { Get-SharpProofEffectivePackageVersion -RepositoryRoot $fixture } `
            -Expected 'CI validation versions require' -Name 'malformed CI validation version'
    }
    Set-ReleaseFixtureEnvironment -Values $privateEnvironment.Add('SHARPPROOF_CI_PACKAGE_VERSION', '0.0.0-ci.42.1')
    Assert-ReleaseFixtureFailure { Get-SharpProofEffectivePackageVersion -RepositoryRoot $fixture } `
        -Expected 'cannot be private release versions' -Name 'private release cannot use CI version'
    Set-ReleaseFixtureEnvironment -Values $privateEnvironment
    Assert-ReleaseFixtureValue -Actual (Get-SharpProofEffectivePackageVersion -RepositoryRoot $fixture) `
        -Expected '2.3.4-preview.9' -Name 'private release retains normal release version'
    foreach ($xml in @('<Project><PropertyGroup><SharpProofVersionPrefix>2.3.4</SharpProofVersionPrefix></PropertyGroup></Project>',
            '<Project><PropertyGroup><SharpProofPackageVersion>2.3.4</SharpProofPackageVersion></PropertyGroup></Project>')) {
        Set-ReleaseFixtureEnvironment -Values $emptyEnvironment
        [IO.File]::WriteAllText($versionPath, $xml, [Text.UTF8Encoding]::new($false))
        Assert-ReleaseFixtureFailure { Get-SharpProofEffectivePackageVersion -RepositoryRoot $fixture } `
            -Expected 'Release version authority is missing.' -Name 'missing first version authority'
    }
    foreach ($version in @('2.3', 'release', '$(Unresolved)', '2.3.4 ', "2.3.4-preview.9`n", "2.3.4-preview.9`r`n")) {
        $escapedVersion = [Security.SecurityElement]::Escape($version)
        $xml = '<Project><PropertyGroup><SharpProofVersionPrefix>2.3.4</SharpProofVersionPrefix><SharpProofPackageVersion>' +
        $escapedVersion + '</SharpProofPackageVersion></PropertyGroup></Project>'
        [IO.File]::WriteAllText($versionPath, $xml, [Text.UTF8Encoding]::new($false))
        Assert-ReleaseFixtureFailure { Get-SharpProofEffectivePackageVersion -RepositoryRoot $fixture } `
            -Expected 'Invalid effective package version.' -Name 'malformed default version'
    }

    foreach ($values in @($emptyEnvironment,
            $emptyEnvironment.Add('SHARPPROOF_REPOSITORY_URL', 'https://github.com/alexyorke/SharpProof'),
            $emptyEnvironment.Add('SHARPPROOF_PRIVATE_PREVIEW', 'false'))) {
        Set-ReleaseFixtureEnvironment -Values $values
        Assert-ReleaseFixtureValue -Actual (Get-SharpProofExpectedRepositoryUrl) `
            -Expected 'https://github.com/alexyorke/SharpProof' -Name 'public repository authority'
    }
    Set-ReleaseFixtureEnvironment -Values $privateEnvironment
    Assert-ReleaseFixtureValue -Actual (Get-SharpProofExpectedRepositoryUrl) `
        -Expected 'https://github.com/alexyorke/SharpProof-preview' -Name 'bound private distribution repository'
    foreach ($url in @($null, '', 'https://github.com/alexyorke/SharpProof')) {
        Set-ReleaseFixtureEnvironment -Values $privateEnvironment.SetItem('SHARPPROOF_REPOSITORY_URL', $url)
        Assert-ReleaseFixtureFailure { Get-SharpProofExpectedRepositoryUrl } `
            -Expected 'must link to their private distribution repository' -Name 'private release cannot claim public repository'
    }
    foreach ($url in @('https://github.com/alexyorke/SharpProof-preview/', 'https://github.com/alexyorke/sharpproof-preview',
            'http://github.com/alexyorke/SharpProof-preview', 'https://example.invalid/SharpProof-preview',
            'https://github.com/alexyorke/SharpProof/', ' https://github.com/alexyorke/SharpProof-preview',
            "https://github.com/alexyorke/SharpProof-preview`n")) {
        Set-ReleaseFixtureEnvironment -Values $privateEnvironment.SetItem('SHARPPROOF_REPOSITORY_URL', $url)
        Assert-ReleaseFixtureFailure { Get-SharpProofExpectedRepositoryUrl } `
            -Expected 'requires the explicit private preview workflow' -Name 'unapproved repository URL'
    }
    foreach ($flag in @($null, 'false', 'True', 'TRUE', ' true', 'true ')) {
        Set-ReleaseFixtureEnvironment -Values $privateEnvironment.SetItem('SHARPPROOF_PRIVATE_PREVIEW', $flag)
        Assert-ReleaseFixtureFailure { Get-SharpProofExpectedRepositoryUrl } `
            -Expected 'requires the explicit private preview workflow' -Name 'private context must be explicit'
    }
    foreach ($name in @('GITHUB_REPOSITORY', 'SHARPPROOF_SOURCE_REPOSITORY')) {
        foreach ($identity in @($null, 'alexyorke/other', 'AlexYorke/SharpProof-preview', 'alexyorke/SharpProof ')) {
            Set-ReleaseFixtureEnvironment -Values $privateEnvironment.SetItem($name, $identity)
            Assert-ReleaseFixtureFailure { Get-SharpProofExpectedRepositoryUrl } `
                -Expected 'requires the explicit private preview workflow' -Name 'repository identity binding'
        }
    }
    foreach ($name in @('SHARPPROOF_SOURCE_COMMIT', 'SHARPPROOF_WORKFLOW_COMMIT')) {
        foreach ($commit in @($null, 'master', ('a' * 39), ('A' * 40), ('a' * 41),
                (('a' * 40) + "`n"), (('a' * 40) + "`r`n"), (' ' + ('a' * 40)))) {
            Set-ReleaseFixtureEnvironment -Values $privateEnvironment.SetItem($name, $commit)
            Assert-ReleaseFixtureFailure { Get-SharpProofExpectedRepositoryUrl } `
                -Expected 'requires the explicit private preview workflow' -Name 'exact source and workflow commit binding'
        }
    }
    Write-Host "Release configuration regressions passed: $script:checks version, public/private repository and workflow/source identity controls."
}
finally {
    foreach ($entry in $originalEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    $fullFixture = [IO.Path]::GetFullPath($fixture)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $fullFixture.StartsWith($temporaryParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($fullFixture) -notlike 'sharpproof-release-configuration-*') {
        throw 'Refusing to remove an unexpected release configuration fixture.'
    }
    Remove-Item -LiteralPath $fullFixture -Recurse -Force
}
