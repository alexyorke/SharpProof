[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateSet('Required')]
    [string]$ExpectedSmt = 'Required',

    [Parameter()]
    [string]$PackageSource,

    [Parameter()]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$ConsumerSdkVersion,

    [Parameter()]
    [switch]$FrameworkConsumersOnly,

    [Parameter()]
    [switch]$ValidatePackageSourceOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-SharpProofSymbolPackages.ps1')

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
        'SharpProof.Verifier'
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

    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $repositoryCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or
        $repositoryCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve the package-source checkout commit.'
    }
    foreach ($packageId in $expectedIds) {
        $main = @($identities | Where-Object Id -eq $packageId)[0]
        $symbols = @($symbolIdentities | Where-Object Id -eq $packageId)[0]
        Test-SharpProofSymbolPackagePair `
            -PackagePath $main.Path `
            -SymbolPackagePath $symbols.Path `
            -PackageId $packageId `
            -PackageVersion $versions[0] `
            -RepositoryCommit $repositoryCommit
    }
    return $resolved
}

function Get-SharpProofPortablePackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    $package = Get-ChildItem -LiteralPath $Source -File -Filter '*.nupkg' |
        ForEach-Object { Get-PackageIdentity -Path $_.FullName } |
        Where-Object { $_.Id -eq 'SharpProof' }
    if (@($package).Count -ne 1) {
        throw "The package source must contain exactly one SharpProof package."
    }
    return [string]$package.Version
}

function New-FrameworkPackageSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $configuredPackages = [Environment]::GetEnvironmentVariable(
        'NUGET_PACKAGES',
        [EnvironmentVariableTarget]::Process)
    $globalPackages = if ([string]::IsNullOrWhiteSpace(
            $configuredPackages)) {
        [IO.Path]::Combine(
            [Environment]::GetFolderPath(
                [Environment+SpecialFolder]::UserProfile),
            '.nuget',
            'packages')
    }
    else {
        [IO.Path]::GetFullPath($configuredPackages)
    }

    $frameworkSource = Join-Path $Root 'framework-packages'
    [IO.Directory]::CreateDirectory($frameworkSource) | Out-Null
    $toolchain = Get-Content -LiteralPath (Join-Path `
        $RepositoryRoot 'eng/container/toolchain.json') -Raw |
        ConvertFrom-Json
    $testRuntimeVersion = [string]$toolchain.dotnet.testRuntimeVersion
    if ($testRuntimeVersion -notmatch '^8\.0\.[0-9]+$') {
        throw "The container test runtime version is invalid: '$testRuntimeVersion'."
    }
    $frameworkPackages = @(
        @('netstandard.library', '2.0.3'),
        @('microsoft.netcore.platforms', '1.1.0'),
        @('microsoft.netframework.referenceassemblies', '1.0.3'),
        @('microsoft.netframework.referenceassemblies.net472', '1.0.3'),
        @('microsoft.netcore.app.ref', $testRuntimeVersion),
        @('microsoft.aspnetcore.app.ref', $testRuntimeVersion)
    )
    foreach ($package in $frameworkPackages) {
        $fileName = "$($package[0]).$($package[1]).nupkg"
        $source = [IO.Path]::Combine(
            $globalPackages,
            [string]$package[0],
            [string]$package[1],
            $fileName)
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw (
                'The offline framework package is missing from the ' +
                "restored global package cache: $source")
        }
        [IO.File]::Copy(
            $source,
            (Join-Path $frameworkSource $fileName),
            $true)
    }

    $unexpectedPackages = @(
        Get-ChildItem -LiteralPath $frameworkSource -File -Filter '*.nupkg' |
            ForEach-Object { Get-PackageIdentity -Path $_.FullName } |
            Where-Object {
                $_.Id.StartsWith(
                    'SharpProof',
                    [StringComparison]::OrdinalIgnoreCase)
            }
    )
    if ($unexpectedPackages.Count -ne 0) {
        throw (
            'The framework-only package source unexpectedly contains ' +
            'SharpProof packages.')
    }
    return $frameworkSource
}

function Invoke-ConsumerDotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    Push-Location $WorkingDirectory
    $capturePath = $null
    try {
        $captureOutput = $Arguments[0] -eq '--version' -or
            $Arguments[0] -eq 'msbuild'
        $output = & dotnet @Arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
        if (-not [string]::IsNullOrEmpty($output)) {
            Write-Host $output.TrimEnd()
        }
        if ($exitCode -ne 0) {
            throw (
                "dotnet $($Arguments -join ' ') failed with exit code " +
                "$exitCode.")
        }
        return $output.Trim()
    }
    finally {
        if ($null -ne $capturePath -and [IO.File]::Exists($capturePath)) {
            Remove-Item -LiteralPath $capturePath -Force
        }
        Pop-Location
    }
}

