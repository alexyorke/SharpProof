[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResultsDirectory = 'artifacts/coverage',

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1200,

    [Parameter()]
    [string]$TestFilter = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryPrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$testProjectParallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$resolvedResultsDirectory = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $ResultsDirectory `
    -ParameterName 'ResultsDirectory'
if (Test-Path -LiteralPath $resolvedResultsDirectory -PathType Container) {
    $existingReport = Get-ChildItem `
        -LiteralPath $resolvedResultsDirectory `
        -Recurse `
        -Filter '*.cobertura.xml' `
        -File `
        -ErrorAction Stop |
        Select-Object -First 1
    if ($null -ne $existingReport) {
        throw (
            'ResultsDirectory already contains coverage evidence: ' +
            $existingReport.FullName)
    }
}

New-Item `
    -ItemType Directory `
    -Force `
    -Path $resolvedResultsDirectory |
    Out-Null

# Establish the denominator and module/source authority before any collector
# can instrument or merge binaries. The validator independently recomputes the
# same portable-PDB universe; this file is evidence, not the source of truth.
$coverageAuthorityPath = Join-Path `
    $resolvedResultsDirectory `
    'coverage-authority.json'
& (Join-Path $PSScriptRoot 'Get-SharpProofCoverageAuthority.ps1') `
    -RepositoryRoot $repositoryRoot `
    -BaselinePath (Join-Path $repositoryRoot 'eng/coverage/baseline.json') `
    -Configuration Release `
    -OutputPath $coverageAuthorityPath
if ($LASTEXITCODE -ne 0) {
    throw 'Coverage authority derivation failed before collection.'
}

$dotnetWrapper = Join-Path `
    $repositoryRoot `
    'scripts\Invoke-SharpProofDotnet.ps1'
$managedSettings = Join-Path `
    $repositoryRoot `
    'eng\coverage\SharpProof.Managed.runsettings'

# The Microsoft collector is used instead of Coverlet. Static managed
# instrumentation rewrites assemblies on disk, so concurrent shards receive
# private managed binaries while immutable dependencies remain hard-linked.
# Child collection remains disabled so packaged consumer builds cannot merge a
# second build of the same assembly and PDB into the project coverage universe.
$broadFilter = 'TestCategory!=Performance&TestCategory!=Coverage'
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    $broadFilter = "($broadFilter)&($TestFilter)"
}
if ([string]::IsNullOrWhiteSpace($TestFilter)) {
    $semanticResults = Join-Path $resolvedResultsDirectory 'semantic'
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofSemanticTests.ps1') `
        -Configuration Release `
        -NoBuild `
        -TimeoutSeconds $TimeoutSeconds `
        -TestFilter $broadFilter `
        -CoverageSettings $managedSettings `
        -CoverageResultsDirectory $semanticResults
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Sharded semantic coverage failed with exit code ' +
            "$LASTEXITCODE.")
    }

    $packageResults = Join-Path $resolvedResultsDirectory 'package'
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
        -Configuration Release `
        -NoBuild `
        -TimeoutSeconds $TimeoutSeconds `
        -CoverageSettings $managedSettings `
        -CoverageResultsDirectory $packageResults
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Sharded package coverage failed with exit code ' +
            "$LASTEXITCODE.")
    }
}
else {
    # Preserve a single-invocation targeted probe for arbitrary user filters.
    & $dotnetWrapper `
        -TimeoutSeconds $TimeoutSeconds `
        test (Join-Path $repositoryRoot 'SharpProof.Dev.Tests.slnf') `
        -c Release `
        --no-build `
        "/m:$testProjectParallelism" `
        --filter $broadFilter `
        --settings $managedSettings `
        --collect 'Code Coverage;Format=Cobertura' `
        --results-directory $resolvedResultsDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage collection failed with exit code $LASTEXITCODE."
    }
}

# SharpProof.Attributes is a compiler-input payload whose exact bytes are
# authenticated by compiler evidence. Linux managed coverage instruments that
# payload on disk, so the broad Worker pass must not rewrite it. Collect its
# own behavioral coverage in an isolated testhost where no compiler manifest
# consumes the instrumented assembly.
$attributesSettings = Join-Path `
    $repositoryRoot `
    'eng\coverage\SharpProof.Attributes.runsettings'
