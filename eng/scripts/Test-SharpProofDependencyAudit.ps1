[CmdletBinding(DefaultParameterSetName = 'Execute')]
param(
    [Parameter()]
    [string]$SolutionPath = 'SharpProof.slnx',

    [Parameter()]
    [string]$NuGetConfigurationPath = 'NuGet.config',

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Report')]
    [string]$ReportPath,

    [Parameter(ParameterSetName = 'Execute')]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$auditTimeoutSeconds = $TimeoutSeconds

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path `
        $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force

function Resolve-RepositoryPathValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path $repositoryRoot $Path
    }
    return [IO.Path]::GetFullPath($candidate)
}

function Resolve-InputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $resolved = Resolve-RepositoryPathValue -Path $Path
    if (-not [IO.File]::Exists($resolved)) {
        throw "$Description is missing: '$Path'."
    }
    return $resolved
}

function Resolve-OutputPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return Resolve-RepositoryPathValue -Path $Path
}

function Get-ExactProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $property = $Value.PSObject.Properties |
        Where-Object { $_.Name -ceq $Name } |
        Select-Object -First 1
    if ($null -eq $property) {
        throw "Dependency audit report is missing '$Name'."
    }
    return , $property.Value
}

function ConvertTo-CanonicalHttpsSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $uri = $null
    if ([string]::IsNullOrWhiteSpace($Value) -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -cne [Uri]::UriSchemeHttps -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw "Dependency audit source must be a canonical HTTPS URI: '$Value'."
    }
    return $uri.AbsoluteUri
}

function Get-ExpectedAuditSources {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigurationPath
    )

    [xml]$configuration = Get-Content `
        -LiteralPath $ConfigurationPath `
        -Raw
    $section = $configuration.SelectSingleNode(
        '/configuration/auditSources')
    if ($null -eq $section) {
        throw (
            'NuGet configuration requires an explicit <auditSources> ' +
            'section; package-source fallback is not accepted.')
    }
    $elementBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    [int]$clearElementCount = 0
    [int]$sourceNodeCount = 0
    [bool]$hasUnexpectedElement = $false
    foreach ($childNode in $section.ChildNodes) {
        if ($childNode.NodeType -ne [Xml.XmlNodeType]::Element) {
            continue
        }
        $elementBuilder.Add($childNode)
        if ($childNode.Name -ceq 'clear') {
            $clearElementCount++
        }
        elseif ($childNode.Name -ceq 'add') {
            $sourceNodeCount++
        }
        else {
            $hasUnexpectedElement = $true
        }
    }
    $elements = $elementBuilder.ToImmutable()
    if ($clearElementCount -ne 1 -or
        $elements.Length -eq 0 -or
        $elements[0].Name -cne 'clear' -or
        $hasUnexpectedElement) {
        throw (
            'NuGet audit sources must be hermetic: <auditSources> ' +
            'requires exactly one leading <clear /> followed only by ' +
            '<add /> elements.')
    }
    if ($sourceNodeCount -eq 0) {
        throw 'NuGet configuration has no approved audit source.'
    }
    $sources = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $seen = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    $keys = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    foreach ($sourceNode in $elements) {
        if ($sourceNode.Name -cne 'add') {
            continue
        }
        $keyAttribute = $sourceNode.Attributes['key']
        $attribute = $sourceNode.Attributes['value']
        if ($null -eq $keyAttribute -or
            [string]::IsNullOrWhiteSpace($keyAttribute.Value) -or
            -not $keys.Add([string]$keyAttribute.Value)) {
            throw 'NuGet audit source keys must be present and unique.'
        }
        if ($null -eq $attribute) {
            throw 'A NuGet audit source has no value attribute.'
        }
        $source = ConvertTo-CanonicalHttpsSource `
            -Value ([string]$attribute.Value)
        if (-not $seen.Add($source)) {
            throw "NuGet audit source is duplicated: '$source'."
        }
        $sources.Add($source)
    }
    $sources.Sort([StringComparer]::Ordinal)
    return , $sources.ToImmutable()
}

