[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateSet('Required', 'Graceful')]
    [string]$ExpectedSmt,

    [Parameter()]
    [string]$PackageSource,

    [Parameter()]
    [switch]$ValidatePackageSourceOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $nuspecEntries = @(
            $archive.Entries |
                Where-Object {
                    $_.FullName.EndsWith(
                        '.nuspec',
                        [StringComparison]::OrdinalIgnoreCase)
                }
        )
        if ($nuspecEntries.Count -ne 1) {
            throw "Package '$Path' must contain exactly one nuspec."
        }
        $reader = [IO.StreamReader]::new($nuspecEntries[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        $namespaces = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespaces.AddNamespace(
            'n',
            $nuspec.DocumentElement.NamespaceURI)
        $metadata = $nuspec.SelectSingleNode(
            '/n:package/n:metadata',
            $namespaces)
        if ($null -eq $metadata) {
            throw "Package '$Path' has no nuspec metadata."
        }
        $id = $metadata.SelectSingleNode('n:id', $namespaces)
        $version = $metadata.SelectSingleNode('n:version', $namespaces)
        if ($null -eq $id -or $null -eq $version) {
            throw "Package '$Path' has an incomplete nuspec identity."
        }
        return [pscustomobject]@{
            Id = $id.InnerText
            Version = $version.InnerText
            Path = $Path
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Resolve-SharpProofPackageSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "SharpProof package source is not a directory: $resolved"
    }
    $packageFiles = @(
        Get-ChildItem -LiteralPath $resolved -File -Filter '*.nupkg'
    )
    if ($packageFiles.Count -ne 3) {
        throw "SharpProof package source must contain exactly three nupkg files; found $($packageFiles.Count)."
    }
    $symbolPackageFiles = @(
        Get-ChildItem -LiteralPath $resolved -File -Filter '*.snupkg'
    )
    if ($symbolPackageFiles.Count -ne 3) {
        throw "SharpProof package source must contain exactly three snupkg files; found $($symbolPackageFiles.Count)."
    }
    $identities = @(
        $packageFiles |
            ForEach-Object { Get-PackageIdentity -Path $_.FullName }
    )
    $symbolIdentities = @(
        $symbolPackageFiles |
            ForEach-Object { Get-PackageIdentity -Path $_.FullName }
    )
    $expectedIds = @(
        'SharpProof',
        'SharpProof.Attributes',
        'SharpProof.Verifier.Win-x64'
    ) | Sort-Object
    $actualIds = @($identities.Id | Sort-Object)
    if (($actualIds -join '|') -ne ($expectedIds -join '|')) {
        throw "SharpProof package source IDs must be exactly '$($expectedIds -join ', ')'; found '$($actualIds -join ', ')'."
    }
    $actualSymbolIds = @($symbolIdentities.Id | Sort-Object)
    if (($actualSymbolIds -join '|') -ne ($expectedIds -join '|')) {
        throw "SharpProof symbol package source IDs must be exactly '$($expectedIds -join ', ')'; found '$($actualSymbolIds -join ', ')'."
    }
    $versions = @(
        (@($identities.Version) +
            @($symbolIdentities.Version)) |
            Sort-Object -Unique
    )
    if ($versions.Count -ne 1) {
        throw "SharpProof package and symbol package versions must match; found '$($versions -join ', ')'."
    }
    return $resolved
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repositoryRoot 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
$isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$isSupportedWorkerHost = $isWindowsHost -and
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
        [System.Runtime.InteropServices.Architecture]::X64 -and
    [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq
        [System.Runtime.InteropServices.Architecture]::X64
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
    throw "ExpectedSmt='$ExpectedSmt' does not match this host's '$expectedHostPolicy' verifier policy."
}

if ([string]::IsNullOrWhiteSpace($PackageSource)) {
    $PackageSource = [Environment]::GetEnvironmentVariable(
        'SHARPPROOF_PACKAGE_SOURCE',
        [EnvironmentVariableTarget]::Process)
}
$resolvedPackageSource = if ([string]::IsNullOrWhiteSpace($PackageSource)) {
    $null
}
else {
    Resolve-SharpProofPackageSource -Path $PackageSource
}
if ($ValidatePackageSourceOnly) {
    if ($null -eq $resolvedPackageSource) {
        throw 'ValidatePackageSourceOnly requires PackageSource or SHARPPROOF_PACKAGE_SOURCE.'
    }
    Write-Host "Validated exact SharpProof package source: $resolvedPackageSource"
    return
}
$previousPackageSource = [Environment]::GetEnvironmentVariable(
    'SHARPPROOF_PACKAGE_SOURCE',
    [EnvironmentVariableTarget]::Process)
if ($null -ne $resolvedPackageSource) {
    [Environment]::SetEnvironmentVariable(
        'SHARPPROOF_PACKAGE_SOURCE',
        $resolvedPackageSource,
        [EnvironmentVariableTarget]::Process)
}

Push-Location $repositoryRoot
try {
    if ($isWindowsHost) {
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1') `
            -MemoryLimitMb 6144 `
            -TimeoutSeconds 900 `
            test $testProject `
            --configuration $Configuration `
            --logger 'console;verbosity=minimal'
    }
    else {
        & dotnet test $testProject `
            --configuration $Configuration `
            --logger 'console;verbosity=minimal'
    }
    if ($LASTEXITCODE -ne 0) {
        throw "SharpProof package consumer tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
    if ($null -ne $resolvedPackageSource) {
        [Environment]::SetEnvironmentVariable(
            'SHARPPROOF_PACKAGE_SOURCE',
            $previousPackageSource,
            [EnvironmentVariableTarget]::Process)
    }
}

$workerScope = if ($isSupportedWorkerHost) {
    'analyzer and out-of-process worker'
}
else {
    'analyzer (packaged worker is not supported on this host)'
}
Write-Host "SharpProof packaged $workerScope consumer passed ($ExpectedSmt host policy)."
