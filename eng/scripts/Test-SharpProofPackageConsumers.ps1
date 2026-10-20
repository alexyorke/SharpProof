[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

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
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function Resolve-SharpProofPackageSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "SharpProof package source is not a directory: $resolved"
    }
    $packageSourceFiles = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(Get-ChildItem -LiteralPath $resolved -File))
    $packageFiles = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            $packageSourceFiles | Where-Object Extension -EQ '.nupkg'
        ))
    if ($packageFiles.Length -ne 3) {
        throw "SharpProof package source must contain exactly three nupkg files; found $($packageFiles.Length)."
    }
    $symbolPackageFiles = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            $packageSourceFiles | Where-Object Extension -EQ '.snupkg'
        ))
    if ($symbolPackageFiles.Length -ne 2) {
        throw "SharpProof package source must contain exactly two snupkg files (Attributes and Verifier; the facade has no executable payload); found $($symbolPackageFiles.Length)."
    }
    $identityFileBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $identityFileBuilder.AddRange($packageFiles)
    $identityFileBuilder.AddRange($symbolPackageFiles)
    $identitySet = Get-SharpProofPackageIdentitySet `
        -Files ([object[]]@($identityFileBuilder.ToImmutable()))
    $identities = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@(
                $identitySet.Identities |
                    Where-Object { $_.File.Extension -eq '.nupkg' } |
                    ForEach-Object { $_.Identity }
            )))
    $symbolIdentities = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@(
                $identitySet.Identities |
                    Where-Object { $_.File.Extension -eq '.snupkg' } |
                    ForEach-Object { $_.Identity }
            )))
    $expectedIds = $SharpProofSymbolPackageIds
    $versions = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($identitySet.Version))

    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $repositoryCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or
        $repositoryCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve the package-source checkout commit.'
    }
    $facade = @($identities | Where-Object { $_.Id -eq 'SharpProof' })[0]
    $null = Test-SharpProofFacadePackage -PackagePath $facade.Path `
        -PackageVersion $versions[0] -RepositoryCommit $repositoryCommit
    foreach ($packageId in $expectedIds) {
        $main = @($identities | Where-Object { $_.Id -eq $packageId })[0]
        $symbols = @($symbolIdentities | Where-Object { $_.Id -eq $packageId })[0]
        $null = Test-SharpProofSymbolPackagePair `
            -PackagePath $main.Path `
            -SymbolPackagePath $symbols.Path `
            -PackageId $packageId `
            -PackageVersion $versions[0] `
            -RepositoryCommit $repositoryCommit
    }
    & (Join-Path $PSScriptRoot 'Test-SharpProofThirdPartyInventory.ps1') `
        -PackageSource $resolved
    return ([ordered]@{
            Path    = [string]$resolved
            Version = [string]$versions[0]
        }).AsReadOnly()
}