function Get-SolutionProjects {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $solutionDirectory = [IO.Path]::GetDirectoryName($Path)
    $solutionPrefix = [IO.Path]::GetFullPath(
        $solutionDirectory.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar)
    try {
        [xml]$solution = [IO.File]::ReadAllText($Path)
    }
    catch {
        throw "Solution is not valid XML: '$Path': $($_.Exception.Message)"
    }
    $projects = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $seen = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($project in @($solution.SelectNodes('/Solution//Project'))) {
        $pathAttribute = $project.Attributes['Path']
        if ($null -eq $pathAttribute -or
            [string]::IsNullOrWhiteSpace($pathAttribute.Value)) {
            throw "Solution contains a project without a Path: '$Path'."
        }
        if ([IO.Path]::GetExtension($pathAttribute.Value) -cne '.csproj') {
            continue
        }
        $relativePath = $pathAttribute.Value.Replace(
            '/', [IO.Path]::DirectorySeparatorChar).Replace(
            '\', [IO.Path]::DirectorySeparatorChar)
        $absolutePath = if ([IO.Path]::IsPathRooted($relativePath)) {
            [IO.Path]::GetFullPath($relativePath)
        }
        else {
            [IO.Path]::GetFullPath(
                (Join-Path $solutionDirectory $relativePath))
        }
        if (-not $absolutePath.StartsWith(
                $solutionPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Solution project is outside the solution directory: '$($pathAttribute.Value)'."
        }
        if (-not $seen.Add($absolutePath)) {
            throw "Solution project is duplicated: '$($pathAttribute.Value)'."
        }
        $projects.Add(([ordered]@{
                    absolute = $absolutePath
                    relative = [IO.Path]::GetRelativePath(
                        $solutionDirectory,
                        $absolutePath).Replace('\', '/')
                }).AsReadOnly())
    }
    if ($projects.Count -eq 0) {
        throw "Solution contains no C# projects: '$Path'."
    }
    return , $projects.ToImmutable()
}

function Invoke-DependencyAudit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Solution,

        [Parameter(Mandatory = $true)]
        [string]$Configuration,

        [Parameter(Mandatory = $true)]
        [string]$TemporaryDirectory
    )

    $standardOutput = Join-Path `
        $TemporaryDirectory `
    ('.dependency-audit.' + [Guid]::NewGuid().ToString('N') + '.json')
    $standardError = [IO.Path]::ChangeExtension($standardOutput, '.stderr.txt')
    $wrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
    $dotnetArguments = @(
        'list',
        $Solution,
        'package',
        '--vulnerable',
        '--include-transitive',
        '--format',
        'json',
        '--output-version',
        '1',
        '--config',
        $Configuration
    )
    try {
        $process = Start-SharpProofEncodedPowerShell `
            -WrapperPath $wrapper `
            -TimeoutSeconds $auditTimeoutSeconds `
            -Arguments $dotnetArguments `
            -WorkingDirectory $repositoryRoot `
            -StandardOutput $standardOutput `
            -StandardError $standardError
        try {
            $outputText = if ([IO.File]::Exists($standardOutput)) {
                [IO.File]::ReadAllText($standardOutput)
            }
            else {
                ''
            }
            $errorText = if ([IO.File]::Exists($standardError)) {
                [IO.File]::ReadAllText($standardError)
            }
            else {
                ''
            }
            if ($process.ExitCode -ne 0) {
                throw (
                    "NuGet dependency audit exited with code " +
                    "$($process.ExitCode): $errorText$outputText")
            }
            if (-not [string]::IsNullOrWhiteSpace($errorText)) {
                throw (
                    'NuGet dependency audit wrote to standard error: ' +
                    $errorText)
            }
            if (-not [IO.File]::Exists($standardOutput)) {
                throw 'NuGet dependency audit produced no JSON report.'
            }
            return $outputText
        }
        finally {
            $process.Dispose()
        }
    }
    finally {
        if ([IO.File]::Exists($standardOutput)) {
            [IO.File]::Delete($standardOutput)
        }
        if ([IO.File]::Exists($standardError)) {
            [IO.File]::Delete($standardError)
        }
    }
}

$resolvedSolution = Resolve-InputPath `
    -Path $SolutionPath `
    -Description 'Solution'
$resolvedConfiguration = Resolve-InputPath `
    -Path $NuGetConfigurationPath `
    -Description 'NuGet configuration'
$reportCandidate = if ($PSCmdlet.ParameterSetName -eq 'Report') {
    Resolve-OutputPath `
        -Path $ReportPath
}
else {
    $null
}
$resolvedReport = if ($null -ne $reportCandidate) {
    Resolve-InputPath `
        -Path $reportCandidate `
        -Description 'Dependency audit report'
}
else {
    $null
}
$resolvedOutput = Resolve-OutputPath -Path $OutputPath
$outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw "OutputPath has no parent directory: '$OutputPath'."
}
$solutionDirectory = [IO.Path]::GetDirectoryName($resolvedSolution)
$outputRelativeToSolution = [IO.Path]::GetRelativePath(
    $solutionDirectory,
    $resolvedOutput)
if ([IO.Path]::IsPathRooted($outputRelativeToSolution) -or
    $outputRelativeToSolution -eq '..' -or
    $outputRelativeToSolution.StartsWith(
        '..' + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::Ordinal)) {
    throw 'OutputPath must be inside the solution directory.'
}
if ([StringComparer]::OrdinalIgnoreCase.Equals(
        $resolvedOutput,
        $resolvedSolution) -or
    [StringComparer]::OrdinalIgnoreCase.Equals(
        $resolvedOutput,
        $resolvedConfiguration) -or
    ($null -ne $resolvedReport -and
    [StringComparer]::OrdinalIgnoreCase.Equals(
        $resolvedOutput,
        $resolvedReport))) {
    throw 'OutputPath cannot overwrite an audit input.'
}
[IO.Directory]::CreateDirectory($outputDirectory) |
    Out-Null
if ([IO.File]::Exists($resolvedOutput)) {
    [IO.File]::Delete($resolvedOutput)
}

$expectedSources = Get-ExpectedAuditSources `
    -ConfigurationPath $resolvedConfiguration
$solutionProjects = Get-SolutionProjects -Path $resolvedSolution
$reportJson = if ($PSCmdlet.ParameterSetName -eq 'Report') {
    [IO.File]::ReadAllText($resolvedReport)
}
else {
    Invoke-DependencyAudit `
        -Solution $resolvedSolution `
        -Configuration $resolvedConfiguration `
        -TemporaryDirectory $outputDirectory
}

try {
    $report = $reportJson | ConvertFrom-Json -ErrorAction Stop
}
catch {
    throw "Dependency audit report is not valid JSON: $($_.Exception.Message)"
}
if ($null -eq $report -or
    [int](Get-ExactProperty -Value $report -Name 'version') -ne 1) {
    throw 'Dependency audit report does not use JSON schema version 1.'
}
$parameters = [string](Get-ExactProperty `
        -Value $report `
        -Name 'parameters')
if ($parameters -cne '--vulnerable --include-transitive') {
    throw (
        "Dependency audit report has unexpected parameters: '$parameters'.")
}

$problemProperty = $report.PSObject.Properties |
    Where-Object { $_.Name -ceq 'problems' } |
    Select-Object -First 1
if ($null -ne $problemProperty) {
    if ($problemProperty.Value -isnot [Array]) {
        throw "Dependency audit report property 'problems' is not an array."
    }
    $problemBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($problem in $problemProperty.Value) {
        $problemBuilder.Add($problem)
    }
    $problems = $problemBuilder.ToImmutable()
    if ($problems.Length -gt 0) {
        $problemSummaryBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        foreach ($problem in $problems) {
            $level = [string](Get-ExactProperty `
                    -Value $problem `
                    -Name 'level')
            $text = [string](Get-ExactProperty `
                    -Value $problem `
                    -Name 'text')
            $problemSummaryBuilder.Add("$level`: $text")
        }
        $problemSummary = $problemSummaryBuilder.ToImmutable() -join '; '
        throw "Dependency audit report contains problems: $problemSummary"
    }
}

