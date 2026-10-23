[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$ConsumerImage,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$SourceRevision,
    [Parameter(Mandatory)][string]$NuGetConfig,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Workspace template acceptance requires the canonical container.'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-workspace-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixtureRoot)
$hive = Join-Path $fixtureRoot 'hive'
$phases = [Collections.Generic.List[object]]::new()
$status = 'failed'
function Invoke-Workspace([string]$Name, [string]$WorkingDirectory, [string[]]$Arguments, [int]$Expected = 0) {
    $log = Join-Path $output "$Name.log"
    Push-Location $WorkingDirectory
    try { & dotnet @Arguments *> $log; $observed = $LASTEXITCODE }
    finally { Pop-Location }
    $phases.Add([ordered]@{ phase = $Name; expectedExit = $Expected; observedExit = $observed })
    if ($observed -ne $Expected) {
        Get-Content -LiteralPath $log -Tail 30 | Write-Host
        throw "Workspace phase $Name returned $observed; expected $Expected."
    }
    $global:LASTEXITCODE = 0
}
function Assert-WorkspaceRegression([string]$Directory, [string]$Outcome) {
    $reports = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -File)
    if ($reports.Count -ne 1) { throw 'Expected one executed workspace NUnit report.' }
    [xml]$report = Get-Content -LiteralPath $reports[0].FullName -Raw
    $tests = @($report.TestRun.Results.UnitTestResult)
    if ($tests.Count -ne 1 -or $tests[0].testName -cne 'ReplayedInputSatisfiesPostcondition' -or $tests[0].outcome -cne $Outcome -or
        [int]$report.TestRun.ResultSummary.Counters.executed -ne 1) { throw 'The workspace regression did not execute the expected assertion.' }
    if ($Outcome -ceq 'Failed' -and -not ([string]$tests[0].Output.ErrorInfo.Message).Contains('The original postcondition must hold.')) {
        throw 'The workspace regression failed before its postcondition assertion.'
    }
}
function Assert-EditorDiagnostic([string]$SarifPath, [int]$Expected) {
    $sarif = Get-Content -LiteralPath $SarifPath -Raw | ConvertFrom-Json
    $diagnostics = @($sarif.runs | ForEach-Object { $_.results } | Where-Object ruleId -CEQ 'SPED0001')
    if ($diagnostics.Count -ne $Expected) { throw "Expected $Expected optional-editor suggestions; found $($diagnostics.Count)." }
}
function Assert-EditorMarker([string]$Phase, [string]$WorkingDirectory, [string]$ProjectPath, [bool]$HasEditor, [string]$ExpectedValue) {
    $arguments = @('msbuild', $ProjectPath, '-nologo', '-getProperty:SharpProofEditorSuggestions', '-getItem:CompilerVisibleProperty')
    if ($ExpectedValue -ceq 'false') { $arguments += '-p:SharpProofEditorSuggestions=false' }
    Invoke-Workspace $Phase $WorkingDirectory $arguments
    $evaluation = Get-Content -LiteralPath (Join-Path $output "$Phase.log") -Raw | ConvertFrom-Json
    $markers = @($evaluation.Items.CompilerVisibleProperty | Where-Object Identity -CEQ 'SharpProofEditorSuggestions')
    $expectedCount = if ($HasEditor) { 1 } else { 0 }
    if ($markers.Count -ne $expectedCount -or $evaluation.Properties.SharpProofEditorSuggestions -cne $ExpectedValue) {
        throw 'The optional editor marker is not imported and exposed exactly once, or its override was ignored.'
    }
}
try {
    Invoke-Workspace 'install-workspace-template' $fixtureRoot @('new', '--debug:custom-hive', $hive, 'install', $PackagePath)
    foreach ($variant in @('portable', 'container')) {
        foreach ($editor in @('true', 'false')) {
            $name = if ($variant -eq 'container') { 'WorkspaceNative' } else { 'WorkspacePortable' }
            $suffix = if ($editor -eq 'true') { '' } else { 'WithoutEditor' }
            $root = Join-Path $fixtureRoot "$name$suffix"
            $phase = "$variant-$editor"
            Invoke-Workspace "generate-$phase" $fixtureRoot @('new', '--debug:custom-hive', $hive, 'sharpproof-workspace', '-n', $name, '-o', $root, '--verification', $variant, '--editor', $editor)
            $projectRelative = "src/app/$name.csproj"
            [xml]$project = Get-Content -LiteralPath (Join-Path $root $projectRelative) -Raw
            $references = @($project.Project.ItemGroup.PackageReference)
            $facade = @($references | Where-Object { $_.Include -ceq 'SharpProof' -and $_.Version -ceq $PackageVersion })
            $fixes = @($references | Where-Object { $_.Include -ceq 'SharpProof.CodeFixes' -and $_.Version -ceq $PackageVersion -and $_.PrivateAssets -ceq 'all' })
            $expectedCount = if ($editor -eq 'true') { 2 } else { 1 }
            if ($references.Count -ne $expectedCount -or $facade.Count -ne 1 -or $fixes.Count -ne ($expectedCount - 1)) { throw 'Workspace editor selection or package pinning is incorrect.' }
            $expectedVerify = if ($variant -eq 'container') { 'true' } else { 'false' }
            $expectedProfile = if ($variant -eq 'container') { 'strict' } else { 'advisory' }
            if ($project.Project.PropertyGroup.SharpProofVerify -cne $expectedVerify -or $project.Project.PropertyGroup.SharpProofProfile -cne $expectedProfile -or
                $project.Project.PropertyGroup.TargetFramework -cne 'net10.0' -or $project.Project.PropertyGroup.LangVersion -cne '13.0') { throw 'Workspace compiler or verification policy changed.' }
            if (@(Get-ChildItem -LiteralPath $root -Filter '*.csproj' -File).Count -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $root 'tests') -PathType Container)) {
                throw 'The workspace did not separate app and test directories.'
            }
            $manifest = Get-Content -LiteralPath (Join-Path $root '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
            if ($manifest.version -ne 1 -or -not $manifest.isRoot -or $manifest.tools.'sharpproof.tool'.version -cne $PackageVersion -or
                (@($manifest.tools.'sharpproof.tool'.commands) -join '|') -cne 'dotnet-sharpproof') { throw 'The workspace tool manifest is not pinned to the qualified version.' }
            [xml]$feed = Get-Content -LiteralPath (Join-Path $root 'NuGet.config') -Raw
            $privateSource = @($feed.configuration.packageSources.add | Where-Object key -CEQ 'SharpProofPreview')
            $mapping = $feed.SelectNodes('/configuration/packageSourceMapping/packageSource[@key="SharpProofPreview"]/package')
            if ($privateSource.Count -ne 1 -or $privateSource[0].value -cne 'https://nuget.pkg.github.com/alexyorke/index.json' -or
                $mapping.Count -ne 1 -or $mapping[0].pattern -cne 'SharpProof*' -or $null -ne $feed.SelectSingleNode('/configuration/packageSourceCredentials')) {
                throw 'The workspace private feed is not isolated or contains embedded credentials.'
            }
            if ($variant -eq 'container') {
                $workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/sharpproof.yml'))
                if (-not $workflow.Contains("sharpproof-consumer.yml@$SourceRevision") -or -not $workflow.Contains("workflow-revision: $SourceRevision") -or
                    -not $workflow.Contains("project: $projectRelative") -or -not $workflow.Contains("image: $ConsumerImage") -or -not $workflow.Contains('mode: report')) {
                    throw 'The workspace baseline caller is not pinned to its qualified source, image, and project.'
                }
            }
            elseif (Test-Path -LiteralPath (Join-Path $root 'compose.yaml')) { throw 'The portable workspace included the native service.' }
            if ($editor -eq 'false') {
                if ($variant -eq 'portable') {
                    $coreSarif = Join-Path $output 'core-only.sarif'
                    Invoke-Workspace 'build-core-only' $root @('build', $projectRelative, '-c', 'Release', '--configfile', $NuGetConfig, "-p:ErrorLog=$coreSarif")
                    Assert-EditorDiagnostic $coreSarif 0
                    Assert-EditorMarker 'evaluate-core-only-marker' $root $projectRelative $false ''
                }
                continue
            }
            Invoke-Workspace "tool-restore-$variant" $root @('tool', 'restore', '--configfile', $NuGetConfig)
            $editorSarif = Join-Path $output "editor-$variant.sarif"
            Invoke-Workspace "build-workspace-$variant" $root @('build', $projectRelative, '-c', 'Release', '--configfile', $NuGetConfig, "-p:ErrorLog=$editorSarif")
            Assert-EditorDiagnostic $editorSarif 1
            Assert-EditorMarker "evaluate-editor-marker-$variant" $root $projectRelative $true 'true'
            Invoke-Workspace "doctor-workspace-$variant" $root @('tool', 'run', 'dotnet-sharpproof', '--', 'doctor', '--project', $projectRelative, '--verification', $variant)
            if ($variant -eq 'portable') {
                $disabledSarif = Join-Path $output 'editor-marker-disabled.sarif'
                Invoke-Workspace 'build-editor-marker-disabled' $root @('build', $projectRelative, '-c', 'Release', '--no-restore', '-t:Rebuild', '-p:SharpProofEditorSuggestions=false', "-p:ErrorLog=$disabledSarif")
                Assert-EditorDiagnostic $disabledSarif 0
                Assert-EditorMarker 'evaluate-disabled-editor-marker' $root $projectRelative $true 'false'
            }
        }
    }
    $native = Join-Path $fixtureRoot 'WorkspaceNative'
    $projectPath = 'src/app/WorkspaceNative.csproj'
    $source = Join-Path $native 'src/app/Counter.cs'
    $original = [IO.File]::ReadAllText($source)
    [IO.File]::WriteAllText($source, $original.Replace('return checked(value + 1);', 'return value;'), [Text.UTF8Encoding]::new($false))
    Invoke-Workspace 'workspace-refuted' $native @('build', $projectPath, '-c', 'Release', '--no-restore') 1
    $result = Join-Path $native 'artifacts/sharpproof/result.json'
    $response = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    $claim = @($response.claimResults | Where-Object outcome -CEQ 'Refuted')[0].claimId
    Invoke-Workspace 'workspace-export' $native @('tool', 'run', 'dotnet-sharpproof', '--', 'export-test', '--project', $projectPath, '--result', 'artifacts/sharpproof/result.json', '--claim', $claim, '--workspace', '.', '--output', 'tests/SharpProof.Regression')
    Invoke-Workspace 'workspace-export-second' $native @('tool', 'run', 'dotnet-sharpproof', '--', 'export-test', '--project', $projectPath, '--result', 'artifacts/sharpproof/result.json', '--claim', $claim, '--workspace', '.', '--output', 'tests/SharpProof.Second')
    $testProject = 'tests/SharpProof.Regression/SharpProof.Regression.csproj'
    Invoke-Workspace 'workspace-first-outputs' $native @('msbuild', $testProject, '-nologo', '-getProperty:BaseIntermediateOutputPath,BaseOutputPath')
    Invoke-Workspace 'workspace-second-outputs' $native @('msbuild', 'tests/SharpProof.Second/SharpProof.Regression.csproj', '-nologo', '-getProperty:BaseIntermediateOutputPath,BaseOutputPath')
    $firstOutputs = Get-Content -LiteralPath (Join-Path $output 'workspace-first-outputs.log') -Raw | ConvertFrom-Json
    $secondOutputs = Get-Content -LiteralPath (Join-Path $output 'workspace-second-outputs.log') -Raw | ConvertFrom-Json
    if ($firstOutputs.Properties.BaseIntermediateOutputPath -ceq $secondOutputs.Properties.BaseIntermediateOutputPath -or
        $firstOutputs.Properties.BaseOutputPath -ceq $secondOutputs.Properties.BaseOutputPath) { throw 'Distinct regression projects collide in the workspace output volumes.' }
    $before = Join-Path $output 'regression-before'
    Invoke-Workspace 'workspace-regression-before' $native @('test', $testProject, '-c', 'Release', '--logger', 'trx', '--results-directory', $before, "-p:RestoreConfigFile=$NuGetConfig") 1
    Assert-WorkspaceRegression $before 'Failed'
    [IO.File]::WriteAllText($source, $original, [Text.UTF8Encoding]::new($false))
    $after = Join-Path $output 'regression-after'
    Invoke-Workspace 'workspace-regression-after' $native @('test', $testProject, '-c', 'Release', '--logger', 'trx', '--results-directory', $after, "-p:RestoreConfigFile=$NuGetConfig")
    Assert-WorkspaceRegression $after 'Passed'
    Invoke-Workspace 'workspace-fixed' $native @('build', $projectPath, '-c', 'Release', '--no-restore')
    $retained = Join-Path ([IO.Path]::GetDirectoryName($output)) 'workspace-consumer'
    foreach ($file in Get-ChildItem -LiteralPath $native -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($native, $file.FullName)
        if ($relative -match '(^|[/\\])(bin|obj|\.sharpproof-obj|\.sharpproof-bin)([/\\]|$)') { continue }
        $destination = Join-Path $retained $relative
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    $status = 'passed'
}
finally {
    [IO.File]::WriteAllText((Join-Path $output 'workspace-run.json'), (ConvertTo-Json -InputObject ([ordered]@{ status = $status; packageVersion = $PackageVersion; sourceRevision = $SourceRevision; consumerImage = $ConsumerImage; phases = @($phases.ToArray()) }) -Depth 6), [Text.UTF8Encoding]::new($false))
    Write-Host "Workspace acceptance evidence: $output"
}
