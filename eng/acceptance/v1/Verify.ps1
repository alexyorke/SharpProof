[CmdletBinding()]
param(
    [Parameter()][switch]$RequireLineTarget,
    [Parameter()][ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter()][string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$acceptanceRoot = $PSScriptRoot
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $acceptanceRoot '..\..\..'))
$contract = Get-Content -LiteralPath (Join-Path $acceptanceRoot 'contract.json') -Raw | ConvertFrom-Json

function Assert-Acceptance {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) { throw "Acceptance baseline mismatch: $Message" }
}

function Assert-Equal {
    param(
        [Parameter()][AllowNull()]$Actual,
        [Parameter()][AllowNull()]$Expected,
        [Parameter(Mandatory = $true)][string]$Name
    )

    Assert-Acceptance ($Actual -ceq $Expected) "$Name is '$Actual'; expected '$Expected'."
}

function Get-ManifestLines {
    param([Parameter(Mandatory = $true)][string]$Path)

    return @(Get-Content -LiteralPath $Path | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#', [StringComparison]::Ordinal)
    })
}

function Test-GitBlobManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$DisplayName,
        [Parameter()][string]$ApprovedUpdatesPath
    )

    $entries = Get-ManifestLines $ManifestPath
    $approvedUpdates = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    if (-not [string]::IsNullOrWhiteSpace($ApprovedUpdatesPath))
    {
        foreach ($approvedEntry in Get-ManifestLines $ApprovedUpdatesPath)
        {
            if ($approvedEntry -notmatch '^(?<oid>[0-9a-f]{40})  (?<path>.+)$')
            {
                throw "Malformed approved $DisplayName update line: '$approvedEntry'."
            }
            Assert-Acceptance (-not $approvedUpdates.ContainsKey($Matches.path)) (
                "approved $DisplayName update '$($Matches.path)' is duplicated.")
            $approvedUpdates.Add($Matches.path, $Matches.oid)
        }
    }
    $usedApprovals = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $entries)
    {
        if ($entry -notmatch '^(?<oid>[0-9a-f]{40})  (?<path>.+)$')
        {
            throw "Malformed $DisplayName manifest line: '$entry'."
        }

        $expectedOid = $Matches.oid
        $path = $Matches.path
        $fullPath = Join-Path $repoRoot $path
        Assert-Acceptance (Test-Path -LiteralPath $fullPath -PathType Leaf) "$DisplayName file '$path' is missing."
        $actualOid = (& git -C $repoRoot hash-object --path $path $path).Trim()
        Assert-Acceptance ($LASTEXITCODE -eq 0) "git hash-object failed for '$path'."
        if ($actualOid -cne $expectedOid)
        {
            Assert-Acceptance ($approvedUpdates.ContainsKey($path)) (
                "$DisplayName Git blob for $path is '$actualOid'; expected baseline '$expectedOid' " +
                'and no approved update exists.')
            Assert-Equal $actualOid $approvedUpdates[$path] "approved $DisplayName Git blob for $path"
            [void]$usedApprovals.Add($path)
        }
    }
    Assert-Equal $usedApprovals.Count $approvedUpdates.Count "used approved $DisplayName updates"

    Write-Host (
        "Verified $($entries.Count) frozen $DisplayName files " +
        "with $($usedApprovals.Count) approved post-baseline updates.")
}

function Get-XmlNodeValue {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlNode]$Node,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $attribute = $Node.Attributes[$Name]
    if ($null -ne $attribute) { return [string]$attribute.Value }
    $child = $Node.SelectSingleNode($Name)
    if ($null -ne $child) { return [string]$child.InnerText }
    return ''
}

function Get-ProjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)][xml]$Document,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $nodes = @($Document.SelectNodes("/Project/PropertyGroup/$Name"))
    if ($nodes.Count -eq 0) { return '' }
    return [string]$nodes[-1].InnerText
}

