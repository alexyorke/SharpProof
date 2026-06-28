[CmdletBinding(DefaultParameterSetName = 'Direct')]
param(
    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 4096,

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [Parameter(ParameterSetName = 'Spec', Mandatory = $true)]
    [string]$ArtifactSpecPath,

    [Parameter(ParameterSetName = 'Direct')]
    [string[]]$AssemblyPath,

    [Parameter(ParameterSetName = 'Direct')]
    [string]$Framework = 'net8.0',

    [Parameter(ParameterSetName = 'Direct')]
    [string]$RuntimeAssemblyName = 'System.Private.CoreLib.dll',

    [Parameter(ParameterSetName = 'Direct')]
    [string[]]$SymbolPrefix,

    [Parameter(ParameterSetName = 'Direct')]
    [switch]$IncludeCallees,

    [Parameter(ParameterSetName = 'Direct')]
    [int]$MaxDepth = 1,

    [Parameter(ParameterSetName = 'Direct')]
    [switch]$TransitiveRoots,

    [Parameter(ParameterSetName = 'Direct')]
    [switch]$ClassifyPurity,

    [Parameter(ParameterSetName = 'Direct')]
    [switch]$CompareManualCatalogs,

    [Parameter(ParameterSetName = 'Direct')]
    [int]$Limit,

    [Parameter(ParameterSetName = 'Direct')]
    [string]$OutputPath,

    [Parameter(ParameterSetName = 'Direct')]
    [string]$OutputDirectory,

    [Parameter(ParameterSetName = 'Direct')]
    [string]$OutputName,

    [Parameter(ParameterSetName = 'Direct')]
    [switch]$AllowUnfilteredRuntimeScan,

    [Parameter(ParameterSetName = 'Direct')]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetWrapper = Join-Path $repoRoot 'scripts\Invoke-PurelySharpDotnet.ps1'
$projectPath = Join-Path $repoRoot 'Tools\PurelySharp.EffectSummary\PurelySharp.EffectSummary.csproj'

function Assert-ExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description path was empty."
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing ${Description}: $Path"
    }
}

function Resolve-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Expected a non-empty path."
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($repoRoot, $Path))
}

function ConvertTo-SafeFileStem {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $safeValue = [regex]::Replace($Value.Trim(), '[^A-Za-z0-9._-]+', '-')
    $safeValue = [regex]::Replace($safeValue, '-{2,}', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safeValue)) {
        return 'effect-summary'
    }

    return $safeValue
}

function Get-DefaultOutputStem {
    param(
        [string[]]$ResolvedAssemblyPaths,
        [string[]]$RequestedSymbolPrefixes,
        [string]$TargetFramework,
        [string]$TargetRuntimeAssemblyName
    )

    if (@($RequestedSymbolPrefixes).Count -gt 0) {
        return 'symbol-' + (ConvertTo-SafeFileStem -Value $RequestedSymbolPrefixes[0])
    }

    if (@($ResolvedAssemblyPaths).Count -eq 1) {
        return 'assembly-' + (ConvertTo-SafeFileStem -Value ([System.IO.Path]::GetFileNameWithoutExtension($ResolvedAssemblyPaths[0])))
    }

    if (@($ResolvedAssemblyPaths).Count -gt 1) {
        return 'assemblies'
    }

    return 'runtime-' + (ConvertTo-SafeFileStem -Value "$TargetFramework-$TargetRuntimeAssemblyName")
}

function Resolve-GeneratedOutputPath {
    param(
        [string[]]$ResolvedAssemblyPaths,
        [string[]]$RequestedSymbolPrefixes,
        [string]$TargetFramework,
        [string]$TargetRuntimeAssemblyName,
        [string]$ExplicitOutputPath,
        [string]$ExplicitOutputDirectory,
        [string]$ExplicitOutputName,
        [switch]$AllowOverwrite
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitOutputPath) -and
        (-not [string]::IsNullOrWhiteSpace($ExplicitOutputDirectory) -or -not [string]::IsNullOrWhiteSpace($ExplicitOutputName))) {
        throw 'Use either -OutputPath or -OutputDirectory/-OutputName, not both.'
    }

    $timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')

    if (-not [string]::IsNullOrWhiteSpace($ExplicitOutputPath)) {
        $resolvedOutputPath = Resolve-RepoPath -Path $ExplicitOutputPath
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($ExplicitOutputDirectory)) {
            $resolvedOutputDirectory = Resolve-RepoPath -Path $ExplicitOutputDirectory
            $includeTimestampInFileName = $true
        }
        else {
            $resolvedOutputDirectory = [System.IO.Path]::Combine($repoRoot, 'artifacts', 'effect-summary', $timestamp)
            $includeTimestampInFileName = $false
        }

        $outputStem = if ([string]::IsNullOrWhiteSpace($ExplicitOutputName)) {
            Get-DefaultOutputStem `
                -ResolvedAssemblyPaths $ResolvedAssemblyPaths `
                -RequestedSymbolPrefixes $RequestedSymbolPrefixes `
                -TargetFramework $TargetFramework `
                -TargetRuntimeAssemblyName $TargetRuntimeAssemblyName
        }
        else {
            ConvertTo-SafeFileStem -Value $ExplicitOutputName
        }

        if ($includeTimestampInFileName -and [string]::IsNullOrWhiteSpace($ExplicitOutputName)) {
            $outputStem = "$outputStem.$timestamp"
        }

        $resolvedOutputPath = [System.IO.Path]::Combine(
            $resolvedOutputDirectory,
            "$outputStem.PurelySharp.EffectSummary.json")
    }

    $resolvedOutputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)
    if ([string]::IsNullOrWhiteSpace($resolvedOutputDirectory)) {
        throw "Unable to resolve an output directory from '$resolvedOutputPath'."
    }

    if (Test-Path -LiteralPath $resolvedOutputDirectory) {
        $directoryItem = Get-Item -LiteralPath $resolvedOutputDirectory
        if (-not $directoryItem.PSIsContainer) {
            throw "Output directory path is a file: $resolvedOutputDirectory"
        }
    }

    if ((Test-Path -LiteralPath $resolvedOutputPath) -and -not $AllowOverwrite.IsPresent) {
        throw "Output file already exists. Use -Force to overwrite it: $resolvedOutputPath"
    }

    return $resolvedOutputPath
}

