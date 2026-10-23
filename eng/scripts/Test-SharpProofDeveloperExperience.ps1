[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConsumerImage,
    [string]$PackageSource = '',
    [string]$ToolPackageSource = '',
    [string]$PreparedCodeFixesSource = '',
    [string]$DeveloperHarness = '',
    [ValidateNotNullOrEmpty()][string]$BuildTarget = 'SharpProof.slnx',
    [ValidateSet('AnalyzerEnabled', 'FeedbackOnly')][string]$QualificationMode = 'AnalyzerEnabled',
    [switch]$PackOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1') -Force
Assert-SharpProofContainer 'Developer-experience qualification requires the canonical container.'
$preparedDeveloperBuild = -not [string]::IsNullOrWhiteSpace($PreparedCodeFixesSource) -or
-not [string]::IsNullOrWhiteSpace($DeveloperHarness)
if ($preparedDeveloperBuild -and
    ([string]::IsNullOrWhiteSpace($PreparedCodeFixesSource) -or [string]::IsNullOrWhiteSpace($DeveloperHarness) -or
    [string]::IsNullOrWhiteSpace($PackageSource) -or [string]::IsNullOrWhiteSpace($ToolPackageSource) -or
    $QualificationMode -cne 'AnalyzerEnabled' -or $PackOnly)) {
    throw 'Prepared developer qualification requires all exact producer inputs, normal analyzers, and the complete qualification.'
}
if ($ConsumerImage -cnotmatch '^ghcr\.io/alexyorke/sharpproof@sha256:[a-f0-9]{64}$') {
    throw 'Supply the exact qualified consumer-image digest.'
}
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/Versions.props') -Raw
$prefix = [string]$versionProps.Project.PropertyGroup.SharpProofVersionPrefix
$version = ([string]$versionProps.Project.PropertyGroup.SharpProofPackageVersion).Replace('$(SharpProofVersionPrefix)', $prefix)
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -cnotmatch '^[a-f0-9]{40}$') { throw 'Developer-experience qualification requires a Git source identity.' }
$runId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = Join-Path $repositoryRoot "artifacts/developer-experience/$runId"
$feed = Join-Path $evidenceRoot 'packages'
[void][IO.Directory]::CreateDirectory($feed)
$timings = [Collections.Generic.List[object]]::new()
$packageEvidence = [Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow.ToString('O')
$status = 'failed'
$toolPackageMode = if ([string]::IsNullOrWhiteSpace($ToolPackageSource)) { 'packed-in-run' } else { 'qualified-artifact' }
$qualifiedToolSha256 = $null
$qualifiedCodeFixesSha256 = $null
$fixtureRoot = $null
$previousDeveloperNugetPackages = $env:NUGET_PACKAGES
function Run([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$Expected = 0) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $log = Join-Path $evidenceRoot "$Name.log"
    & $Executable @Arguments *> $log
    $observed = $LASTEXITCODE
    $timer.Stop()
    $timings.Add([ordered]@{ phase = $Name; elapsedMilliseconds = $timer.ElapsedMilliseconds; expectedExit = $Expected; observedExit = $observed })
    if ($observed -ne $Expected) {
        Get-Content -LiteralPath $log -Tail 35 | Write-Host
        throw "$Name returned $observed; expected $Expected."
    }
    Write-Host "$Name passed ($($timer.ElapsedMilliseconds) ms)."
    $global:LASTEXITCODE = 0
}
function Assert-RegressionResult([string]$ResultsDirectory, [ValidateSet('Failed', 'Passed')][string]$Outcome) {
    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File)
    if ($trxFiles.Count -ne 1) { throw 'Expected one executed NUnit regression report, not a build failure or empty test run.' }
    [xml]$testRun = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
    $results = @($testRun.TestRun.Results.UnitTestResult)
    $counters = $testRun.TestRun.ResultSummary.Counters
    $expectedFailed = if ($Outcome -eq 'Failed') { 1 } else { 0 }
    $expectedPassed = if ($Outcome -eq 'Passed') { 1 } else { 0 }
    if ($results.Count -ne 1 -or $results[0].testName -cne 'ReplayedInputSatisfiesPostcondition' -or
        $results[0].outcome -cne $Outcome -or [int]$counters.total -ne 1 -or [int]$counters.executed -ne 1 -or
        [int]$counters.failed -ne $expectedFailed -or [int]$counters.passed -ne $expectedPassed) {
        throw "Expected the generated NUnit assertion to execute once and be $Outcome."
    }
    if ($Outcome -eq 'Failed' -and -not ([string]$results[0].Output.ErrorInfo.Message).Contains('The original postcondition must hold.')) {
        throw 'The generated regression failed before reaching its original postcondition assertion.'
    }
}
function Save-Reports([string]$Source, [string]$Phase) {
    $destination = Join-Path $evidenceRoot $Phase
    [void][IO.Directory]::CreateDirectory($destination)
    foreach ($name in @('result.json', 'request.json', 'compiler-manifest.json', 'result.sarif')) {
        $report = Join-Path $Source $name
        if (Test-Path -LiteralPath $report -PathType Leaf) { Copy-Item -LiteralPath $report -Destination $destination -Force }
        elseif ($name -ne 'result.sarif') { throw "Required verification evidence is missing: $name." }
    }
}
try {
    # The caller builds the selected product and test closure, including the internal analyzer, first.
    if (-not $PackOnly -and -not $preparedDeveloperBuild) {
        Run 'developer-unit-tests' 'dotnet' @('test', 'src/SharpProof.DeveloperExperience.Tests/SharpProof.DeveloperExperience.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore')
    }
    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        Run 'pack-product' 'dotnet' @('pack', $BuildTarget, '-c', 'Release', '--no-build', '--no-restore', '-o', $feed, "-p:RepositoryCommit=$sourceCommit", "-p:SourceRevisionId=$sourceCommit")
    }
    else {
        & (Join-Path $PSScriptRoot 'Test-SharpProofPackageConsumers.ps1') -PackageSource $PackageSource -ValidatePackageSourceOnly
        foreach ($file in Get-ChildItem -LiteralPath $PackageSource -File | Where-Object Extension -In @('.nupkg', '.snupkg')) {
            Copy-Item -LiteralPath $file.FullName -Destination $feed
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($ToolPackageSource)) {
        $toolSource = (Resolve-Path -LiteralPath $ToolPackageSource).Path
        if (-not (Test-Path -LiteralPath $toolSource -PathType Container)) { throw 'The qualified tool package source must be a directory.' }
        $toolArchives = @(Get-ChildItem -LiteralPath $toolSource -Filter '*.nupkg' -File)
        if ($toolArchives.Count -ne 1 -or $toolArchives[0].Name -cne "SharpProof.Tool.$version.nupkg") {
            throw 'The qualified tool source must contain exactly the expected SharpProof.Tool archive.'
        }
        $toolIdentity = Get-SharpProofPackageIdentity -Path $toolArchives[0].FullName -RequireRepository -RequireSingleIdentity
        if ($toolIdentity.Id -cne 'SharpProof.Tool' -or $toolIdentity.Version -cne $version -or $toolIdentity.RepositoryCommit -cne $sourceCommit) {
            throw 'The qualified tool archive differs from the expected version or source commit.'
        }
        $qualifiedToolSha256 = (Get-FileHash -LiteralPath $toolArchives[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Copy-Item -LiteralPath $toolArchives[0].FullName -Destination $feed
    }
    if ($preparedDeveloperBuild) {
        $editor = Get-SharpProofPreparedCodeFixesPackage -PackageSource $PreparedCodeFixesSource `
            -PackageVersion $version -RepositoryCommit $sourceCommit
        $qualifiedCodeFixesSha256 = $editor.Sha256
        Copy-Item -LiteralPath $editor.Path -Destination $feed
        # The receipt binds all seven existing archives before any supplied
        # test assembly is loaded. Templates are packed below with this job's
        # actual consumer-image digest and retain their complete regressions.
        Run 'developer-unit-tests' 'pwsh' @('-NoLogo', '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'Invoke-SharpProofDeveloperHarnessTests.ps1'),
            '-HarnessRoot', $DeveloperHarness, '-PackageSource', $feed,
            '-ResultsDirectory', (Join-Path $evidenceRoot 'developer-unit-tests'))
        Run 'restore-Templates' 'dotnet' @('restore', 'src/SharpProof.Templates/SharpProof.Templates.csproj',
            '--locked-mode', '-p:Configuration=Release')
        $developerPackages = @('Templates')
    }
    else {
        $developerPackages = if ($toolPackageMode -eq 'qualified-artifact') { @('CodeFixes', 'Templates') } else { @('Tool', 'CodeFixes', 'Templates') }
    }
    foreach ($name in $developerPackages) {
        Run "pack-$name" 'dotnet' @('pack', "src/SharpProof.$name/SharpProof.$name.csproj", '-c', 'Release', '--no-build', '--no-restore',
            '-o', $feed, "-p:RepositoryCommit=$sourceCommit", "-p:SourceRevisionId=$sourceCommit", '-p:SharpProofDeveloperPackages=true', '-p:SharpProofPackTool=true', "-p:SharpProofConsumerImage=$ConsumerImage")
    }
    foreach ($name in @('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier', 'SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')) {
        $packagePath = Join-Path $feed "$name.$version.nupkg"
        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Missing qualified package $name." }
        $identity = Get-SharpProofPackageIdentity -Path $packagePath -RequireRepository -RequireSingleIdentity
        if ($identity.Id -cne $name -or $identity.Version -cne $version -or $identity.RepositoryCommit -cne $sourceCommit) {
            throw "Package $name does not match the qualified package version and source commit."
        }
        $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($name -ceq 'SharpProof.Tool' -and $toolPackageMode -eq 'qualified-artifact' -and $hash -cne $qualifiedToolSha256) {
            throw 'The installed tool archive does not match the supplied qualified bytes.'
        }
        if ($name -ceq 'SharpProof.CodeFixes' -and $preparedDeveloperBuild -and $hash -cne $qualifiedCodeFixesSha256) {
            throw 'The installed editor archive does not match the supplied qualified bytes.'
        }
        $packageEvidence.Add([ordered]@{ id = $name; version = $version; repositoryCommit = $sourceCommit; fileName = [IO.Path]::GetFileName($packagePath); sha256 = $hash })
    }
    Run 'template-regressions' 'pwsh' @('-NoLogo', '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-SharpProofTemplates.ps1'),
        '-PackagePath', (Join-Path $feed "SharpProof.Templates.$version.nupkg"), '-PackageVersion', $version,
        '-ConsumerImage', $ConsumerImage, '-RepositoryCommit', $sourceCommit)
    $editorArchive = [IO.Compression.ZipFile]::OpenRead((Join-Path $feed "SharpProof.CodeFixes.$version.nupkg"))
    try {
        if (@($editorArchive.Entries | Where-Object FullName -CEQ 'analyzers/dotnet/cs/SharpProof.CodeFixes.dll').Count -ne 1) {
            throw 'The optional editor package does not contain its code-fix assembly.'
        }
        if (@($editorArchive.Entries | Where-Object FullName -Like '*Microsoft.CodeAnalysis*.dll').Count -ne 0) {
            throw 'Editor-host Roslyn assemblies must not be bundled into the analyzer package.'
        }
    }
    finally { $editorArchive.Dispose() }
    if ($PackOnly) { $status = 'passed'; return }
    $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "sharpproof-dx-$runId"
    [void][IO.Directory]::CreateDirectory($fixtureRoot)
    # Every run must consume its exact packed bytes, even when the preview
    # version is unchanged. Never reuse a previous run's SharpProof packages.
    $env:NUGET_PACKAGES = Join-Path $fixtureRoot 'nuget-packages'
    $nugetConfig = Join-Path $fixtureRoot 'NuGet.config'
    $config = [Xml.XmlDocument]::new()
    $config.LoadXml('<configuration><packageSources><clear/><add key="fixture" value=""/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="fixture"><package pattern="SharpProof"/><package pattern="SharpProof.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>')
    $config.configuration.packageSources.add[0].SetAttribute('value', $feed)
    $config.Save($nugetConfig)
    $tools = Join-Path $fixtureRoot 'tools'
    Run 'install-tool' 'dotnet' @('tool', 'install', 'SharpProof.Tool', '--version', $version, '--tool-path', $tools, '--configfile', $nugetConfig)
    $tool = Join-Path $tools 'dotnet-sharpproof'
    $hive = Join-Path $fixtureRoot 'template-hive'
    Run 'install-template' 'dotnet' @('new', '--debug:custom-hive', $hive, 'install', (Join-Path $feed "SharpProof.Templates.$version.nupkg"))
    foreach ($variant in @('portable', 'container')) {
        $name = if ($variant -eq 'portable') { 'Portable' } else { 'Native' }
        $destination = Join-Path $fixtureRoot $name
        Run "generate-$variant" 'dotnet' @('new', '--debug:custom-hive', $hive, 'sharpproof', '-n', $name, '-o', $destination, '--verification', $variant)
        $project = Join-Path $destination "$name.csproj"
        [xml]$generatedProject = Get-Content -LiteralPath $project -Raw
        $references = @($generatedProject.Project.ItemGroup.PackageReference)
        if ($references.Count -ne 1 -or $references[0].Include -cne 'SharpProof' -or $references[0].Version -cne $version) {
            throw 'The starter must reference the exact qualified SharpProof facade package.'
        }
        $expectedVerification = if ($variant -eq 'container') { 'true' } else { 'false' }
        $expectedProfile = if ($variant -eq 'container') { 'strict' } else { 'advisory' }
        if ($generatedProject.Project.PropertyGroup.SharpProofVerify -cne $expectedVerification -or
            $generatedProject.Project.PropertyGroup.SharpProofProfile -cne $expectedProfile -or
            $generatedProject.Project.PropertyGroup.TargetFramework -cne 'net10.0' -or
            $generatedProject.Project.PropertyGroup.LangVersion -cne '13.0') {
            throw 'The template selected the wrong verification policy or compiler configuration.'
        }
        Run "build-$variant" 'dotnet' @('build', $project, '-c', 'Release', '--configfile', $nugetConfig)
        Run "evaluate-$variant" 'dotnet' @('msbuild', $project, '-nologo', '-getProperty:SharpProofVerify,SharpProofProfile,TargetFramework,LangVersion')
        $evaluated = Get-Content -LiteralPath (Join-Path $evidenceRoot "evaluate-$variant.log") -Raw | ConvertFrom-Json
        if ($evaluated.Properties.SharpProofVerify -cne $expectedVerification -or
            $evaluated.Properties.SharpProofProfile -cne $expectedProfile -or
            $evaluated.Properties.TargetFramework -cne 'net10.0' -or $evaluated.Properties.LangVersion -cne '13.0') {
            throw 'The restored package imports changed the starter verification policy or compiler configuration.'
        }
        if (-not ([IO.File]::ReadAllText((Join-Path $evidenceRoot "build-$variant.log")).Contains('SP0027'))) { throw 'The starter did not demonstrate its expected diagnostic.' }
        Run "doctor-$variant" $tool @('doctor', '--project', $project, '--verification', $variant)
    }
    $native = Join-Path $fixtureRoot 'Native'
    $nativeProject = Join-Path $native 'Native.csproj'
    $resultRoot = Join-Path $native 'artifacts/sharpproof'
    $resultPath = Join-Path $resultRoot 'result.json'
    $initialResult = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($initialResult.claimResults[0].presentation.parameterNames[0] -cne 'value') { throw 'Source parameter labels were not preserved.' }
    $captured = Get-Content -LiteralPath (Join-Path $resultRoot 'compiler-manifest.json') -Raw | ConvertFrom-Json
    if ($captured.compilation.assemblyName -cne 'Native' -or $captured.compilation.targetFramework -cne 'net10.0' -or
        @($captured.compilation.syntaxTrees).Count -eq 0 -or @($captured.compilation.references).Count -eq 0) {
        throw 'The artifact does not retain the source/reference metadata needed by comparison and export.'
    }
    Run 'explain-proven' $tool @('explain', '--result', $resultPath)
    Save-Reports $resultRoot 'baseline'
    $baselineRoot = Join-Path $evidenceRoot 'baseline'
    $baseline = Join-Path $baselineRoot 'result.json'
    Run 'compare-identical' $tool @('compare', '--baseline', $baseline, '--current', $resultPath, '--fail-on-regression')
    $sourcePath = Join-Path $native 'Counter.cs'
    $original = [IO.File]::ReadAllText($sourcePath)
    [IO.File]::WriteAllText($sourcePath, $original.Replace('return checked(value + 1);', 'return value;'), [Text.UTF8Encoding]::new($false))
    Run 'build-refuted' 'dotnet' @('build', $nativeProject, '-c', 'Release', '--no-restore') 1
    if (-not ([IO.File]::ReadAllText((Join-Path $evidenceRoot 'build-refuted.log')).Contains('Counterexample:'))) {
        throw 'Refuted build output did not use the source parameter label.'
    }
    Run 'compare-regression' $tool @('compare', '--baseline', $baseline, '--current', $resultPath, '--fail-on-regression') 1
    $refuted = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    Save-Reports $resultRoot 'refuted'
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $evidenceRoot 'refuted')
    $claim = @($refuted.claimResults | Where-Object outcome -CEQ 'Refuted')[0].claimId
    $exportRoot = Join-Path $fixtureRoot 'Exported'
    Run 'export-test' $tool @('export-test', '--project', $nativeProject, '--result', $resultPath, '--claim', $claim, '--output', $exportRoot)
    Copy-Item -LiteralPath $exportRoot -Destination (Join-Path $evidenceRoot 'exported-regression') -Recurse
    $testProject = Join-Path $exportRoot 'SharpProof.Regression.csproj'
    $beforeResults = Join-Path $evidenceRoot 'regression-before'
    Run 'regression-fails-before-fix' 'dotnet' @('test', $testProject, '-c', 'Release', '--logger', 'trx', '--results-directory', $beforeResults) 1
    Assert-RegressionResult $beforeResults 'Failed'
    [IO.File]::WriteAllText($sourcePath, $original, [Text.UTF8Encoding]::new($false))
    $afterResults = Join-Path $evidenceRoot 'regression-after'
    Run 'regression-passes-after-fix' 'dotnet' @('test', $testProject, '-c', 'Release', '--logger', 'trx', '--results-directory', $afterResults)
    Assert-RegressionResult $afterResults 'Passed'
    Run 'verify-fixed-consumer' 'dotnet' @('build', $nativeProject, '-c', 'Release', '--no-restore')
    Save-Reports $resultRoot 'fixed'
    $standardRoot = Join-Path $fixtureRoot 'Standard'
    [void][IO.Directory]::CreateDirectory($standardRoot)
    $standardProject = Join-Path $standardRoot 'Standard.csproj'
    [xml]$standardXml = Get-Content -LiteralPath $nativeProject -Raw
    $standardXml.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText = 'netstandard2.0'
    $language = $standardXml.SelectSingleNode('/Project/PropertyGroup/LangVersion')
    if ($null -eq $language) {
        $language = $standardXml.CreateElement('LangVersion')
        [void]$standardXml.SelectSingleNode('/Project/PropertyGroup').AppendChild($language)
    }
    $language.InnerText = '13.0'
    $standardXml.Save($standardProject)
    Copy-Item -LiteralPath (Join-Path $native 'Directory.Build.props') -Destination $standardRoot
    [IO.File]::WriteAllText((Join-Path $standardRoot 'Counter.cs'), $original.Replace('return checked(value + 1);', 'return value;'), [Text.UTF8Encoding]::new($false))
    Run 'standard-library-refuted' 'dotnet' @('build', $standardProject, '-c', 'Release', '--configfile', $nugetConfig) 1
    $standardResult = Join-Path $standardRoot 'artifacts/sharpproof/result.json'
    $standardResponse = Get-Content -LiteralPath $standardResult -Raw | ConvertFrom-Json
    Save-Reports ([IO.Path]::GetDirectoryName($standardResult)) 'standard-library-refuted'
    Copy-Item -LiteralPath (Join-Path $standardRoot 'Counter.cs') -Destination (Join-Path $evidenceRoot 'standard-library-refuted')
    $standardClaim = @($standardResponse.claimResults | Where-Object outcome -CEQ 'Refuted')[0].claimId
    $standardExport = Join-Path $fixtureRoot 'StandardRegression'
    Run 'standard-library-export' $tool @('export-test', '--project', $standardProject, '--result', $standardResult, '--claim', $standardClaim, '--output', $standardExport)
    Copy-Item -LiteralPath $standardExport -Destination (Join-Path $evidenceRoot 'exported-standard-regression') -Recurse
    $standardTestProject = Join-Path $standardExport 'SharpProof.Regression.csproj'
    [xml]$standardTestXml = Get-Content -LiteralPath $standardTestProject -Raw
    if ($standardTestXml.Project.PropertyGroup.TargetFramework -cne 'net8.0') { throw 'A netstandard library needs a runnable NUnit host.' }
    $standardResults = Join-Path $evidenceRoot 'regression-standard'
    Run 'standard-library-test-fails' 'dotnet' @('test', $standardTestProject, '-c', 'Release', '--logger', 'trx', '--results-directory', $standardResults) 1
    Assert-RegressionResult $standardResults 'Failed'
    Run 'doctor-missing-project' $tool @('doctor', '--project', (Join-Path $fixtureRoot 'missing.csproj')) 2
    Run 'export-existing-directory' $tool @('export-test', '--project', $nativeProject, '--result', $resultPath, '--claim', $claim, '--output', $exportRoot) 2
    # Export only fixture source and reports, never compiler intermediates or tool caches.
    $retained = Join-Path $evidenceRoot 'consumer'
    [void][IO.Directory]::CreateDirectory($retained)
    foreach ($name in @('Native.csproj', 'Counter.cs', 'compose.yaml', 'Directory.Build.props', 'global.json')) {
        Copy-Item -LiteralPath (Join-Path $native $name) -Destination $retained
    }
    Copy-Item -LiteralPath $resultRoot -Destination (Join-Path $retained 'evidence') -Recurse
    $status = 'passed'
}
finally {
    if ($null -eq $previousDeveloperNugetPackages) { Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue }
    else { $env:NUGET_PACKAGES = $previousDeveloperNugetPackages }
    if ($null -ne $fixtureRoot -and (Test-Path -LiteralPath (Join-Path $fixtureRoot 'Native') -PathType Container)) {
        $nativeFixture = Join-Path $fixtureRoot 'Native'
        $retainedFixture = Join-Path $evidenceRoot 'consumer'
        [void][IO.Directory]::CreateDirectory($retainedFixture)
        foreach ($name in @('Native.csproj', 'Counter.cs', 'compose.yaml', 'Directory.Build.props', 'global.json')) {
            $fixtureInput = Join-Path $nativeFixture $name
            if (Test-Path -LiteralPath $fixtureInput -PathType Leaf) { Copy-Item -LiteralPath $fixtureInput -Destination $retainedFixture -Force }
        }
        $reports = Join-Path $nativeFixture 'artifacts/sharpproof'
        if (Test-Path -LiteralPath $reports -PathType Container) {
            $retainedReports = Join-Path $retainedFixture 'evidence'
            [void][IO.Directory]::CreateDirectory($retainedReports)
            foreach ($report in Get-ChildItem -LiteralPath $reports -File) { Copy-Item -LiteralPath $report.FullName -Destination $retainedReports -Force }
        }
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'timings.json'), (ConvertTo-Json -InputObject @($timings.ToArray()) -Depth 5), [Text.UTF8Encoding]::new($false))
    $run = [ordered]@{
        schemaVersion              = 1
        status                     = $status
        scope                      = if ($PackOnly) { 'packages' } else { 'developer-experience' }
        qualificationMode          = $QualificationMode
        repositoryAnalyzersEnabled = $QualificationMode -eq 'AnalyzerEnabled'
        sourceCommit               = $sourceCommit
        packageVersion             = $version
        consumerImage              = $ConsumerImage
        toolPackageMode            = $toolPackageMode
        qualifiedToolSha256        = $qualifiedToolSha256
        developerBuildMode         = if ($preparedDeveloperBuild) { 'qualified-artifact' } else { 'built-in-run' }
        qualifiedCodeFixesSha256   = $qualifiedCodeFixesSha256
        startedAtUtc               = $startedAt
        finishedAtUtc              = [DateTimeOffset]::UtcNow.ToString('O')
        phaseCount                 = $timings.Count
        packages                   = @($packageEvidence.ToArray())
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'run.json'), (ConvertTo-Json -InputObject $run -Depth 5), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'qualification-mode.txt'), "$QualificationMode`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Developer-experience evidence: $evidenceRoot"
}