function Get-DependencyReferences {
    $references = [System.Collections.Generic.List[string]]::new()
    $projectFiles = @(& git -C $repoRoot ls-files -- '*.csproj' '*.props' '*.targets' | Sort-Object)
    Assert-Acceptance ($LASTEXITCODE -eq 0) 'git ls-files failed while reading dependency references.'
    foreach ($path in $projectFiles)
    {
        [xml]$xml = Get-Content -LiteralPath (Join-Path $repoRoot $path) -Raw
        foreach ($node in @($xml.SelectNodes('//PackageReference|//ProjectReference')))
        {
            $references.Add(('{0}|{1}|{2}|{3}|{4}|{5}' -f
                $path,
                $node.LocalName,
                (Get-XmlNodeValue $node 'Include'),
                (Get-XmlNodeValue $node 'Version'),
                (Get-XmlNodeValue $node 'PrivateAssets'),
                (Get-XmlNodeValue $node 'GeneratePathProperty')))
        }
    }

    return @($references | Sort-Object)
}

function Get-AnalyzerContract {
    param([Parameter(Mandatory = $true)][string]$AssemblyDirectory)

    $analyzerAssemblyPath = Join-Path $AssemblyDirectory 'SharpProof.Analyzer.dll'
    Assert-Acceptance (Test-Path -LiteralPath $analyzerAssemblyPath -PathType Leaf) (
        "built analyzer '$analyzerAssemblyPath' is missing; build the solution first.")
    foreach ($dependency in @('Microsoft.CodeAnalysis.dll', 'System.Collections.Immutable.dll'))
    {
        $dependencyPath = Join-Path $AssemblyDirectory $dependency
        Assert-Acceptance (Test-Path -LiteralPath $dependencyPath -PathType Leaf) (
            "analyzer dependency '$dependencyPath' is missing.")
        [void][System.Reflection.Assembly]::LoadFrom($dependencyPath)
    }

    $assembly = [System.Reflection.Assembly]::LoadFrom($analyzerAssemblyPath)
    $analyzerType = $assembly.GetType('SharpProof.Analyzer.SharpProofAnalyzer', $true)
    $analyzer = [Activator]::CreateInstance($analyzerType)
    $supported = $analyzerType.GetProperty('SupportedDiagnostics').GetValue($analyzer)
    $diagnostics = @($supported | ForEach-Object {
        [ordered]@{
            id = $_.Id
            title = $_.Title.ToString([Globalization.CultureInfo]::InvariantCulture)
            message = $_.MessageFormat.ToString([Globalization.CultureInfo]::InvariantCulture)
            category = $_.Category
            severity = $_.DefaultSeverity.ToString()
            enabled = $_.IsEnabledByDefault
            description = $_.Description.ToString([Globalization.CultureInfo]::InvariantCulture)
            helpLink = $_.HelpLinkUri
        }
    })

    $bindingFlags = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
    $registryType = $assembly.GetType(
        'SharpProof.Analyzer.Configuration.AnalyzerConfigurationOptionRegistry',
        $true)
    $options = $registryType.GetProperty('All', $bindingFlags).GetValue($null)
    $configuration = @($options | ForEach-Object {
        $optionType = $_.GetType()
        $allowed = $optionType.GetProperty('AllowedValues').GetValue($_)
        $isDefault = $allowed.GetType().GetProperty('IsDefault').GetValue($allowed)
        $allowedValues = [System.Collections.Generic.List[string]]::new()
        if (-not $isDefault)
        {
            foreach ($value in $allowed) { $allowedValues.Add($value) }
        }
        [ordered]@{
            key = $optionType.GetProperty('Key').GetValue($_)
            valueKind = $optionType.GetProperty('ValueKind').GetValue($_).ToString()
            allowedValues = $allowedValues
        }
    })

    return [ordered]@{
        schemaVersion = 1
        diagnostics = $diagnostics
        configuration = $configuration
    }
}