function Invoke-DotnetWrapper {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$DotnetArgs,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & $dotnetWrapper -MemoryLimitMb $MemoryLimitMb -DotnetArgs $DotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

Assert-ExistingPath -Path $dotnetWrapper -Description 'dotnet wrapper'
Assert-ExistingPath -Path $projectPath -Description 'effect summary tool project'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found on PATH.'
}

$resolvedAssemblyPaths = @()
$resolvedOutputPath = $null
$resolvedArtifactSpecPath = $null

if ($PSCmdlet.ParameterSetName -eq 'Spec') {
    $resolvedArtifactSpecPath = Resolve-RepoPath -Path $ArtifactSpecPath
    Assert-ExistingPath -Path $resolvedArtifactSpecPath -Description 'artifact spec'
}
else {
    $resolvedAssemblyPaths = @($AssemblyPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Resolve-RepoPath -Path $_ })
    $requestedSymbolPrefixes = @($SymbolPrefix | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($TransitiveRoots.IsPresent -and -not $IncludeCallees.IsPresent) {
        throw '-TransitiveRoots requires -IncludeCallees.'
    }

    if (($PSBoundParameters.ContainsKey('MaxDepth')) -and -not $IncludeCallees.IsPresent -and $MaxDepth -ne 1) {
        throw '-MaxDepth requires -IncludeCallees.'
    }

    if (@($resolvedAssemblyPaths).Count -eq 0 -and @($requestedSymbolPrefixes).Count -eq 0 -and -not $AllowUnfilteredRuntimeScan.IsPresent) {
        throw 'Direct refresh requires -SymbolPrefix or -AssemblyPath. Use -AllowUnfilteredRuntimeScan for an intentional full runtime scan.'
    }

    $resolvedOutputPath = Resolve-GeneratedOutputPath `
        -ResolvedAssemblyPaths $resolvedAssemblyPaths `
        -RequestedSymbolPrefixes $requestedSymbolPrefixes `
        -TargetFramework $Framework `
        -TargetRuntimeAssemblyName $RuntimeAssemblyName `
        -ExplicitOutputPath $OutputPath `
        -ExplicitOutputDirectory $OutputDirectory `
        -ExplicitOutputName $OutputName `
        -AllowOverwrite:$Force
}

Push-Location $repoRoot
try {
    Write-Host 'Refreshing ad hoc effect-summary artifacts.' -ForegroundColor Yellow
    Write-Host 'Outputs default to ignored local paths under artifacts\effect-summary unless you provide -OutputPath or an explicit artifact spec.' -ForegroundColor Yellow

    Write-Host 'Building effect summary tool...' -ForegroundColor Cyan
    Invoke-DotnetWrapper `
        -DotnetArgs @('build', $projectPath, '-c', $Configuration, '-m:20') `
        -FailureMessage 'Failed to build effect summary tool.'

    if ($PSCmdlet.ParameterSetName -eq 'Spec') {
        Write-Host "Running caller-supplied artifact spec: $resolvedArtifactSpecPath" -ForegroundColor Cyan
        Invoke-DotnetWrapper `
            -DotnetArgs @('run', '--project', $projectPath, '-c', $Configuration, '--no-build', '--', '--artifact-spec', $resolvedArtifactSpecPath) `
            -FailureMessage 'Failed to generate effect summaries from the supplied artifact spec.'
    }
    else {
        $toolArgs = @('run', '--project', $projectPath, '-c', $Configuration, '--no-build', '--')

        foreach ($resolvedAssemblyPath in $resolvedAssemblyPaths) {
            $toolArgs += @('--assembly', $resolvedAssemblyPath)
        }

        if (@($resolvedAssemblyPaths).Count -eq 0) {
            $toolArgs += @('--framework', $Framework)
            if (-not [string]::IsNullOrWhiteSpace($RuntimeAssemblyName)) {
                $toolArgs += @('--runtime-assembly', $RuntimeAssemblyName)
            }
        }

        foreach ($requestedSymbolPrefix in $requestedSymbolPrefixes) {
            $toolArgs += @('--symbol-prefix', $requestedSymbolPrefix)
        }

        if ($IncludeCallees.IsPresent) {
            $toolArgs += '--include-callees'
            $toolArgs += @('--max-depth', $MaxDepth.ToString())
        }

        if ($TransitiveRoots.IsPresent) {
            $toolArgs += '--transitive-roots'
        }

        if ($CompareManualCatalogs.IsPresent) {
            $toolArgs += '--compare-manual-catalogs'
        }
        elseif ($ClassifyPurity.IsPresent) {
            $toolArgs += '--classify-purity'
        }

        if ($PSBoundParameters.ContainsKey('Limit')) {
            $toolArgs += @('--limit', $Limit.ToString())
        }

        $toolArgs += @('--output', $resolvedOutputPath)

        Write-Host "Writing effect summary to: $resolvedOutputPath" -ForegroundColor Cyan
        Invoke-DotnetWrapper `
            -DotnetArgs $toolArgs `
            -FailureMessage 'Failed to generate the requested effect summary.'

        Write-Host "Done. Generated: $resolvedOutputPath" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
