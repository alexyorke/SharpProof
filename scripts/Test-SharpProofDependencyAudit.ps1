[CmdletBinding(DefaultParameterSetName = 'Execute')]
param(
    [Parameter()]
    [string]$SolutionPath = 'SharpProof.sln',

    [Parameter()]
    [string]$NuGetConfigurationPath = 'NuGet.Config',

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

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

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
    return ,$property.Value
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
    $elements = @($section.ChildNodes | Where-Object {
            $_.NodeType -eq [Xml.XmlNodeType]::Element
        })
    $clearElements = @($elements | Where-Object {
            $_.Name -ceq 'clear'
        })
    if ($clearElements.Count -ne 1 -or
        $elements.Count -eq 0 -or
        $elements[0].Name -cne 'clear' -or
        @($elements | Where-Object {
                $_.Name -cne 'clear' -and $_.Name -cne 'add'
            }).Count -ne 0) {
        throw (
            'NuGet audit sources must be hermetic: <auditSources> ' +
            'requires exactly one leading <clear /> followed only by ' +
            '<add /> elements.')
    }
    $sourceNodes = @($elements | Where-Object { $_.Name -ceq 'add' })
    if ($sourceNodes.Count -eq 0) {
        throw 'NuGet configuration has no approved audit source.'
    }
    $sources = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $keys = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($sourceNode in $sourceNodes) {
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
    $result = $sources.ToArray()
    [Array]::Sort($result, [StringComparer]::Ordinal)
    return $result
}

function Get-SolutionProjects {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $solutionDirectory = [IO.Path]::GetDirectoryName($Path)
    $projects = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($line in [IO.File]::ReadLines($Path)) {
        if ($line -notmatch
                '^Project\("\{[^"]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)",') {
            continue
        }
        $relativePath = $Matches[1].Replace('\', '/')
        $absolutePath = [IO.Path]::GetFullPath(
            (Join-Path $solutionDirectory $relativePath))
        if (-not $seen.Add($absolutePath)) {
            throw "Solution project is duplicated: '$relativePath'."
        }
        $projects.Add([pscustomobject][ordered]@{
                absolute = $absolutePath
                relative = [IO.Path]::GetRelativePath(
                    $solutionDirectory,
                    $absolutePath).Replace('\', '/')
            })
    }
    if ($projects.Count -eq 0) {
        throw "Solution contains no C# projects: '$Path'."
    }
    return $projects.ToArray()
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
    $quotedArguments = @(
        $dotnetArguments |
            ForEach-Object {
                "'" + ([string]$_).Replace("'", "''") + "'"
            }
    ) -join ','
    $escapedWrapper = $wrapper.Replace("'", "''")
    $command = (
        "& '$escapedWrapper' -TimeoutSeconds $TimeoutSeconds " +
        "@($quotedArguments); exit " +
        '$LASTEXITCODE')
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($command))
    try {
        $process = Start-Process `
            -FilePath 'pwsh' `
            -ArgumentList @(
                '-NoLogo',
                '-NoProfile',
                '-EncodedCommand',
                $encodedCommand
            ) `
            -WorkingDirectory $repositoryRoot `
            -Wait `
            -PassThru `
            -RedirectStandardOutput $standardOutput `
            -RedirectStandardError $standardError
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

[string[]]$expectedSources = @(
    Get-ExpectedAuditSources `
        -ConfigurationPath $resolvedConfiguration
)
[object[]]$solutionProjects = @(
    Get-SolutionProjects -Path $resolvedSolution
)
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
    $problems = @($problemProperty.Value)
    if ($problems.Count -gt 0) {
        $problemSummary = @(
            foreach ($problem in $problems) {
                $level = [string](Get-ExactProperty `
                        -Value $problem `
                        -Name 'level')
                $text = [string](Get-ExactProperty `
                        -Value $problem `
                        -Name 'text')
                "$level`: $text"
            }
        ) -join '; '
        throw "Dependency audit report contains problems: $problemSummary"
    }
}

$sourceValue = Get-ExactProperty -Value $report -Name 'sources'
if ($sourceValue -isnot [Array]) {
    throw "Dependency audit report property 'sources' is not an array."
}
$observedSources = [Collections.Generic.List[string]]::new()
$observedSourceSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($sourceValueItem in @($sourceValue)) {
    $source = ConvertTo-CanonicalHttpsSource `
        -Value ([string]$sourceValueItem)
    if (-not $observedSourceSet.Add($source)) {
        throw "Dependency audit report duplicates source '$source'."
    }
    $observedSources.Add($source)
}
$observedSourceArray = $observedSources.ToArray()
[Array]::Sort($observedSourceArray, [StringComparer]::Ordinal)
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
$observedProjects = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$vulnerablePackages = [Collections.Generic.List[string]]::new()
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
    if (-not $observedProjects.Add($absoluteProjectPath)) {
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
        [Collections.Generic.HashSet[string]]::new(
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
                $vulnerablePackages.Add("$packageId@$resolvedVersion")
            }
        }
    }
}

$expectedProjectSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($solutionProject in $solutionProjects) {
    [void]$expectedProjectSet.Add([string]$solutionProject.absolute)
}
if (-not $observedProjects.SetEquals($expectedProjectSet)) {
    $missing = @(
        $expectedProjectSet |
            Where-Object { -not $observedProjects.Contains($_) } |
            Sort-Object
    )
    $invented = @(
        $observedProjects |
            Where-Object { -not $expectedProjectSet.Contains($_) } |
            Sort-Object
    )
    throw (
        'Dependency audit project coverage is incomplete or invented. ' +
        "Missing: $($missing -join ', '); invented: " +
        "$($invented -join ', ').")
}
if ($vulnerablePackages.Count -ne 0) {
    throw (
        'Dependency audit found vulnerable packages: ' +
        (($vulnerablePackages.ToArray() | Sort-Object -Unique) -join ', '))
}

[string[]]$projectPaths = @(
    $solutionProjects |
        ForEach-Object { [string]$_.relative }
)
[Array]::Sort($projectPaths, [StringComparer]::Ordinal)
$evidence = [pscustomobject][ordered]@{
    schemaVersion = 1
    gate = 'dependencyAudit'
    passed = $true
    parameters = $parameters
    auditSources = $observedSourceArray
    projects = $projectPaths
    counts = [pscustomobject][ordered]@{
        projects = $projectPaths.Count
        vulnerablePackages = 0
        problems = 0
    }
}
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
    "Dependency audit passed for $($projectPaths.Count) projects using " +
    "$($observedSourceArray.Count) approved source(s).")