function Test-PackageArtifacts {
    param(
        [Parameter(Mandatory = $true)]$PackageContract,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $resolvedDirectory = if ([System.IO.Path]::IsPathRooted($Directory))
    {
        [System.IO.Path]::GetFullPath($Directory)
    }
    else
    {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Directory))
    }
    Assert-Acceptance (Test-Path -LiteralPath $resolvedDirectory -PathType Container) (
        "package directory '$resolvedDirectory' does not exist.")
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($package in $PackageContract.packages)
    {
        $packagePath = Join-Path $resolvedDirectory "$($package.id).$($PackageContract.version).nupkg"
        Assert-Acceptance (Test-Path -LiteralPath $packagePath -PathType Leaf) (
            "package '$packagePath' is missing.")
        $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
        try
        {
            $actual = @($archive.Entries.FullName | Where-Object {
                $_ -ne '_rels/.rels' -and
                $_ -ne '[Content_Types].xml' -and
                -not $_.StartsWith('package/services/metadata/', [StringComparison]::Ordinal)
            } | Sort-Object)
            $expected = @($package.entries | Sort-Object)
            Assert-Equal ($actual -join "`n") ($expected -join "`n") "archive layout for $($package.id)"
        }
        finally
        {
            $archive.Dispose()
        }
    }

    Write-Host "Verified $(@($PackageContract.packages).Count) package archives."
}

