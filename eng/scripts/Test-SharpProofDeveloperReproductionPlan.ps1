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

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$qualificationRoots = Get-SharpProofDeveloperQualificationBuildProjects -RepositoryRoot $repositoryRoot
$expectedRoots = @(
    'src/SharpProof.Attributes/SharpProof.Attributes.csproj',
    'src/SharpProof.DeveloperExperience.Tests/SharpProof.DeveloperExperience.Tests.csproj',
    'src/SharpProof.Package/SharpProof.Package.csproj',
    'src/SharpProof.Templates/SharpProof.Templates.csproj',
    'src/SharpProof.Verifier/SharpProof.Verifier.csproj')
if ($qualificationRoots.Length -ne $expectedRoots.Count -or
    @($expectedRoots | Where-Object { $qualificationRoots -cnotcontains $_ }).Count -ne 0) {
    throw 'Developer qualification must retain the product catalog, its test root, and Templates.'
}
$closure = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$pending = [Collections.Generic.Queue[string]]::new()
foreach ($project in $qualificationRoots) { $pending.Enqueue($project) }
while ($pending.Count -gt 0) {
    $project = $pending.Dequeue()
    if (-not $closure.Add($project)) { continue }
    $path = Join-Path $repositoryRoot $project
    [xml]$projectXml = [IO.File]::ReadAllText($path)
    foreach ($reference in $projectXml.SelectNodes('/Project/ItemGroup/ProjectReference')) {
        $include = $reference.GetAttribute('Include').Replace('\', '/')
        if ($include.Contains('$') -or $include.Contains('*')) { throw 'Developer build closure requires an evaluated project-reference check.' }
        $absolute = [IO.Path]::GetFullPath($include, (Split-Path -Parent $path))
        $pending.Enqueue([IO.Path]::GetRelativePath($repositoryRoot, $absolute).Replace('\', '/'))
    }
}
foreach ($name in @('Tool', 'CodeFixes', 'Reporting', 'Analyzer', 'Package', 'Verifier', 'Templates')) {
    if (-not $closure.Contains("src/SharpProof.$name/SharpProof.$name.csproj")) {
        throw "Developer qualification lost a required package or test dependency: '$name'."
    }
}
$checks++

function Assert-DeveloperQualificationBuildFailure([string]$Root, [string]$Expected) {
    try { Get-SharpProofDeveloperQualificationBuildProjects -RepositoryRoot $Root | Out-Null }
    catch {
        if ($_.Exception.Message -cne $Expected) { throw }
        return
    }
    throw "Developer qualification accepted invalid build roots: $Expected"
}

$buildFixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-developer-build-plan-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory((Join-Path $buildFixture 'eng/scripts')) | Out-Null
    $fixtureRoots = @(
        'src/Future.Package/Future.Package.csproj',
        'src/SharpProof.DeveloperExperience.Tests/SharpProof.DeveloperExperience.Tests.csproj',
        'src/SharpProof.Templates/SharpProof.Templates.csproj')
    foreach ($project in $fixtureRoots) {
        $path = Join-Path $buildFixture $project
        [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
        [IO.File]::WriteAllText($path, '<Project />')
    }
    $solutionPath = Join-Path $buildFixture 'SharpProof.slnx'
    $solutionText = '<Solution>' + (($fixtureRoots | ForEach-Object { '<Project Path="' + $_ + '" />' }) -join '') + '</Solution>'
    [IO.File]::WriteAllText($solutionPath, $solutionText)
    [IO.File]::WriteAllText((Join-Path $buildFixture 'eng/scripts/package-projects.json'),
        '{"schemaVersion":1,"projects":["src/Future.Package/Future.Package.csproj"]}')
    $futureRoots = Get-SharpProofDeveloperQualificationBuildProjects -RepositoryRoot $buildFixture
    if ($futureRoots.Length -ne 3 -or $futureRoots -cnotcontains $fixtureRoots[0]) {
        throw 'Developer qualification did not derive a future product root from the package catalog.'
    }
    $checks++
    [IO.File]::WriteAllText($solutionPath, '<Solution><Project Path="src/Future.Package/Future.Package.csproj" /></Solution>')
    Assert-DeveloperQualificationBuildFailure $buildFixture `
        "Developer qualification build project is missing from the source solution: '$($fixtureRoots[1])'."
    $checks++
    [IO.File]::WriteAllText($solutionPath, $solutionText)
    [IO.File]::Delete((Join-Path $buildFixture $fixtureRoots[2]))
    Assert-DeveloperQualificationBuildFailure $buildFixture `
        "Developer qualification build project is missing from the source solution: '$($fixtureRoots[2])'."
    $checks++
    [IO.File]::WriteAllText((Join-Path $buildFixture 'eng/scripts/package-projects.json'),
        '{"schemaVersion":1,"projects":["../outside.csproj"]}')
    Assert-DeveloperQualificationBuildFailure $buildFixture 'Package build roots must be repository-relative projects under src.'
    $checks++
}
finally {
    if ([IO.Directory]::Exists($buildFixture)) { [IO.Directory]::Delete($buildFixture, $true) }
}

$containerTokens = $null
$containerErrors = $null
$containerAst = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Invoke-SharpProofContainer.ps1'), [ref]$containerTokens, [ref]$containerErrors)
$qualificationFunctions = @($containerAst.FindAll({ param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Invoke-DeveloperQualification'
        }, $true))
if ($containerErrors.Count -ne 0 -or $qualificationFunctions.Count -ne 1) {
    throw 'Expected one parseable actual developer qualification implementation.'
}
$qualificationBody = $qualificationFunctions[0].Body.GetScriptBlock()

function Test-DeveloperQualificationRoute {
    param(
        [scriptblock]$Body,
        [ValidateSet('Normal', 'Fast', 'PackOnly', 'ConsumerFailure', 'Prepared', 'PreparedFailure', 'PreparedPackOnly')][string]$Route
    )
    $repositoryRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-developer-route-' + [Guid]::NewGuid().ToString('N'))
    $Fast = $Route -ceq 'Fast'
    $PackOnly = $Route -cin @('PackOnly', 'PreparedPackOnly')
    $hasPreparedDeveloperInput = $Route.StartsWith('Prepared', [StringComparison]::Ordinal)
    $PackageSource = 'fixture-package-feed'
    $observed = [ordered]@{ filter = $null; buildArguments = $null; consumer = $false; projects = $expectedRoots }
    function Invoke-SharpProofSolutionBuild {
        param([string]$BuildConfiguration, [string[]]$AdditionalBuildArguments = @(), [string]$BuildTarget)
        if ($hasPreparedDeveloperInput) { throw 'Prepared developer qualification must not invoke a managed build.' }
        if ($BuildConfiguration -cne 'Release' -or $null -eq $AdditionalBuildArguments) {
            throw 'Developer qualification supplied null build arguments or lost Release configuration.'
        }
        if (-not [IO.File]::Exists($BuildTarget)) { throw 'Developer build filter was not created before building.' }
        $observed.filter = $BuildTarget
        $observed.buildArguments = $AdditionalBuildArguments
        $selection = Get-Content -LiteralPath $BuildTarget -Raw | ConvertFrom-Json
        if ($selection.solution.path -cne 'SharpProof.slnx' -or
            @($selection.solution.projects).Count -ne $observed.projects.Count -or
            @($selection.solution.projects | Where-Object { $observed.projects -cnotcontains $_ }).Count -ne 0) {
            throw 'Developer qualification changed the selected graph.'
        }
    }
    function Invoke-RequiredScript {
        param([string]$Path, [string]$FailureMessage, [object]$Arguments)
        if ($hasPreparedDeveloperInput) {
            if ($Path -cne 'eng/scripts/Test-SharpProofDeveloperExperience.ps1' -or [string]::IsNullOrWhiteSpace($FailureMessage) -or
                $Arguments.Count -ne 6 -or $Arguments['PackageSource'] -cne $PackageSource -or
                $Arguments['ConsumerImage'] -cne $env:SHARPPROOF_CONSUMER_IMAGE -or
                $Arguments['ToolPackageSource'] -cne $env:SHARPPROOF_QUALIFIED_TOOL_PACKAGES -or
                $Arguments['PreparedCodeFixesSource'] -cne $env:SHARPPROOF_QUALIFIED_CODEFIX_PACKAGES -or
                $Arguments['DeveloperHarness'] -cne $env:SHARPPROOF_DEVELOPER_HARNESS -or
                $Arguments['QualificationMode'] -cne 'AnalyzerEnabled') {
                throw 'Prepared developer qualification lost its exact input route or normal analyzer mode.'
            }
            $observed.consumer = $true
            if ($Route -ceq 'PreparedFailure') { throw 'Fixture developer consumer failure.' }
            [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'artifacts/developer-experience')) | Out-Null
            return
        }
        if ($Path -cne 'eng/scripts/Test-SharpProofDeveloperExperience.ps1' -or [string]::IsNullOrWhiteSpace($FailureMessage) -or
            $Arguments['BuildTarget'] -cne $observed.filter -or -not [IO.File]::Exists($Arguments['BuildTarget']) -or
            $Arguments['PackageSource'] -cne $PackageSource -or $Arguments['PackOnly'] -ne $PackOnly -or
            $Arguments['QualificationMode'] -cne $(if ($Fast) { 'FeedbackOnly' } else { 'AnalyzerEnabled' })) {
            throw 'Developer qualification lost its live build filter, package input, or analyzer mode.'
        }
        $observed.consumer = $true
        if ($Route -ceq 'ConsumerFailure') { throw 'Fixture developer consumer failure.' }
        [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'artifacts/developer-experience')) | Out-Null
    }
    try {
        [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'eng/scripts')) | Out-Null
        foreach ($project in $observed.projects) {
            $path = Join-Path $repositoryRoot $project
            [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
            [IO.File]::WriteAllText($path, '<Project />')
        }
        $solutionText = '<Solution>' + (($observed.projects | ForEach-Object { '<Project Path="' + $_ + '" />' }) -join '') + '</Solution>'
        [IO.File]::WriteAllText((Join-Path $repositoryRoot 'SharpProof.slnx'), $solutionText)
        $catalogProjects = @($observed.projects | Where-Object { $_ -notmatch 'DeveloperExperience.Tests|SharpProof.Templates' })
        [IO.File]::WriteAllText((Join-Path $repositoryRoot 'eng/scripts/package-projects.json'),
            (([ordered]@{ schemaVersion = 1; projects = $catalogProjects }) | ConvertTo-Json))
        $consumerFailed = $false
        try { & $Body }
        catch {
            if ($Route -ceq 'PreparedPackOnly' -and $_.Exception.Message -ceq 'A prepared developer harness requires complete qualification.') {
                if ($observed.consumer -or $null -ne $observed.filter) { throw 'Prepared PackOnly admission executed work before rejecting the route.' }
                return
            }
            if ($Route -cnotin @('ConsumerFailure', 'PreparedFailure') -or $_.Exception.Message -cne 'Fixture developer consumer failure.') { throw }
            $consumerFailed = $true
        }
        if ($hasPreparedDeveloperInput) {
            $modeFile = Join-Path $repositoryRoot 'artifacts/developer-experience/qualification-mode.txt'
            if (-not $observed.consumer -or $null -ne $observed.filter -or $null -ne $observed.buildArguments -or
                $consumerFailed -ne ($Route -ceq 'PreparedFailure') -or
                [IO.File]::Exists($modeFile) -ne ($Route -ceq 'Prepared')) {
                throw 'Prepared developer qualification must preserve consumer failure and avoid a redundant build.'
            }
            return
        }
        if (-not $observed.consumer -or $consumerFailed -ne ($Route -ceq 'ConsumerFailure') -or
            $null -eq $observed.filter -or [IO.File]::Exists($observed.filter)) {
            throw 'Developer qualification did not execute its consumer or clean its filter after completion.'
        }
        $expectedArguments = if ($Fast) { 1 } else { 0 }
        if ($null -eq $observed.buildArguments -or $observed.buildArguments.Count -ne $expectedArguments -or
            ($Fast -and $observed.buildArguments[0] -cne '-p:RunAnalyzersDuringBuild=false')) {
            throw 'Normal developer builds must keep analyzer defaults; only Fast may explicitly disable analyzers.'
        }
        $modeFile = Join-Path $repositoryRoot 'artifacts/developer-experience/qualification-mode.txt'
        if ([IO.File]::Exists($modeFile) -ne ($Route -cin @('Normal', 'Fast'))) {
            throw 'Developer qualification mode evidence differs from the completed route.'
        }
    }
    finally {
        if ([IO.Directory]::Exists($repositoryRoot)) { [IO.Directory]::Delete($repositoryRoot, $true) }
    }
}

foreach ($route in @('Normal', 'Fast', 'PackOnly', 'ConsumerFailure', 'Prepared', 'PreparedFailure', 'PreparedPackOnly')) {
    Test-DeveloperQualificationRoute -Body $qualificationBody -Route $route
    $checks++
}
Write-Host "Developer reproduction regressions passed: $checks preparation, identity, independent-path, and redacted diagnostic controls."