$sourceValue = Get-ExactProperty -Value $report -Name 'sources'
if ($sourceValue -isnot [Array]) {
    throw "Dependency audit report property 'sources' is not an array."
}
$observedSources = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$observedSourceSet = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
    [StringComparer]::Ordinal)
foreach ($sourceValueItem in @($sourceValue)) {
    $source = ConvertTo-CanonicalHttpsSource `
        -Value ([string]$sourceValueItem)
    if (-not $observedSourceSet.Add($source)) {
        throw "Dependency audit report duplicates source '$source'."
    }
    $observedSources.Add($source)
}
$observedSources.Sort([StringComparer]::Ordinal)
$observedSourceArray = $observedSources.ToImmutable()
if ([string]::Join("`n", $observedSourceArray) -cne
    [string]::Join("`n", $expectedSources)) {
    throw (
        'Dependency audit did not use the exact approved source set. ' +
        "Expected: $($expectedSources -join ', '); observed: " +
        "$($observedSourceArray -join ', ').")
}

$projectValue = Get-ExactProperty -Value $report -Name 'projects'
if ($projectValue -isnot [Array]) {
    throw "Dependency audit report property 'projects' is not an array."
}
$observedProjectBuilder = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
    [StringComparer]::OrdinalIgnoreCase)
$vulnerablePackageBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
foreach ($project in @($projectValue)) {
    $projectPath = [string](Get-ExactProperty `
            -Value $project `
            -Name 'path')
    if ([string]::IsNullOrWhiteSpace($projectPath)) {
        throw 'Dependency audit report contains an empty project path.'
    }
    if (-not [IO.Path]::IsPathFullyQualified($projectPath)) {
        throw (
            'Dependency audit report project paths must be absolute: ' +
            "'$projectPath'.")
    }
    $absoluteProjectPath = [IO.Path]::GetFullPath($projectPath)
    if (-not $observedProjectBuilder.Add($absoluteProjectPath)) {
        throw "Dependency audit report duplicates project '$projectPath'."
    }
    $frameworkProperty = $project.PSObject.Properties |
        Where-Object { $_.Name -ceq 'frameworks' } |
        Select-Object -First 1
    if ($null -eq $frameworkProperty) {
        throw (
            "Dependency audit report project '$projectPath' is missing " +
            "'frameworks'.")
    }
    if ($frameworkProperty.Value -isnot [Array]) {
        throw (
            "Dependency audit report project '$projectPath' has a " +
            "non-array 'frameworks' property.")
    }
    if ($frameworkProperty.Value.Count -eq 0) {
        throw (
            "Dependency audit report project '$projectPath' has an empty " +
            "'frameworks' array.")
    }
    $observedFrameworks =
    [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    foreach ($framework in @($frameworkProperty.Value)) {
        $frameworkName = [string](Get-ExactProperty `
                -Value $framework `
                -Name 'framework')
        if ([string]::IsNullOrWhiteSpace($frameworkName)) {
            throw (
                "Dependency audit report project '$projectPath' contains " +
                'an empty framework name.')
        }
        if (-not $observedFrameworks.Add($frameworkName)) {
            throw (
                "Dependency audit report project '$projectPath' duplicates " +
                "framework '$frameworkName'.")
        }
        foreach ($packagePropertyName in @(
                'topLevelPackages',
                'transitivePackages')) {
            $packageProperty = $framework.PSObject.Properties |
                Where-Object { $_.Name -ceq $packagePropertyName } |
                Select-Object -First 1
            if ($null -eq $packageProperty) {
                if ($packagePropertyName -ceq 'transitivePackages') {
                    continue
                }
                throw (
                    "Dependency audit report framework '$frameworkName' " +
                    "is missing '$packagePropertyName'.")
            }
            if ($packageProperty.Value -isnot [Array]) {
                throw (
                    "Dependency audit report '$packagePropertyName' is not " +
                    'an array.')
            }
            foreach ($package in @($packageProperty.Value)) {
                $packageId = [string](Get-ExactProperty `
                        -Value $package `
                        -Name 'id')
                $resolvedVersion = [string](Get-ExactProperty `
                        -Value $package `
                        -Name 'resolvedVersion')
                if ([string]::IsNullOrWhiteSpace($packageId) -or
                    [string]::IsNullOrWhiteSpace($resolvedVersion)) {
                    throw (
                        'Dependency audit report contains a vulnerable ' +
                        'package with an empty identity or version.')
                }
                $vulnerablePackageBuilder.Add("$packageId@$resolvedVersion")
            }
        }
    }
}

$expectedProjectBuilder = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
    [StringComparer]::OrdinalIgnoreCase)