Push-Location $repoRoot
try
{
    & git cat-file -e "$($contract.baselineCommit)^{commit}" 2>$null
    Assert-Acceptance ($LASTEXITCODE -eq 0) "baseline commit '$($contract.baselineCommit)' is unavailable."

    $globalJson = Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json
    Assert-Equal $globalJson.sdk.version $contract.sdkVersion 'global.json SDK'
    $actualSdk = (& dotnet --version).Trim()
    Assert-Acceptance ($LASTEXITCODE -eq 0) 'dotnet --version failed.'
    Assert-Equal $actualSdk $contract.sdkVersion 'selected .NET SDK'

    $inventory = @(Import-Csv -LiteralPath (Join-Path $acceptanceRoot 'production-inventory.tsv') -Delimiter "`t")
    Assert-Equal $inventory.Count ([int]$contract.baseline.productionFiles) 'baseline production file count'
    $inventoryPhysical = ($inventory | Measure-Object -Property physicalLines -Sum).Sum
    $inventoryNonblank = ($inventory | Measure-Object -Property nonblankLines -Sum).Sum
    Assert-Equal ([int]$inventoryPhysical) ([int]$contract.baseline.physicalLines) 'baseline physical lines'
    Assert-Equal ([int]$inventoryNonblank) ([int]$contract.baseline.nonblankLines) 'baseline nonblank lines'
    $baselinePaths = @(& git ls-tree -r --name-only $contract.baselineCommit -- @($contract.measurement.roots) |
        Where-Object { $_ -like '*.cs' } | Sort-Object)
    Assert-Acceptance ($LASTEXITCODE -eq 0) 'git ls-tree failed for the production baseline.'
    Assert-Equal (($inventory.path | Sort-Object) -join "`n") ($baselinePaths -join "`n") (
        'baseline production paths')
    foreach ($entry in $inventory)
    {
        $lines = @(& git show "$($contract.baselineCommit):$($entry.path)")
        Assert-Acceptance ($LASTEXITCODE -eq 0) "git show failed for baseline file '$($entry.path)'."
        $nonblank = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
        Assert-Equal $lines.Count ([int]$entry.physicalLines) "baseline physical lines for $($entry.path)"
        Assert-Equal $nonblank ([int]$entry.nonblankLines) "baseline nonblank lines for $($entry.path)"
    }
    Write-Host "Verified pinned production inventory: $($inventory.Count) files, $inventoryPhysical physical, $inventoryNonblank nonblank."

    Test-GitBlobManifest `
        (Join-Path $acceptanceRoot 'frozen-tests.gitblob') `
        'test' `
        (Join-Path $acceptanceRoot 'approved-test-updates.gitblob')
    Test-GitBlobManifest (Join-Path $acceptanceRoot 'corpus-inventory.gitblob') 'corpus'

    $expectedReferences = Get-ManifestLines (Join-Path $acceptanceRoot 'dependency-references.txt')
    $actualReferences = Get-DependencyReferences
    Assert-Equal ($actualReferences -join "`n") ($expectedReferences -join "`n") 'dependency references'
    Write-Host "Verified $($actualReferences.Count) dependency references."

    $readmeExamples = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs\readme-examples') -Directory |
        Where-Object {
            (Test-Path -LiteralPath (Join-Path $_.FullName 'input.cs')) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'output.txt'))
        })
    Assert-Equal $readmeExamples.Count 22 'README example pair count'
    $symbolicExamples = @(Get-Content -LiteralPath (
        Join-Path $repoRoot 'docs\readme-examples\symbolic-examples.json') -Raw | ConvertFrom-Json)
    Assert-Equal $symbolicExamples.Count 5 'documented symbolic command count'
    foreach ($example in $symbolicExamples)
    {
        Assert-Acceptance (-not [string]::IsNullOrWhiteSpace($example.Command)) (
            "symbolic example '$($example.Id)' has no command.")
    }
    $fuzzEntries = @(Get-Content -LiteralPath (
        Join-Path $repoRoot 'Tools\SharpProof.Fuzz.Core\FuzzShapeRegistry.jsonl') |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal $fuzzEntries.Count 70 'fuzz shape count'
    Assert-Equal @($fuzzEntries.Id | Sort-Object -Unique).Count 70 'unique fuzz shape count'
    Write-Host 'Verified corpus topology: 22 README pairs, 5 commands, 70 fuzz shapes, seed 12345.'

    $expectedAnalyzerContract = Get-Content -LiteralPath (
        Join-Path $acceptanceRoot 'analyzer-contract.json') -Raw | ConvertFrom-Json
    $assemblyDirectory = Join-Path $repoRoot "SharpProof.Analyzer\bin\$Configuration\netstandard2.0"
    $actualAnalyzerContract = Get-AnalyzerContract $assemblyDirectory
    $expectedAnalyzerJson = $expectedAnalyzerContract | ConvertTo-Json -Depth 8 -Compress
    $actualAnalyzerJson = $actualAnalyzerContract | ConvertTo-Json -Depth 8 -Compress
    Assert-Equal $actualAnalyzerJson $expectedAnalyzerJson 'analyzer diagnostic/configuration contract'
    Write-Host 'Verified 20 analyzer descriptors and 19 configuration options.'

    $publicAssemblies = @($contract.publicMetadata.assemblies | ForEach-Object {
        Join-Path $repoRoot ([string]$_)
    })
    $actualPublicSignatures = @(& (Join-Path $acceptanceRoot 'Get-PublicMetadataSignatures.ps1') `
        -AssemblyPath $publicAssemblies)
    $expectedPublicSignatures = @(Get-Content -LiteralPath (
        Join-Path $repoRoot $contract.publicMetadata.signatures))
    Assert-Equal ($actualPublicSignatures -join "`n") ($expectedPublicSignatures -join "`n") (
        'public metadata signatures')
    Write-Host "Verified $($actualPublicSignatures.Count) public metadata signature lines."

    $packageContract = Get-Content -LiteralPath (
        Join-Path $acceptanceRoot 'package-contract.json') -Raw | ConvertFrom-Json
    [xml]$release = Get-Content -LiteralPath (Join-Path $repoRoot 'SharpProof.Release.props') -Raw
    [xml]$metadata = Get-Content -LiteralPath (Join-Path $repoRoot 'SharpProof.PackageMetadata.props') -Raw
    $prefix = Get-ProjectPropertyValue $release 'SharpProofVersionPrefix'
    $versionExpression = Get-ProjectPropertyValue $release 'SharpProofPackageVersion'
    $version = $versionExpression.Replace('$(SharpProofVersionPrefix)', $prefix)
    Assert-Equal $version $packageContract.version 'package version'
    Assert-Equal (Get-ProjectPropertyValue $release 'SharpProofPublisher') $packageContract.authors 'package authors'
    Assert-Equal (Get-ProjectPropertyValue $release 'SharpProofProjectUrl') $packageContract.projectUrl (
        'package project URL')
    Assert-Equal (Get-ProjectPropertyValue $metadata 'PackageLicenseExpression') $packageContract.license (
        'package license')
    $packageProjectManifest = Get-Content -LiteralPath (
        Join-Path $repoRoot 'scripts\package-projects.json') -Raw | ConvertFrom-Json
    Assert-Equal (@($packageProjectManifest.projects) -join "`n") (
        @($packageContract.sourceProjects) -join "`n") 'package source projects'
    foreach ($package in $packageContract.packages)
    {
        [xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot $package.project) -Raw
        Assert-Equal (Get-ProjectPropertyValue $project 'PackageId') $package.id (
            "PackageId for $($package.project)")
        Assert-Equal (Get-ProjectPropertyValue $project 'TargetFramework') $package.targetFramework (
            "TargetFramework for $($package.project)")
    }
    Write-Host "Verified release metadata for version $version."
    if (-not [string]::IsNullOrWhiteSpace($PackageDirectory))
    {
        Test-PackageArtifacts $packageContract $PackageDirectory
    }

    $measureScript = Join-Path $repoRoot $contract.measurement.script
    $powershell = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
    $measurementOutput = @(& $powershell -NoProfile -File $measureScript -Json 2>&1)
    $measurementExitCode = $LASTEXITCODE
    $measurement = ($measurementOutput -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-Equal $measurement.baselineCommit $contract.baselineCommit 'measurement baseline commit'
    Assert-Equal ([int]$measurement.baselinePhysicalLines) ([int]$contract.baseline.physicalLines) (
        'measurement physical baseline')
    Assert-Equal ([int]$measurement.baselineNonblankLines) ([int]$contract.baseline.nonblankLines) (
        'measurement nonblank baseline')
    Assert-Equal ([int]$measurement.maximumPhysicalLines) ([int]$contract.target.physicalLines) (
        'measurement physical target')
    Assert-Equal ([int]$measurement.maximumNonblankLines) ([int]$contract.target.nonblankLines) (
        'measurement nonblank target')
    Assert-Equal (@($measurement.roots) -join "`n") (@($contract.measurement.roots) -join "`n") (
        'measurement roots')
    Assert-Equal $measurement.exclusions.generatedFileNamePattern (
        $contract.measurement.exclusions.generatedFileNamePattern) 'generated filename exclusion'
    Assert-Equal $measurement.exclusions.autoGeneratedHeaderPattern (
        $contract.measurement.exclusions.autoGeneratedHeaderPattern) 'auto-generated header exclusion'
    $expectedMeasurementExitCode = if ($measurement.passed) { 0 } else { 1 }
    Assert-Equal $measurementExitCode $expectedMeasurementExitCode 'measurement process exit code'
    Assert-Equal ([int]$measurement.creditedRemovedLines) ([Math]::Min(
        [int]$measurement.removedPhysicalLines,
        [int]$measurement.removedNonblankLines)) 'credited line reduction'
    if ($RequireLineTarget)
    {
        Assert-Acceptance ([bool]$measurement.passed) (
            "line target failed: $($measurement.physicalLines)/$($measurement.maximumPhysicalLines) physical, " +
            "$($measurement.nonblankLines)/$($measurement.maximumNonblankLines) nonblank.")
    }
    elseif (-not $measurement.passed)
    {
        Write-Host (
            "Line target pending: $($measurement.physicalLines)/$($measurement.maximumPhysicalLines) physical, " +
            "$($measurement.nonblankLines)/$($measurement.maximumNonblankLines) nonblank; " +
            "$($measurement.creditedRemovedLines)/$($contract.target.creditedReduction) credited.")
    }
    else
    {
        Write-Host "Line target passed with $($measurement.creditedRemovedLines) credited removals."
    }

    Write-Host 'SharpProof acceptance baseline verification passed.'
}
finally
{
    Pop-Location
}
