[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateSet('Required', 'Graceful')]
    [string]$ExpectedSmt = 'Required'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$wrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
$fixtureRoot = Join-Path $PSScriptRoot 'package-consumers'
$artifactRoot = Join-Path $repoRoot 'artifacts/package-consumers'
$runRoot = Join-Path $artifactRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$packageSource = Join-Path $runRoot 'packages'
$consumerRoot = Join-Path $runRoot 'consumers'
$packageCache = Join-Path $runRoot '.nuget'
$isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

function Invoke-DotnetCommand {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        if ($isWindowsHost) {
            $lines = @(& $wrapper -MemoryLimitMb 6144 -TimeoutSeconds 600 @Arguments 2>&1)
        }
        else {
            $lines = @(& dotnet @Arguments 2>&1)
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $output = $lines -join [Environment]::NewLine
    if ($output.Length -ne 0) {
        Write-Host $output
    }
    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode."
    }

    return $output
}

function Expand-ProjectTemplate {
    param(
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$PackageVersion
    )

    $content = [System.IO.File]::ReadAllText($TemplatePath)
    $content = $content.Replace('__PACKAGE_VERSION__', $PackageVersion).Replace("`r`n", "`n")
    [System.IO.File]::WriteAllText(
        $DestinationPath,
        $content,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-EvaluatedProjectProperty {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    # This property-only evaluation does not run build targets. Capture its
    # single-line output directly; the Job Object wrapper intentionally writes
    # child output to the host instead of the PowerShell pipeline.
    Push-Location $repoRoot
    try {
        $lines = @(& dotnet msbuild $ProjectPath -nologo "-getProperty:$PropertyName" 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $output = $lines -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        throw "Evaluating $PropertyName from '$ProjectPath' failed with exit code $exitCode.$([Environment]::NewLine)$output"
    }

    $value = @($output -split '\r?\n') |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Last 1
    if ($null -eq $value) {
        throw "Project '$ProjectPath' does not evaluate $PropertyName."
    }
    return ([string]$value).Trim()
}

$previousPackageCache = $env:NUGET_PACKAGES
New-Item -ItemType Directory -Force -Path $packageSource, $consumerRoot, $packageCache | Out-Null

try {
    $analyzerProject = Join-Path $repoRoot 'SharpProof.Package/SharpProof.Package.csproj'
    $attributesProject = Join-Path $repoRoot 'SharpProof.Attributes/SharpProof.Attributes.csproj'
    $symbolicProject = Join-Path $repoRoot 'SharpProof.Symbolic/SharpProof.Symbolic.csproj'
    $analyzerVersion = Get-EvaluatedProjectProperty $analyzerProject 'PackageVersion'
    $attributesVersion = Get-EvaluatedProjectProperty $attributesProject 'Version'
    $symbolicVersion = Get-EvaluatedProjectProperty $symbolicProject 'Version'
    if ($analyzerVersion -ne $attributesVersion -or $analyzerVersion -ne $symbolicVersion) {
        throw "Package versions do not match: analyzer '$analyzerVersion', attributes '$attributesVersion', symbolic '$symbolicVersion'."
    }

    [void](Invoke-DotnetCommand @('restore', (Join-Path $repoRoot 'SharpProof.sln')) $repoRoot)
    [void](Invoke-DotnetCommand @(
        'build', $analyzerProject,
        '--configuration', $Configuration,
        '--no-restore',
        '/m:1',
        '/warnaserror') $repoRoot)
    [void](Invoke-DotnetCommand @(
        'pack', $analyzerProject,
        '--configuration', $Configuration,
        '--no-build',
        '--output', $packageSource) $repoRoot)
    [void](Invoke-DotnetCommand @(
        'pack', $attributesProject,
        '--configuration', $Configuration,
        '--no-build',
        '--output', $packageSource) $repoRoot)
    [void](Invoke-DotnetCommand @(
        'pack', $symbolicProject,
        '--configuration', $Configuration,
        '--no-build',
        '--output', $packageSource) $repoRoot)

    $env:NUGET_PACKAGES = $packageCache
    $nugetConfigPath = Join-Path $runRoot 'NuGet.Config'
    $escapedPackageSource = [System.Security.SecurityElement]::Escape($packageSource)
    $nugetConfig = '<configuration><packageSources><clear />' +
        '<add key="local-sharpproof" value="' + $escapedPackageSource + '" />' +
        '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />' +
        '</packageSources></configuration>'
    [System.IO.File]::WriteAllText(
        $nugetConfigPath,
        $nugetConfig,
        [System.Text.UTF8Encoding]::new($false))
    $symbolicConsumer = Join-Path $consumerRoot 'SymbolicConsumer'
    New-Item -ItemType Directory -Path $symbolicConsumer | Out-Null
    Expand-ProjectTemplate `
        (Join-Path $fixtureRoot 'SymbolicConsumer.csproj.template') `
        (Join-Path $symbolicConsumer 'SymbolicConsumer.csproj') `
        $symbolicVersion
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'SymbolicConsumer.cs') `
        -Destination (Join-Path $symbolicConsumer 'Program.cs')
    [void](Invoke-DotnetCommand @(
        'restore', (Join-Path $symbolicConsumer 'SymbolicConsumer.csproj'),
        '--configfile', $nugetConfigPath,
        '--packages', $packageCache) $symbolicConsumer)
    [void](Invoke-DotnetCommand @(
        'run',
        '--project', (Join-Path $symbolicConsumer 'SymbolicConsumer.csproj'),
        '--configuration', $Configuration,
        '--no-restore',
        '--', $ExpectedSmt) $symbolicConsumer)

    $analyzerConsumer = Join-Path $consumerRoot 'AnalyzerConsumer'
    New-Item -ItemType Directory -Path $analyzerConsumer | Out-Null
    Expand-ProjectTemplate `
        (Join-Path $fixtureRoot 'AnalyzerConsumer.csproj.template') `
        (Join-Path $analyzerConsumer 'AnalyzerConsumer.csproj') `
        $analyzerVersion
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'AnalyzerConsumer.cs') `
        -Destination (Join-Path $analyzerConsumer 'AnalyzerConsumer.cs')
    [void](Invoke-DotnetCommand @(
        'restore', (Join-Path $analyzerConsumer 'AnalyzerConsumer.csproj'),
        '--configfile', $nugetConfigPath,
        '--packages', $packageCache) $analyzerConsumer)
    $analyzerDiagnosticLog = Join-Path $analyzerConsumer 'analyzer-diagnostics.sarif'
    [void](Invoke-DotnetCommand @(
        'build', (Join-Path $analyzerConsumer 'AnalyzerConsumer.csproj'),
        '--configuration', $Configuration,
        '--no-restore',
        "/p:ErrorLog=$analyzerDiagnosticLog") $analyzerConsumer)
    if (-not (Test-Path -LiteralPath $analyzerDiagnosticLog)) {
        throw 'The analyzer consumer did not produce its compiler diagnostic log.'
    }
    $analyzerDiagnostics = [System.IO.File]::ReadAllText($analyzerDiagnosticLog)
    $analyzerSarif = $analyzerDiagnostics | ConvertFrom-Json
    $loadFailureIds = @('AD0001', 'CS8032', 'CS8034', 'CS8785')
    $loadFailures = @($analyzerSarif.runs | ForEach-Object { $_.results } | Where-Object {
        $loadFailureIds -contains $_.ruleId
    })
    if ($loadFailures.Count -ne 0) {
        throw 'The packaged analyzer reported an analyzer/generator load failure.'
    }
    $sharpProofDiagnostics = @($analyzerSarif.runs | ForEach-Object { $_.results } | Where-Object {
        $_.ruleId -eq 'SP0004'
    })
    if ($sharpProofDiagnostics.Count -eq 0) {
        throw 'The analyzer consumer did not report SP0004, so analyzer loading was not proven.'
    }

    $runtimeDescription = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription + '/' +
        [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    Write-Host "Package consumers passed for $runtimeDescription with expectation $ExpectedSmt."
}
finally {
    $env:NUGET_PACKAGES = $previousPackageCache
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    if (-not $resolvedRunRoot.StartsWith($resolvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove package-consumer run directory outside '$resolvedArtifactRoot'."
    }
    if (Test-Path -LiteralPath $resolvedRunRoot) {
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
}