& $dotnetWrapper `
    -TimeoutSeconds $TimeoutSeconds `
    test (Join-Path `
        $repositoryRoot `
        'SharpProof.Attributes.Test\SharpProof.Attributes.Test.csproj') `
    -c Release `
    --no-build `
    --settings $attributesSettings `
    --collect 'Code Coverage;Format=Cobertura' `
    --results-directory $resolvedResultsDirectory
if ($LASTEXITCODE -ne 0) {
    throw (
        'Isolated Attributes coverage failed with exit code ' +
        "$LASTEXITCODE.")
}

# The broad pass deliberately excludes wall-clock assertions. Exercise the
# complete performance protocol in a separate structural-evidence test whose
# settings instrument only SharpProof.Gates and never its child processes or
# product payloads. The ordinary uninstrumented Performance test remains the
# authoritative threshold gate.
$gateSettings = Join-Path `
    $repositoryRoot `
    'eng\coverage\SharpProof.Gates.runsettings'
& $dotnetWrapper `
    -TimeoutSeconds $TimeoutSeconds `
    test (Join-Path `
        $repositoryRoot `
        'SharpProof.Gates.Test\SharpProof.Gates.Test.csproj') `
    -c Release `
    --no-build `
    --filter 'TestCategory=Coverage' `
    --settings $gateSettings `
    --collect 'Code Coverage;Format=Cobertura' `
    --results-directory $resolvedResultsDirectory
if ($LASTEXITCODE -ne 0) {
    throw (
        'Isolated Gates coverage failed with exit code ' +
        "$LASTEXITCODE.")
}

# The container entrypoint builds each task in a disposable clone while the
# coverage artifacts are persisted through the shared artifacts directory.
# The Microsoft collector writes absolute clone paths into Cobertura. Rewrite
# only paths proven to be inside this checkout to repository-relative names so
# the persisted report remains valid after the disposable clone is removed and
# the policy gate evaluates the same canonical source identity everywhere.
$coverageReports = @(
    Get-ChildItem `
        -LiteralPath $resolvedResultsDirectory `
        -Recurse `
        -Filter '*.cobertura.xml' `
        -File `
        -ErrorAction Stop)
$coverageAuthority = Get-Content `
    -LiteralPath $coverageAuthorityPath `
    -Raw | ConvertFrom-Json
$coverageModuleHashes = @(
    $coverageAuthority.modules |
        ForEach-Object { [string]$_.assemblySha256 } |
        Sort-Object)
foreach ($coverageReport in $coverageReports) {
    [xml]$coverageDocument = Get-Content `
        -LiteralPath $coverageReport.FullName `
        -Raw
    $changed = $false
    foreach ($class in $coverageDocument.SelectNodes('//class[@filename]')) {
        $fileName = [string]$class.filename
        if (-not [IO.Path]::IsPathRooted($fileName)) {
            continue
        }
        $fullPath = [IO.Path]::GetFullPath($fileName)
        if (-not $fullPath.StartsWith(
                $repositoryPrefix,
                [StringComparison]::Ordinal)) {
            continue
        }
        $class.SetAttribute(
            'filename',
            $fullPath.Substring($repositoryPrefix.Length).Replace('\', '/'))
        $changed = $true
    }
    $authorityNodes = @(
        $coverageDocument.SelectNodes('/coverage/sharpProofAuthority'))
    if ($authorityNodes.Count -ne 0) {
        throw (
            "Coverage report already contains authority metadata: " +
            $coverageReport.FullName)
    }
    $authorityNode = $coverageDocument.CreateElement('sharpProofAuthority')
    $authorityNode.SetAttribute('schemaVersion', '1')
    $authorityNode.SetAttribute('commit', [string]$coverageAuthority.commit)
    $authorityNode.SetAttribute(
        'universeSha256',
        [string]$coverageAuthority.universeSha256)
    $authorityNode.SetAttribute(
        'modules',
        ($coverageModuleHashes -join ','))
    [void]$coverageDocument.DocumentElement.AppendChild($authorityNode)
    $changed = $true
    if ($changed) {
        $writerSettings = [Xml.XmlWriterSettings]::new()
        $writerSettings.Encoding = [Text.UTF8Encoding]::new($false)
        $writerSettings.Indent = $true
        $writerSettings.NewLineChars = "`n"
        $writerSettings.NewLineHandling =
            [Xml.NewLineHandling]::Replace
        $writer = [Xml.XmlWriter]::Create(
            $coverageReport.FullName,
            $writerSettings)
        try {
            $coverageDocument.Save($writer)
        }
        finally {
            $writer.Dispose()
        }
    }
}
