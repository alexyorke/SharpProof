[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.SemanticCoveragePlan.ps1')

$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-authority-build-plan-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null

function Write-BuildPlanSolution([string[]]$Projects) {
    $entries = @($Projects | ForEach-Object {
            '<Project Path="' + [Security.SecurityElement]::Escape($_) + '" />'
        })
    [IO.File]::WriteAllText((Join-Path $fixture 'SharpProof.slnx'),
        '<Solution><Folder Name="/src/">' + ($entries -join '') + '</Folder></Solution>')
}

function Assert-AuthorityBuildProjects([string[]]$Additional, [string[]]$Expected) {
    $actual = Get-SharpProofCoverageAuthorityBuildProjects `
        -RepositoryRoot $fixture -AdditionalProjects $Additional
    if (($actual -join "`n") -cne (@($Expected | Sort-Object -CaseSensitive) -join "`n")) {
        throw 'Coverage authority build did not retain its conservative production and selected test roots.'
    }
}

function Assert-AuthorityBuildFailure([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -cne $Expected) { throw }
        return
    }
    throw "Coverage authority build accepted invalid selection: $Expected"
}

try {
    # Unevaluated flags never remove a production candidate. Imported or
    # conditional MSBuild properties are resolved by the actual authority later.
    $production = @('src/Core/Core.csproj', 'src/Future/Future.csproj', 'src/Package/Package.csproj')
    $tests = @('src/Core.Tests/Core.Tests.csproj', 'src/Future.UnitTests/Future.UnitTests.csproj',
        'src/Package.IntegrationTests/Package.IntegrationTests.csproj',
        'src/Timing.PerformanceTests/Timing.PerformanceTests.csproj')
    foreach ($project in @($production) + @($tests)) {
        $path = Join-Path $fixture $project
        [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
        [IO.File]::WriteAllText($path, '<Project><PropertyGroup><SharpProofProductionProject>false</SharpProofProductionProject></PropertyGroup></Project>')
    }
    Write-BuildPlanSolution (@($production) + @($tests))
    Assert-AuthorityBuildProjects -Additional @() -Expected $production
    Assert-AuthorityBuildProjects -Additional @($tests[0], $tests[0].Replace('/', '\'), $production[0]) `
        -Expected (@($production) + $tests[0])
    Assert-AuthorityBuildProjects -Additional @((Join-Path $fixture $tests[2])) `
        -Expected (@($production) + $tests[2])

    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture -AdditionalProjects @('')
    } 'Coverage authority build has an empty additional project.'
    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture -AdditionalProjects @('../Foreign.csproj')
    } 'Coverage authority build additional project is not in the solution.'
    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture -AdditionalProjects @('src/Unlisted.csproj')
    } 'Coverage authority build additional project is not in the solution.'

    Write-BuildPlanSolution (@($production) + $production[0].Replace('/', '\'))
    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture
    } 'Coverage build solution has a duplicate project.'
    Write-BuildPlanSolution @('../Foreign.csproj')
    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture
    } 'Coverage build solution has a project outside the repository.'
    Write-BuildPlanSolution @('src/Missing.csproj')
    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture
    } "Coverage build solution project is absent: 'src/Missing.csproj'."
    Write-BuildPlanSolution $tests
    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture
    } 'Coverage authority build has no non-test solution projects.'
    [IO.File]::WriteAllText((Join-Path $fixture 'SharpProof.slnx'), '<Solution><Project /></Solution>')
    Assert-AuthorityBuildFailure {
        Get-SharpProofCoverageAuthorityBuildProjects -RepositoryRoot $fixture
    } 'Coverage build solution has a project without a Path.'
    Write-Host 'Coverage authority build plan regressions passed: future production roots, all test suffixes, conservative flags, selected closure roots, deduplication, and invalid paths.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::Ordinal) -or
        $resolved -eq $temporaryRoot) { throw 'Invalid coverage authority fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
