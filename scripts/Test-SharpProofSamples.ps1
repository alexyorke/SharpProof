[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateSet('Required', 'Graceful')]
    [string]$ExpectedSmt,

    [Parameter()]
    [string]$PackageSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$samplesRoot = Join-Path $repositoryRoot 'samples'
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
$isWindowsHost =
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Windows)
$isSupportedWorkerHost = $isWindowsHost -and
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
        [Runtime.InteropServices.Architecture]::X64 -and
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq
        [Runtime.InteropServices.Architecture]::X64
$expectedHostPolicy = if ($isSupportedWorkerHost) {
    'Required'
}
else {
    'Graceful'
}
if ([string]::IsNullOrWhiteSpace($ExpectedSmt)) {
    $ExpectedSmt = $expectedHostPolicy
}
elseif ($ExpectedSmt -ne $expectedHostPolicy) {
    throw (
        "ExpectedSmt='$ExpectedSmt' does not match this host's " +
        "'$expectedHostPolicy' verifier policy.")
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
        [string[]]$Arguments,

        [Parameter()]
        [int]$TimeoutSeconds = 300
    )

    $script:dotnetInvocationOrdinal++
    $effectiveArguments = [Collections.Generic.List[string]]::new(
        [string[]]$Arguments)
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

    $lines = if ($isWindowsHost) {
        @(
            & $dotnetWrapper `
                -MemoryLimitMb 6144 `
                -TimeoutSeconds $TimeoutSeconds `
                @effectiveArguments 2>&1
        )
    }
    else {
        @(& dotnet @effectiveArguments 2>&1)
    }
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    $capturedOutput =
        @($lines | ForEach-Object { $_.ToString() }) -join "`n"
    $loggedOutput = if ($null -ne $logPath -and
        (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        Get-Content -LiteralPath $logPath -Raw
    }
    else {
        ''
    }
    $output = $capturedOutput + "`n" + $loggedOutput
    if (-not $isWindowsHost -and
        -not [string]::IsNullOrWhiteSpace($capturedOutput)) {
        Write-Host $capturedOutput
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Assert-ExitCode {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Result,

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
        [pscustomobject]$Result,

        [Parameter(Mandatory)]
        [string[]]$Values,

        [Parameter(Mandatory)]
        [string]$Operation
    )

    foreach ($value in $Values) {
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

function Test-SampleProjectInventory {
    $projectFiles = @(
        Get-ChildItem -LiteralPath $samplesRoot -Filter '*.csproj' -Recurse |
            Sort-Object FullName
    )
    if ($projectFiles.Count -ne 8) {
        throw "The sample inventory must contain exactly 8 projects."
    }

    $allowedPackages = [Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            'SharpProof',
            'SharpProof.Attributes',
            'SharpProof.Verifier.Win-x64'
        ),
        [StringComparer]::Ordinal)
    foreach ($projectFile in $projectFiles) {
        [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw
        if ($project.SelectNodes('//ProjectReference').Count -ne 0) {
            throw "Sample project references are forbidden: $($projectFile.FullName)"
        }
        $references = @($project.SelectNodes('//PackageReference'))
        if ($references.Count -eq 0) {
            throw "Sample project has no package reference: $($projectFile.FullName)"
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
    $solutionProjects = @(
        $solution.SelectNodes('/Solution/Project') |
            ForEach-Object { ([string]$_.Path).Replace('\', '/') } |
            Sort-Object
    )
    $diskProjects = @(
        $projectFiles |
            ForEach-Object {
                [IO.Path]::GetRelativePath(
                    $samplesRoot,
                    $_.FullName).Replace('\', '/')
            } |
            Sort-Object
    )
    if (($solutionProjects -join '|') -ne ($diskProjects -join '|')) {
        throw 'SharpProof.Samples.slnx must list the exact sample inventory.'
    }
}

function New-LocalPackageFeed {
    $feed = Join-Path $temporaryRoot 'nupkgs'
    [void](New-Item -ItemType Directory -Path $feed)
    $manifest = Get-Content -LiteralPath (
        Join-Path $repositoryRoot 'scripts\package-projects.json') -Raw |
        ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw 'Unsupported package-project manifest schema.'
    }
    foreach ($project in @($manifest.projects)) {
        $projectPath = Join-Path $repositoryRoot ([string]$project)
        $pack = Invoke-CapturedDotNet -TimeoutSeconds 900 -Arguments @(
            'pack',
            $projectPath,
            '--configuration',
            $Configuration,
            '--output',
            $feed,
            '--nologo'
        )
        Assert-ExitCode $pack $true "Packing $projectPath"
    }
    return $feed
}

function Invoke-SampleBuild {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectName,

        [Parameter(Mandatory)]
        [string]$RunName,

        [Parameter()]
        [string[]]$Properties = @()
    )

    $projectPath = Join-Path (
        Join-Path $samplesRoot $ProjectName) "$ProjectName.csproj"
    $runRoot = Join-Path $temporaryRoot (
        'work/' + $RunName.ToLowerInvariant())
    $obj = Get-ForwardSlashPath (Join-Path $runRoot 'obj')
    $bin = Get-ForwardSlashPath (Join-Path $runRoot 'bin')
    $commonProperties = @(
        "-p:BaseIntermediateOutputPath=$obj/",
        "-p:BaseOutputPath=$bin/",
        "-p:RestorePackagesPath=$(Get-ForwardSlashPath $packageCache)"
    ) + $Properties

    $restore = Invoke-CapturedDotNet -Arguments (
        @(
            'restore',
            $projectPath,
            '--source',
            $script:resolvedPackageSource,
            '--packages',
            $packageCache,
            '--no-http-cache',
            '--force',
            '--nologo'
        ) + $commonProperties)
    Assert-ExitCode $restore $true "Restoring $RunName"

    return Invoke-CapturedDotNet -Arguments (
        @(
            'build',
            $projectPath,
            '--configuration',
            $Configuration,
            '--no-restore',
            '--nologo',
            '--verbosity',
            'minimal'
        ) + $commonProperties)
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
        @('SP0027', 'SP0045', 'SP0047') `
        'Expected-diagnostics build'

    $malformed = Invoke-SampleBuild `
        -ProjectName 'MalformedContract' `
        -RunName 'MalformedContract-expected'
    Assert-ExitCode $malformed $false 'Malformed-contract build'
    Assert-OutputContains $malformed @('SP0024') 'Malformed-contract build'

    $strictResultPath = Get-ForwardSlashPath (
        Join-Path $temporaryRoot 'results/library.json')
    $strict = Invoke-SampleBuild `
        -ProjectName 'Library' `
        -RunName 'Library-strict' `
        -Properties @(
            '-p:ContinuousIntegrationBuild=true',
            "-p:SharpProofVerifyResultFile=$strictResultPath"
        )
    if ($isSupportedWorkerHost) {
        Assert-ExitCode $strict $true 'Strict library build'
        $strictResult = Read-WorkerResult $strictResultPath
        if ([string]$strictResult.runStatus -ne 'Complete' -or
            [string]$strictResult.failureReason -ne 'None' -or
            @($strictResult.claimResults).Count -ne 1 -or
            @($strictResult.claimResults |
                Where-Object { [string]$_.outcome -ne 'Proven' }).Count -ne 0) {
            throw 'The strict library sample did not prove every selected claim.'
        }

        $outcomesResultPath = Get-ForwardSlashPath (
            Join-Path $temporaryRoot 'results/outcomes.json')
        $outcomes = Invoke-SampleBuild `
            -ProjectName 'Outcomes' `
            -RunName 'Outcomes-explicit' `
            -Properties @(
                '-p:SharpProofVerify=true',
                '-p:SharpProofVerifyPolicy=advisory',
                "-p:SharpProofVerifyResultFile=$outcomesResultPath"
            )
        Assert-ExitCode $outcomes $false 'Mixed-outcomes verification'
        Assert-OutputContains `
            $outcomes `
            @('failed with exit code 5') `
            'Mixed-outcomes verification'
        $outcomeResult = Read-WorkerResult $outcomesResultPath
        if ([string]$outcomeResult.runStatus -ne 'Complete' -or
            [string]$outcomeResult.failureReason -ne 'None') {
            throw 'Mixed-outcomes verification did not complete normally.'
        }
        $actualOutcomes = @(
            $outcomeResult.claimResults |
                ForEach-Object { [string]$_.outcome } |
                Sort-Object
        )
        $expectedOutcomes = @('Proven', 'Refuted', 'Unknown') | Sort-Object
        if (($actualOutcomes -join '|') -ne ($expectedOutcomes -join '|')) {
            throw (
                "Mixed-outcomes verification returned '$($actualOutcomes -join ', ')' " +
                "instead of '$($expectedOutcomes -join ', ')'.")
        }
        $unknownClaims = @(
            $outcomeResult.claimResults |
                Where-Object { [string]$_.outcome -eq 'Unknown' }
        )
        if ($unknownClaims.Count -ne 1) {
            throw 'Mixed-outcomes verification must return exactly one Unknown.'
        }
        $unknownReason = [string]$unknownClaims[0].reason
        if ($unknownReason -in @('', 'None', 'Unspecified')) {
            throw 'The Unknown sample claim must have a typed non-None reason.'
        }
    }
    else {
        Assert-ExitCode $strict $false 'Unsupported-host strict library build'
        Assert-OutputContains `
            $strict `
            @('supported only on Windows x64') `
            'Unsupported-host strict library build'
    }

    Write-Host (
        "SharpProof package-backed samples passed ($ExpectedSmt host policy).")
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    if (-not $resolvedTemporaryRoot.StartsWith(
            $temporaryParent,
            [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedTemporaryRoot -eq $temporaryParent) {
        throw "Refusing to remove unexpected temporary path: $resolvedTemporaryRoot"
    }
    if (Test-Path -LiteralPath $resolvedTemporaryRoot) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