function Assert-SharpProofAnalyzerItems {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output,

        [Parameter(Mandatory = $true)]
        [string]$Framework
    )

    try {
        $evaluation = $Output | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw (
            "The $Framework package consumer returned malformed evaluated " +
            "MSBuild analyzer items: $($_.Exception.Message)")
    }

    $analyzerItems = @($evaluation.Items.Analyzer)
    $sharpProofItems = @(
        $analyzerItems |
            Where-Object {
                ([string]$_.Identity) -match
                    '[/\\]SharpProof\.[^/\\]+\.dll$'
            }
    )
    $entryPoints = @(
        $sharpProofItems |
            Where-Object {
                $_.SharpProofAnalyzerRole -eq 'EntryPoint'
            }
    )
    $entryPointNames = @(
        $entryPoints |
            ForEach-Object {
                (([string]$_.Identity) -replace '\\', '/') -split '/' |
                    Select-Object -Last 1
            }
    )
    $generators = @(
        $sharpProofItems |
            Where-Object {
                $_.SharpProofAnalyzerRole -eq 'Generator'
            }
    )
    $generatorNames = @(
        $generators |
            ForEach-Object {
                (([string]$_.Identity) -replace '\\', '/') -split '/' |
                    Select-Object -Last 1
            }
    )
    $legacyEntryPoints = @(
        $sharpProofItems |
            Where-Object {
                (([string]$_.Identity) -replace '\\', '/') -match
                    '/SharpProof\.PortableAnalyzer\.dll$'
            }
    )

    if ($entryPointNames.Count -ne 1 -or
        $entryPointNames[0] -ne 'SharpProof.Analyzer.dll' -or
        $generatorNames.Count -ne 1 -or
        $generatorNames[0] -ne 'SharpProof.ContractForGenerator.dll' -or
        $legacyEntryPoints.Count -ne 0) {
        throw (
            "The $Framework package consumer must load exactly one SharpProof " +
            'analyzer and one ContractFor generator, with no portable monolith.')
    }
}