foreach ($solutionProject in $solutionProjects) {
    [void]$expectedProjectBuilder.Add([string]$solutionProject.absolute)
}
$observedProjects = $observedProjectBuilder.ToImmutable()
$expectedProjectSet = $expectedProjectBuilder.ToImmutable()
if (-not $observedProjects.SetEquals($expectedProjectSet)) {
    $missingBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $expectedProjectSet |
        Where-Object { -not $observedProjects.Contains($_) } |
        Sort-Object |
        ForEach-Object {
            $missingBuilder.Add([string]$_)
        }
    $missing = $missingBuilder.ToImmutable()
    $inventedBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $observedProjects |
        Where-Object { -not $expectedProjectSet.Contains($_) } |
        Sort-Object |
        ForEach-Object {
            $inventedBuilder.Add([string]$_)
        }
    $invented = $inventedBuilder.ToImmutable()
    throw (
        'Dependency audit project coverage is incomplete or invented. ' +
        "Missing: $($missing -join ', '); invented: " +
        "$($invented -join ', ').")
}
$vulnerablePackages = $vulnerablePackageBuilder.ToImmutable()
if ($vulnerablePackages.Length -ne 0) {
    throw (
        'Dependency audit found vulnerable packages: ' +
        (($vulnerablePackages | Sort-Object -Unique) -join ', '))
}

$projectPathBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
foreach ($solutionProject in $solutionProjects) {
    $projectPathBuilder.Add([string]$solutionProject.relative)
}
$projectPathBuilder.Sort([StringComparer]::Ordinal)
$projectPaths = $projectPathBuilder.ToImmutable()
$evidence = ([ordered]@{
        schemaVersion = 1
        gate          = 'dependencyAudit'
        passed        = $true
        parameters    = $parameters
        auditSources  = $observedSourceArray
        projects      = $projectPaths
        counts        = ([ordered]@{
                projects           = $projectPaths.Length
                vulnerablePackages = 0
                problems           = 0
            }).AsReadOnly()
    }).AsReadOnly()
$json = ($evidence | ConvertTo-Json -Depth 6) -replace "`r`n", "`n"
$temporaryOutput = Join-Path `
    $outputDirectory `
('.' + [IO.Path]::GetFileName($resolvedOutput) + '.' +
    [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText(
        $temporaryOutput,
        $json + "`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryOutput, $resolvedOutput, $true)
}
finally {
    if ([IO.File]::Exists($temporaryOutput)) {
        [IO.File]::Delete($temporaryOutput)
    }
}

Write-Host (
    "Dependency audit passed for $($projectPaths.Length) projects using " +
    "$($observedSourceArray.Length) approved source(s).")
