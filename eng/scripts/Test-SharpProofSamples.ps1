[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [string]$PackageSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sampleConfiguration = $Configuration
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$samplesRoot = Join-Path $repositoryRoot 'src/samples'
$isSupportedWorkerHost = $IsLinux -and
$env:SHARPPROOF_CONTAINER -ceq '1' -and
[Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
[Runtime.InteropServices.Architecture]::X64 -and
[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq
[Runtime.InteropServices.Architecture]::X64
if (-not $isSupportedWorkerHost) {
    throw 'SharpProof samples must run in the canonical Linux amd64 container.'
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent (
    'SharpProof.Samples.' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $temporaryRoot)
$packageCache = Join-Path $temporaryRoot 'packages'
[void](New-Item -ItemType Directory -Path $packageCache)
$script:dotnetInvocationOrdinal = 0

function Invoke-CapturedDotNet {
    param(
        [Parameter(Mandatory)]
        [Nullable[Collections.Immutable.ImmutableArray[string]]]$Arguments
    )

    if ($null -eq $Arguments -or $Arguments.IsDefault) { throw [ArgumentNullException]::new('Arguments') }
    if ($Arguments.Length -eq 0) { throw [ArgumentException]::new('Arguments must not be empty.', 'Arguments') }
    foreach ($argument in $Arguments) { if ([string]::IsNullOrEmpty($argument)) { throw [ArgumentException]::new('Arguments must not contain null or empty strings.', 'Arguments') } }
    $script:dotnetInvocationOrdinal++
    $effectiveArguments = $Arguments.ToBuilder()
    $logPath = $null
    if ($Arguments[0] -in @(
            'build',
            'msbuild',
            'pack',
            'restore',
            'test')) {
        $logDirectory = Join-Path $temporaryRoot 'logs'
        [void](New-Item -ItemType Directory -Path $logDirectory -Force)
        $logPath = Join-Path $logDirectory (
            $script:dotnetInvocationOrdinal.ToString(
                'D3',
                [Globalization.CultureInfo]::InvariantCulture) + '.log')
        $effectiveArguments.Add(
            "-flp:logfile=$logPath;verbosity=normal")
    }

    [string[]]$nativeArguments = @($effectiveArguments.ToImmutable())
    $lineBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    & dotnet @nativeArguments 2>&1 | ForEach-Object { $lineBuilder.Add($_.ToString()) }
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    $lines = $lineBuilder.ToImmutable()
    $capturedOutput =
    $lines -join "`n"
    $loggedOutput = if ($null -ne $logPath -and
        (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        Get-Content -LiteralPath $logPath -Raw
    }
    else {
        ''
    }
    $output = $capturedOutput + "`n" + $loggedOutput
    if (-not [string]::IsNullOrWhiteSpace($capturedOutput)) {
        Write-Host $capturedOutput
    }
    return ([ordered]@{
            ExitCode = $exitCode
            Output   = $output
        }).AsReadOnly()
}

function Assert-ExitCode {
    param(
        [Parameter(Mandatory)]
        [object]$Result,

        [Parameter(Mandatory)]
        [bool]$SuccessExpected,

        [Parameter(Mandatory)]
        [string]$Operation
    )

    $succeeded = $Result.ExitCode -eq 0
    if ($succeeded -ne $SuccessExpected) {
        $expectation = if ($SuccessExpected) { 'succeed' } else { 'fail' }
        throw (
            "$Operation was expected to $expectation but exited with " +
            "$($Result.ExitCode).`n$($Result.Output)")
    }
}

function Assert-OutputContains {
    param(
        [Parameter(Mandatory)]
        [object]$Result,

        [Parameter(Mandatory)]
        [Nullable[Collections.Immutable.ImmutableArray[string]]]$Values,

        [Parameter(Mandatory)]
        [string]$Operation
    )

    if ($null -eq $Values -or $Values.IsDefault) { throw [ArgumentNullException]::new('Values') }
    if ($Values.Length -eq 0) { throw [ArgumentException]::new('Values must not be empty.', 'Values') }
    foreach ($value in $Values) {
        if ([string]::IsNullOrEmpty($value)) { throw [ArgumentException]::new('Values must not contain null or empty strings.', 'Values') }
        if (-not $Result.Output.Contains(
                $value,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Operation output did not contain '$value'."
        }
    }
}

function Get-ForwardSlashPath {
    param([Parameter(Mandatory)][string]$Path)

    return [IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function New-IsolatedNuGetConfig {
    param([Parameter(Mandatory)][string]$Source)

    $path = Join-Path $temporaryRoot 'NuGet.config'
    $escapedSource = [Security.SecurityElement]::Escape(
        (Get-ForwardSlashPath $Source))
    $content = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="SharpProofLocal" value="$escapedSource" />
    <add key="nuget.org"
         value="https://api.nuget.org/v3/index.json"
         protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="SharpProofLocal">
      <package pattern="SharpProof*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.*" />
      <package pattern="NETStandard.*" />
      <package pattern="runtime.*" />
      <package pattern="System.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    [IO.File]::WriteAllText(
        $path,
        $content.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
    return $path
}

function Test-SampleProjectInventory {
    $projectFiles = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            Get-ChildItem -LiteralPath $samplesRoot -Filter '*.csproj' -Recurse |
                Sort-Object FullName
        ))
    if ($projectFiles.Count -ne 8) {
        throw "The sample inventory must contain exactly 8 projects."
    }

    $allowedPackages = [Collections.Immutable.ImmutableHashSet]::CreateRange[string](
        [StringComparer]::Ordinal, [string[]]$SharpProofPackageIds)
    [xml]$sharedProject = Get-Content -LiteralPath (
        Join-Path $samplesRoot 'Directory.Build.props') -Raw
    $sharedReferences = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($sharedProject.SelectNodes('//PackageReference')))
    foreach ($projectFile in $projectFiles) {
        [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw
        if ($project.SelectNodes('//ProjectReference').Count -ne 0) {
            throw "Sample project references are forbidden: $($projectFile.FullName)"
        }
        $referenceBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
        [void]$referenceBuilder.AddRange([object[]]@($project.SelectNodes('//PackageReference')))
        [void]$referenceBuilder.AddRange([object[]]$sharedReferences)
        $references = $referenceBuilder.ToImmutable()
        if ($references.Count -ne 1 -or [string]$references[0].Include -cne 'SharpProof' -or
            $references[0].HasAttribute('PrivateAssets')) {
            throw "Every sample must consume one public SharpProof package reference: $($projectFile.FullName)"
        }
        foreach ($reference in $references) {
            $id = [string]$reference.Include
            if (-not $allowedPackages.Contains($id)) {
                throw "Unexpected sample package reference '$id'."
            }
            if ([string]$reference.Version -ne
                '$(SharpProofSamplePackageVersion)') {
                throw (
                    "Sample package '$id' must use the centralized " +
                    '$(SharpProofSamplePackageVersion).')
            }
        }
    }

    [xml]$solution = Get-Content -LiteralPath (
        Join-Path $samplesRoot 'SharpProof.Samples.slnx') -Raw
    $solutionProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            $solution.SelectNodes('/Solution/Project') |
                ForEach-Object { ([string]$_.Path).Replace('\', '/') } |
                Sort-Object
        ))
    $diskProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            $projectFiles |
                ForEach-Object {
                    [IO.Path]::GetRelativePath(
                        $samplesRoot,
                        $_.FullName).Replace('\', '/')
                } |
                Sort-Object
        ))
    if (($solutionProjects -join '|') -ne ($diskProjects -join '|')) {
        throw 'SharpProof.Samples.slnx must list the exact sample inventory.'
    }
}

function New-LocalPackageFeed {
    $feed = Join-Path $temporaryRoot 'nupkgs'
    [void](New-Item -ItemType Directory -Path $feed)
    $manifest = Get-Content -LiteralPath (
        Join-Path $repositoryRoot 'eng\scripts\package-projects.json') -Raw |
        ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw 'Unsupported package-project manifest schema.'
    }
    foreach ($project in @($manifest.projects)) {
        $projectPath = Join-Path $repositoryRoot ([string]$project)
        $build = Invoke-CapturedDotNet -Arguments ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    'build', $projectPath, '--configuration', $Configuration, '--nologo',
                    '-p:GeneratePackageOnBuild=false')))
        Assert-ExitCode $build $true "Building $projectPath before symbol extraction"
    }
    & (Join-Path $PSScriptRoot 'Export-SharpProofEmbeddedSymbols.ps1') `
        -RepositoryRoot $repositoryRoot -Configuration $Configuration
    foreach ($project in @($manifest.projects)) {
        $projectPath = Join-Path $repositoryRoot ([string]$project)
        $pack = Invoke-CapturedDotNet -Arguments ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    'pack',
                    $projectPath,
                    '--configuration',
                    $sampleConfiguration,
                    '--output',
                    $feed,
                    '--no-build',
                    '--no-restore',
                    '--nologo'
                )))
        Assert-ExitCode $pack $true "Packing $projectPath"
    }
    return $feed
}

function Invoke-SampleBuild {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$RunName,
        [Parameter()][Nullable[Collections.Immutable.ImmutableArray[string]]]$Properties = $null
    )
    $projectPath = Join-Path (Join-Path $samplesRoot $ProjectName) "$ProjectName.csproj"
    $runRoot = Join-Path $temporaryRoot ('work/' + $RunName.ToLowerInvariant())
    $obj = Get-ForwardSlashPath (Join-Path $runRoot 'obj')
    $bin = Get-ForwardSlashPath (Join-Path $runRoot 'bin')
    $commonPropertyBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $commonPropertyBuilder.Add("-p:BaseIntermediateOutputPath=$obj/")
    $commonPropertyBuilder.Add("-p:BaseOutputPath=$bin/")
    $commonPropertyBuilder.Add('-p:UseSharedCompilation=false')
    $commonPropertyBuilder.Add("-p:RestorePackagesPath=$(Get-ForwardSlashPath $packageCache)")
    if ($null -ne $Properties -and -not $Properties.IsDefault) { $commonPropertyBuilder.AddRange([string[]]@($Properties)) }
    $commonProperties = $commonPropertyBuilder.ToImmutable()
    $restoreArguments = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $restoreArguments.AddRange([Collections.Immutable.ImmutableArray]::Create[string]('restore', $projectPath, '--configfile', $script:nugetConfig, '--packages', $packageCache, '--no-http-cache', '--force', '--nologo'))
    $restoreArguments.AddRange($commonProperties)
    $restore = Invoke-CapturedDotNet -Arguments $restoreArguments.ToImmutable()
    Assert-ExitCode $restore $true "Restoring $RunName"
    $buildArguments = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $buildArguments.AddRange([Collections.Immutable.ImmutableArray]::Create[string]('build', $projectPath, '--configuration', $sampleConfiguration, '--no-restore', '--nologo', '--verbosity', 'minimal'))
    $buildArguments.AddRange($commonProperties)
    return Invoke-CapturedDotNet -Arguments $buildArguments.ToImmutable()
}

function Read-WorkerResult {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected worker result was not published: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

try {
    Test-SampleProjectInventory

    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        $PackageSource = New-LocalPackageFeed
    }
    $script:resolvedPackageSource = (
        Resolve-Path -LiteralPath $PackageSource -ErrorAction Stop).Path
    $script:nugetConfig =
    New-IsolatedNuGetConfig $script:resolvedPackageSource
    & (Join-Path $PSScriptRoot 'Test-SharpProofPackageConsumers.ps1') `
        -PackageSource $script:resolvedPackageSource `
        -ValidatePackageSourceOnly

    foreach ($projectName in @(
            'Effects',
            'Preconditions',
            'ContractFor',
            'TrustedBoundary',
            'Library',
            'Outcomes')) {
        $build = Invoke-SampleBuild `
            -ProjectName $projectName `
            -RunName ($projectName + '-advisory')
        Assert-ExitCode $build $true "$projectName advisory build"
    }

    $diagnostics = Invoke-SampleBuild `
        -ProjectName 'Diagnostics' `
        -RunName 'Diagnostics-expected'
    Assert-ExitCode $diagnostics $true 'Expected-diagnostics build'
    Assert-OutputContains `
        $diagnostics `
    ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('SP0027', 'SP0045', 'SP0047'))) `
        'Expected-diagnostics build'

    $malformed = Invoke-SampleBuild `
        -ProjectName 'MalformedContract' `
        -RunName 'MalformedContract-expected'
    Assert-ExitCode $malformed $false 'Malformed-contract build'
    Assert-OutputContains $malformed ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('SP0024'))) 'Malformed-contract build'

    $strictResultPath = Get-ForwardSlashPath (
        Join-Path $temporaryRoot 'results/library.json')
    $strict = Invoke-SampleBuild `
        -ProjectName 'Library' `
        -RunName 'Library-strict' `
        -Properties ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                '-p:ContinuousIntegrationBuild=true',
                "-p:SharpProofVerifyResultFile=$strictResultPath"
            )))
    Assert-ExitCode $strict $true 'Strict library build'
    $strictResult = Read-WorkerResult $strictResultPath
    if ([string]$strictResult.runStatus -ne 'Complete' -or
        [string]$strictResult.failureReason -ne 'None' -or
        @($strictResult.claimResults).Count -ne 5 -or
        @($strictResult.claimResults |
                Where-Object { [string]$_.outcome -ne 'Proven' }).Count -ne 0) {
        throw 'The strict library sample did not prove every selected claim.'
    }

    $outcomesResultPath = Get-ForwardSlashPath (
        Join-Path $temporaryRoot 'results/outcomes.json')
    $outcomes = Invoke-SampleBuild `
        -ProjectName 'Outcomes' `
        -RunName 'Outcomes-explicit' `
        -Properties ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                '-p:SharpProofVerify=true',
                '-p:SharpProofVerifyPolicy=advisory',
                "-p:SharpProofVerifyResultFile=$outcomesResultPath"
            )))
    Assert-ExitCode $outcomes $false 'Mixed-outcomes verification'
    Assert-OutputContains `
        $outcomes `
    ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('error SP0051: Refuted Postcondition claim'))) `
        'Mixed-outcomes verification'
    $outcomeResult = Read-WorkerResult $outcomesResultPath
    if ([string]$outcomeResult.runStatus -ne 'Complete' -or
        [string]$outcomeResult.failureReason -ne 'None') {
        throw 'Mixed-outcomes verification did not complete normally.'
    }
    $actualOutcomes = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            $outcomeResult.claimResults |
                ForEach-Object { [string]$_.outcome } |
                Sort-Object
        ))
    $expectedOutcomes = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(@('Proven', 'Refuted', 'Unknown') | Sort-Object))
    if (($actualOutcomes -join '|') -ne ($expectedOutcomes -join '|')) {
        throw (
            "Mixed-outcomes verification returned '$($actualOutcomes -join ', ')' " +
            "instead of '$($expectedOutcomes -join ', ')'.")
    }
    $unknownClaims = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            $outcomeResult.claimResults |
                Where-Object { [string]$_.outcome -eq 'Unknown' }
        ))
    if ($unknownClaims.Count -ne 1) {
        throw 'Mixed-outcomes verification must return exactly one Unknown.'
    }
    $unknownReason = [string]$unknownClaims[0].reason
    if ($unknownReason -in @('', 'None', 'Unspecified')) {
        throw 'The Unknown sample claim must have a typed non-None reason.'
    }

    Write-Host 'SharpProof package-backed samples passed.'
}
finally {
    $resolvedTemporaryRoot = Resolve-SharpProofContainedPath `
        -Root $temporaryParent -Path $temporaryRoot `
        -ParameterName 'Sample temporary path'
    if (Test-Path -LiteralPath $resolvedTemporaryRoot) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