function Test-SharpProofFrameworkConsumers {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter()]
        [string]$SdkVersion
    )

    $parent = Join-Path `
        ([IO.Path]::GetTempPath()) `
        'SharpProof.PackageConsumers'
    $root = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($root) | Out-Null
    try {
        $encoding = [Text.UTF8Encoding]::new($false)
        $globalJson = Join-Path $root 'global.json'
        if ([string]::IsNullOrWhiteSpace($SdkVersion)) {
            [IO.File]::Copy(
                (Join-Path $RepositoryRoot 'global.json'),
                $globalJson)
        }
        else {
            $json = [ordered]@{
                sdk = [ordered]@{
                    version = $SdkVersion
                    rollForward = 'disable'
                }
            } | ConvertTo-Json -Depth 3
            [IO.File]::WriteAllText(
                $globalJson,
                $json + "`n",
                $encoding)
        }

        $frameworkSource = New-FrameworkPackageSource `
            -Root $root `
            -RepositoryRoot $RepositoryRoot
        $escapedSource = [Security.SecurityElement]::Escape($Source)
        $escapedFrameworkSource =
            [Security.SecurityElement]::Escape($frameworkSource)
        $nugetConfig = Join-Path $root 'NuGet.Config'
        $nugetConfigLines = @(
            '<?xml version="1.0" encoding="utf-8"?>'
            '<configuration>'
            '  <packageSources>'
            '    <clear />'
            "    <add key=`"SharpProofLocal`" value=`"$escapedSource`" />"
            "    <add key=`"FrameworkOffline`" value=`"$escapedFrameworkSource`" />"
            '  </packageSources>'
            '  <packageSourceMapping>'
            '    <packageSource key="SharpProofLocal">'
            '      <package pattern="SharpProof*" />'
            '    </packageSource>'
            '    <packageSource key="FrameworkOffline">'
            '      <package pattern="NETStandard.Library" />'
            '      <package pattern="Microsoft.NETCore.Platforms" />'
            '      <package pattern="Microsoft.NETCore.App.Ref" />'
            '      <package pattern="Microsoft.AspNetCore.App.Ref" />'
            '      <package pattern="Microsoft.NETFramework.ReferenceAssemblies*" />'
            '    </packageSource>'
            '  </packageSourceMapping>'
            '</configuration>'
        )
        [IO.File]::WriteAllText(
            $nugetConfig,
            ($nugetConfigLines -join "`n") + "`n",
            $encoding)

        $actualSdk = Invoke-ConsumerDotNet `
            -WorkingDirectory $root `
            -Arguments @('--version') `
            -RepositoryRoot $RepositoryRoot
        if (-not [string]::IsNullOrWhiteSpace($SdkVersion) -and
            $actualSdk.Trim() -ne $SdkVersion) {
            throw (
                "Expected actual consumer SDK '$SdkVersion', but dotnet " +
                "selected '$($actualSdk.Trim())'.")
        }

        $frameworks = @('netstandard2.0', 'net8.0', 'net472')

        $escapedVersion = [Security.SecurityElement]::Escape($Version)
        foreach ($framework in $frameworks) {
            $consumer = Join-Path $root $framework
            [IO.Directory]::CreateDirectory($consumer) | Out-Null
            $projectLines = @(
                '<Project Sdk="Microsoft.NET.Sdk">'
                '  <PropertyGroup>'
                "    <TargetFramework>$framework</TargetFramework>"
                '    <LangVersion>12.0</LangVersion>'
                '    <SharpProofProfile>advisory</SharpProofProfile>'
                '    <SharpProofFeatures>all</SharpProofFeatures>'
                '    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
                '    <WarningsAsErrors>AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>'
                '    <NuGetAudit>false</NuGetAudit>'
                '    <RestoreIgnoreFailedSources>false</RestoreIgnoreFailedSources>'
                '  </PropertyGroup>'
                '  <ItemGroup>'
                '    <PackageReference Include="SharpProof"'
                "                      Version=`"$escapedVersion`" />"
                if ($framework -eq 'net472') {
                    '    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="1.0.3" PrivateAssets="all" />'
                }
                '  </ItemGroup>'
                '</Project>'
            )
            [IO.File]::WriteAllText(
                (Join-Path $consumer 'Consumer.csproj'),
                ($projectLines -join "`n") + "`n",
                $encoding)
            $sourceLines = @(
                'using SharpProof.Attributes;'
                ''
                'public static class Subject {'
                '    [ZeroAllocations]'
                '    public static int Identity(int value) => value;'
                '}'
            )
            [IO.File]::WriteAllText(
                (Join-Path $consumer 'Subject.cs'),
                ($sourceLines -join "`n") + "`n",
                $encoding)

            $cache = Join-Path $root 'packages'
            Invoke-ConsumerDotNet `
                -WorkingDirectory $consumer `
                -Arguments @(
                    'restore',
                    'Consumer.csproj',
                    '--configfile',
                    $nugetConfig,
                    '--packages',
                    $cache,
                    '--nologo') `
                -RepositoryRoot $RepositoryRoot | Out-Null
            $analyzers = Invoke-ConsumerDotNet `
                -WorkingDirectory $consumer `
                -Arguments @(
                    'msbuild',
                    'Consumer.csproj',
                    '-getItem:Analyzer',
                    '--nologo') `
                -RepositoryRoot $RepositoryRoot
            Assert-SharpProofAnalyzerItems `
                -Output $analyzers `
                -Framework $framework
            Invoke-ConsumerDotNet `
                -WorkingDirectory $consumer `
                -Arguments @(
                    'build',
                    'Consumer.csproj',
                    '--configuration',
                    $Configuration,
                    '--no-restore',
                    '--nologo') `
                -RepositoryRoot $RepositoryRoot | Out-Null
        }
    }
    finally {
        $expectedParent = [IO.Path]::GetFullPath($parent)
        $resolvedRoot = [IO.Path]::GetFullPath($root)
        if (-not $resolvedRoot.StartsWith(
                $expectedParent + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected consumer root '$resolvedRoot'."
        }
        if ([IO.Directory]::Exists($resolvedRoot)) {
            [IO.Directory]::Delete($resolvedRoot, $true)
        }
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repositoryRoot 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
$isSupportedWorkerHost = $IsLinux -and
    $env:SHARPPROOF_CONTAINER -ceq '1' -and
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
        [System.Runtime.InteropServices.Architecture]::X64 -and
    [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq
        [System.Runtime.InteropServices.Architecture]::X64
if (-not $isSupportedWorkerHost) {
    throw (
        'SharpProof package consumers must run in the canonical Linux ' +
        'amd64 container.')
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
if ($null -ne $resolvedPackageSource) {
    $packageVersion = Get-SharpProofPortablePackageVersion `
        -Source $resolvedPackageSource
    Test-SharpProofFrameworkConsumers `
        -Source $resolvedPackageSource `
        -Version $packageVersion `
        -RepositoryRoot $repositoryRoot `
        -SdkVersion $ConsumerSdkVersion
}
elseif ($FrameworkConsumersOnly) {
    throw 'FrameworkConsumersOnly requires PackageSource or SHARPPROOF_PACKAGE_SOURCE.'
}
if ($FrameworkConsumersOnly) {
    Write-Host (
        "SharpProof package-backed framework consumers passed with actual " +
        "SDK '$ConsumerSdkVersion'.")
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
    $packageTestArguments = @(
        'test',
        $testProject,
        '--configuration',
        $Configuration,
        '--logger',
        'console;verbosity=minimal')
    if ($null -ne $resolvedPackageSource) {
        # Framework compatibility is exercised above. Re-run only the exact
        # package graph, analyzer discovery, and real verifier proof here;
        # the complete package suite belongs to the ordinary test/acceptance
        # lane and must not be duplicated for every consumer job.
        $focusedPackageFilter =
            'FullyQualifiedName=SharpProof.Package.Test.PackageLayoutSmokeTests.PackageGraphAndLayoutsAreExact|' +
            'FullyQualifiedName=SharpProof.Package.Test.PackageLayoutSmokeTests.StrictAnalyzerSetDiscoversEachEntrypointOnce|' +
            'FullyQualifiedName=SharpProof.Package.Test.PackageLayoutSmokeTests.VerifierPackageTransitivelySuppliesPortableProduct'
        $packageTestArguments += @('--filter', $focusedPackageFilter)
    }
    & dotnet @packageTestArguments
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