function New-FrameworkPackageSource {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', 'RepositoryRoot',
        Justification = 'Compatibility parameter; targeting packs are selected by the consumer SDK.')]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter()]
        [scriptblock]$DownloadPackageArchive
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
    # Targeting packs are selected by the consumer SDK, independently of the
    # runtime used to execute repository tests. Query the exact SDK selected
    # by this fixture's global.json before constructing its isolated feed.
    $frameworkProbe = Join-Path $Root 'FrameworkProbe.proj'
    [IO.File]::WriteAllText($frameworkProbe,
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>',
        [Text.UTF8Encoding]::new($false))
    $frameworkEvaluation = (Invoke-ConsumerDotNet `
            -WorkingDirectory $Root `
            -Arguments @('msbuild', $frameworkProbe, '-nologo',
            '-getItem:KnownFrameworkReference') -Quiet) | ConvertFrom-Json
    $frameworkReferences = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($frameworkEvaluation.Items.KnownFrameworkReference |
                    Where-Object { $_.TargetFramework -eq 'net8.0' })))
    $targetingPackBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    [void]$targetingPackBuilder.AddRange([string[]]@(
            'Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref'))
    if ($IsWindows) { [void]$targetingPackBuilder.Add('Microsoft.WindowsDesktop.App.Ref') }
    $targetingPacks = $targetingPackBuilder.ToImmutable()
    $frameworkPackageBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    [void]$frameworkPackageBuilder.AddRange([object[]]@(
            ([ordered]@{
                Id      = 'netstandard.library'
                Version = '2.0.3'
                Pattern = 'NETStandard.Library'
            }).AsReadOnly()
            ([ordered]@{
                Id      = 'microsoft.netcore.platforms'
                Version = '1.1.0'
                Pattern = 'Microsoft.NETCore.Platforms'
            }).AsReadOnly()
            ([ordered]@{
                Id      = 'microsoft.netframework.referenceassemblies'
                Version = '1.0.3'
                Pattern = 'Microsoft.NETFramework.ReferenceAssemblies*'
            }).AsReadOnly()
            ([ordered]@{
                Id      = 'microsoft.netframework.referenceassemblies.net472'
                Version = '1.0.3'
                Pattern = 'Microsoft.NETFramework.ReferenceAssemblies*'
            }).AsReadOnly()
        ))
    foreach ($targetingPack in $targetingPacks) {
        $packVersions = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($frameworkReferences |
                        Where-Object { $_.TargetingPackName -eq $targetingPack } |
                        Select-Object -ExpandProperty TargetingPackVersion -Unique)))
        if ($packVersions.Count -ne 1 -or
            [string]$packVersions[0] -notmatch '^8\.0\.[0-9]+$') {
            throw "The selected consumer SDK did not declare one net8.0 targeting-pack version for '$targetingPack'."
        }
        Write-Host "Consumer SDK requires $targetingPack $($packVersions[0])."
        [void]$frameworkPackageBuilder.Add(([ordered]@{
                    Id      = $targetingPack.ToLowerInvariant()
                    Version = [string]$packVersions[0]
                    Pattern = $targetingPack
                }).AsReadOnly())
    }
    $frameworkPackages = $frameworkPackageBuilder.ToImmutable()
    foreach ($package in $frameworkPackages) {
        $fileName = "$($package.Id).$($package.Version).nupkg"
        $source = [IO.Path]::Combine(
            $globalPackages,
            [string]$package.Id,
            [string]$package.Version,
            $fileName)
        $downloadRoot = $null
        $packagePath = $source
        try {
            if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
                $downloadRoot = Join-Path `
                ([IO.Path]::GetTempPath()) `
                ('sharpproof-framework-package-' +
                    [Guid]::NewGuid().ToString('N'))
                [IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
                $packagePath = Join-Path $downloadRoot $fileName
                $normalizedId = $package.Id.ToLowerInvariant()
                $normalizedVersion = $package.Version.ToLowerInvariant()
                $uri = (
                    'https://api.nuget.org/v3-flatcontainer/' +
                    "$normalizedId/$normalizedVersion/" +
                    "$normalizedId.$normalizedVersion.nupkg")
                try {
                    if ($null -ne $DownloadPackageArchive) {
                        & $DownloadPackageArchive $uri $packagePath |
                            Out-Null
                    }
                    else {
                        Invoke-WebRequest `
                            -Uri $uri `
                            -OutFile $packagePath `
                            -MaximumRedirection 5 `
                            -TimeoutSec 90 `
                            -ErrorAction Stop | Out-Null
                    }
                }
                catch {
                    throw (
                        "Could not download framework package '$($package.Id) " +
                        "$($package.Version)' from NuGet.org: " +
                        $_.Exception.Message)
                }
                if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
                    throw (
                        "NuGet.org did not create the requested framework " +
                        "package archive '$fileName'.")
                }
            }

            $identity = Get-SharpProofPackageIdentity `
                -Path $packagePath `
                -RequireSingleIdentity
            if (-not [string]::Equals(
                    [string]$identity.Id,
                    [string]$package.Id,
                    [StringComparison]::OrdinalIgnoreCase) -or
                [string]$identity.Version -cne [string]$package.Version) {
                throw (
                    "Framework package archive '$packagePath' has identity " +
                    "'$($identity.Id) $($identity.Version)'; expected " +
                    "'$($package.Id) $($package.Version)'.")
            }

            if ($null -ne $downloadRoot) {
                [IO.Directory]::CreateDirectory(
                    [IO.Path]::GetDirectoryName($source)) | Out-Null
                [IO.File]::Copy($packagePath, $source, $true)
            }
            [IO.File]::Copy(
                $source,
                (Join-Path $frameworkSource $fileName),
                $true)
        }
        finally {
            if ($null -ne $downloadRoot -and
                (Test-Path -LiteralPath $downloadRoot -PathType Container)) {
                Remove-Item -LiteralPath $downloadRoot -Recurse -Force
            }
        }
    }

    $unexpectedPackages = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@(
                Get-ChildItem -LiteralPath $frameworkSource -File -Filter '*.nupkg' |
                    ForEach-Object {
                        Get-SharpProofPackageIdentity -Path $_.FullName
                    } |
                    Where-Object {
                        $_.Id.StartsWith(
                            'SharpProof',
                            [StringComparison]::OrdinalIgnoreCase)
                    }
            )))
    if ($unexpectedPackages.Count -ne 0) {
        throw (
            'The framework-only package source unexpectedly contains ' +
            'SharpProof packages.')
    }
    return ([ordered]@{
            Source   = $frameworkSource
            Packages = $frameworkPackages
        }).AsReadOnly()
}

function Invoke-ConsumerDotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$Quiet
    )

    Push-Location $WorkingDirectory
    try {
        $output = & dotnet @Arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
        if (-not [string]::IsNullOrEmpty($output) -and
            (-not $Quiet -or $exitCode -ne 0)) {
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

    $analyzerItems = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($evaluation.Items.Analyzer)))
    $sharpProofItems = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@(
                $analyzerItems |
                    Where-Object {
                        ([string]$_.Identity) -match
                        '[/\\]SharpProof\.[^/\\]+\.dll$'
                    }
            )))
    $entryPointNameBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $generatorNameBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $legacyEntryPointBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($item in $sharpProofItems) {
        $identity = ([string]$item.Identity).Replace('\', '/')
        $name = ($identity -split '/')[-1]
        if ($item.SharpProofAnalyzerRole -eq 'EntryPoint') {
            $entryPointNameBuilder.Add($name)
        }
        if ($item.SharpProofAnalyzerRole -eq 'Generator') {
            $generatorNameBuilder.Add($name)
        }
        if ($identity -match '/SharpProof\.PortableAnalyzer\.dll$') {
            $legacyEntryPointBuilder.Add($item)
        }
    }

    $entryPointNames = $entryPointNameBuilder.ToImmutable()
    $generatorNames = $generatorNameBuilder.ToImmutable()
    $legacyEntryPoints = $legacyEntryPointBuilder.ToImmutable()

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
            $json = ([ordered]@{
                    sdk = ([ordered]@{
                            version     = $SdkVersion
                            rollForward = 'disable'
                        }).AsReadOnly()
                }).AsReadOnly() | ConvertTo-Json -Depth 3
            [IO.File]::WriteAllText(
                $globalJson,
                $json + "`n",
                $encoding)
        }

        $frameworkCatalog = New-FrameworkPackageSource `
            -Root $root `
            -RepositoryRoot $RepositoryRoot
        $frameworkSource = [string]$frameworkCatalog.Source
        $escapedSource = [Security.SecurityElement]::Escape($Source)
        $escapedFrameworkSource =
        [Security.SecurityElement]::Escape($frameworkSource)
        $nugetConfig = Join-Path $root 'NuGet.config'
        $nugetConfigLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
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
                $frameworkCatalog.Packages |
                    ForEach-Object { $_.Pattern } |
                    Select-Object -Unique |
                    ForEach-Object {
                        "      <package pattern=`"$_`" />"
                    }
                '    </packageSource>'
                '  </packageSourceMapping>'
                '</configuration>'
            ))
        [IO.File]::WriteAllText(
            $nugetConfig,
            ($nugetConfigLines -join "`n") + "`n",
            $encoding)

        $actualSdk = Invoke-ConsumerDotNet `
            -WorkingDirectory $root `
            -Arguments @('--version')
        if (-not [string]::IsNullOrWhiteSpace($SdkVersion) -and
            $actualSdk.Trim() -ne $SdkVersion) {
            throw (
                "Expected actual consumer SDK '$SdkVersion', but dotnet " +
                "selected '$($actualSdk.Trim())'.")
        }

        $contract = Get-Content -LiteralPath (Join-Path `
                $RepositoryRoot 'eng/acceptance/contract.json') -Raw |
            ConvertFrom-Json
        $frameworks = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($contract.supportedTargetFrameworks | ForEach-Object {
                        [string]$_
                    })))
        if ($frameworks.Count -eq 0) {
            throw 'The acceptance contract must declare supported target frameworks.'
        }

        $escapedVersion = [Security.SecurityElement]::Escape($Version)
        foreach ($framework in $frameworks) {
            $consumer = Join-Path $root $framework
            [IO.Directory]::CreateDirectory($consumer) | Out-Null
            $projectLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    '<Project Sdk="Microsoft.NET.Sdk">'
                    '  <PropertyGroup>'
                    "    <TargetFramework>$framework</TargetFramework>"
                    '    <LangVersion>12.0</LangVersion>'
                    '    <SharpProofProfile>advisory</SharpProofProfile>'
                    '    <SharpProofVerify>false</SharpProofVerify>'
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
                ))
            [IO.File]::WriteAllText(
                (Join-Path $consumer 'Consumer.csproj'),
                ($projectLines -join "`n") + "`n",
                $encoding)
            $sourceLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    'using SharpProof.Attributes;'
                    ''
                    'public static class Subject {'
                    '    [ZeroAllocations]'
                    '    public static int Identity(int value) => value;'
                    '}'
                ))
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
                '--nologo') | Out-Null
            $analyzers = Invoke-ConsumerDotNet `
                -WorkingDirectory $consumer `
                -Arguments @(
                'msbuild',
                'Consumer.csproj',
                '-getItem:Analyzer',
                '--nologo')
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
                '--nologo') | Out-Null
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

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$testProject = Join-Path $repositoryRoot 'src\SharpProof.Package.IntegrationTests\SharpProof.Package.IntegrationTests.csproj'
$isSupportedWorkerHost = $IsLinux -and
$env:SHARPPROOF_CONTAINER -ceq '1' -and
[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
[System.Runtime.InteropServices.Architecture]::X64 -and
[System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq
[System.Runtime.InteropServices.Architecture]::X64
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
    Write-Host (
        "Validated exact SharpProof package source: " +
        $resolvedPackageSource.Path)
    return
}
if ($null -ne $resolvedPackageSource) {
    $packageVersion = [string]$resolvedPackageSource.Version
    Test-SharpProofFrameworkConsumers `
        -Source $resolvedPackageSource.Path `
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
if (-not $isSupportedWorkerHost) {
    throw (
        'SharpProof package consumers must run in the canonical Linux ' +
        'amd64 container.')
}
$previousPackageSource = [Environment]::GetEnvironmentVariable(
    'SHARPPROOF_PACKAGE_SOURCE',
    [EnvironmentVariableTarget]::Process)
if ($null -ne $resolvedPackageSource) {
    [Environment]::SetEnvironmentVariable(
        'SHARPPROOF_PACKAGE_SOURCE',
        $resolvedPackageSource.Path,
        [EnvironmentVariableTarget]::Process)
}

Push-Location $repositoryRoot
try {
    $packageTestArgumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    [void]$packageTestArgumentBuilder.AddRange([string[]]@(
            'test',
            $testProject,
            '--configuration',
            $Configuration,
            '--no-restore',
            '--logger',
            'console;verbosity=minimal'))
    if ($null -ne $resolvedPackageSource) {
        # Framework compatibility is exercised above. Re-run only the exact
        # package graph, analyzer discovery, and real verifier proof here;
        # the complete package suite belongs to the ordinary test/acceptance
        # lane and must not be duplicated for every consumer job.
        $focusedPackageFilter =
        'FullyQualifiedName=SharpProof.Package.Test.PackageLayoutSmokeTests.PackageGraphAndLayoutsAreExact|' +
        'FullyQualifiedName=SharpProof.Package.Test.PackageLayoutSmokeTests.StrictAnalyzerSetDiscoversEachEntrypointOnce|' +
        'FullyQualifiedName=SharpProof.Package.Test.PackageLayoutSmokeTests.VerifierPackageTransitivelySuppliesPortableProduct'
        [void]$packageTestArgumentBuilder.AddRange([string[]]@('--filter', $focusedPackageFilter))
    }
    $packageTestArguments = [string[]]@($packageTestArgumentBuilder.ToImmutable())
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

Write-Host 'SharpProof packaged analyzer and out-of-process worker consumer passed.'
