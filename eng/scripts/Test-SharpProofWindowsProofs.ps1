[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$actualHost = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$actualProcess = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if (-not $IsWindows -or $actualHost -cne $Architecture -or $actualProcess -cne $Architecture) {
    throw "Native proof acceptance requires matching Windows $Architecture OS and process (host=$actualHost; process=$actualProcess)."
}
if ([Environment]::Version.Major -lt 10) {
    throw ('Native proof acceptance requires PowerShell 7.6 or later running on .NET 10 or later. ' +
        "This PowerShell process runs .NET $([Environment]::Version). " +
        'Update PowerShell; installing the .NET SDK alone does not update its runtime.')
}
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
$source = (Resolve-Path -LiteralPath $PackageSource).Path
$packages = @(Get-ChildItem -LiteralPath $source -File |
        Where-Object Extension -In @('.nupkg', '.snupkg'))
$identities = Get-SharpProofPackageIdentitySet -Files $packages
$version = [string]$identities.Version
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sdk = (Get-Content (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$root = Join-Path ([IO.Path]::GetTempPath()) ('SharpProof Windows proofs ' + [char]0x03bb + ' ' + [Guid]::NewGuid().ToString('N'))
$consumer = Join-Path $root 'consumer with spaces'
$cache = Join-Path $root 'packages'
$resultPath = Join-Path $consumer 'obj/Release/net10.0/SharpProof/result.json'
$encoding = [Text.UTF8Encoding]::new($false)
$previousPackages = $env:NUGET_PACKAGES

function Invoke-ProofBuild {
    param([string[]]$ExtraArguments = @(), [switch]$ExpectFailure)
    Push-Location $consumer
    try {
        $output = & dotnet build Consumer.csproj -c Release --no-restore --nologo `
            /nodeReuse:false '-p:UseSharedCompilation=false' @ExtraArguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
        Write-Host $output.TrimEnd()
        if (($exitCode -ne 0) -ne [bool]$ExpectFailure) {
            throw "Unexpected native proof build exit code $exitCode."
        }
        return $output
    }
    finally { Pop-Location }
}

function Write-ProofSource {
    param([string]$ReturnedExpression = 'value')
    [IO.File]::WriteAllText((Join-Path $consumer 'Subject.cs'), @"
using SharpProof.Attributes;
public static class Subject {
    public static long Identity(long value) {
        Contract.Ensures(Contract.Result<long>() == value);
        return $ReturnedExpression;
    }
}
"@, $encoding)
}

function Assert-ProofOutcome {
    param([string]$Outcome)
    $response = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($response.runStatus -cne 'Complete' -or
        @($response.claimResults).Count -ne 1 -or
        $response.claimResults[0].outcome -cne $Outcome) {
        throw "Expected one complete $Outcome native claim result."
    }
    if ($response.claimResults[0].claimId -cne $response.manifest.claims[0].claimId -or
        $response.inputHash -notmatch '^[0-9a-f]{64}$' -or
        $response.requestHash -notmatch '^[0-9a-f]{64}$') {
        throw 'The native claim evidence is not bound to its manifest and input.'
    }
    if ($Outcome -ceq 'Refuted' -and @($response.claimResults[0].model).Count -eq 0) {
        throw 'The native refutation did not publish a counterexample.'
    }
    return $response
}

function Test-WindowsPublicationPaths {
    $hostDll = Join-Path $cache "sharpproof.verifier/$version/tools/net10/SharpProof.Host.dll"
    # Load from bytes so the acceptance process does not lock its test cache.
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($hostDll))
    $lease = $assembly.GetType('SharpProof.Host.PublicationLease', $true)
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $canonical = $lease.GetMethod('CanonicalMember', $flags, $null, [type[]]@([string]), $null)
    $validate = $lease.GetMethod('ValidateMembers', $flags)
    $member = Join-Path $consumer 'publication.json'
    Assert-NativeException {
        $null = $validate.Invoke($null, [object[]]@(, [string[]]@($member, $member.ToUpperInvariant())))
    } ([ArgumentException])
    $acquire = $lease.GetMethod('Acquire', $flags)
    $acquireAsync = $lease.GetMethod('AcquireAsync', $flags)
    $held = $acquire.Invoke($null, [object[]]@([string[]]@($member), [Threading.CancellationToken]::None))
    $deadline = [Threading.CancellationTokenSource]::new(500)
    try {
        $pending = $acquireAsync.Invoke($null, [object[]]@([string[]]@($member.ToUpperInvariant()), $deadline.Token))
        Assert-NativeException {
            $unexpected = $pending.GetAwaiter().GetResult()
            $unexpected.Dispose()
        } ([OperationCanceledException])
    }
    finally {
        $held.Dispose()
        $deadline.Dispose()
    }
    foreach ($path in @(
            ($member + ':stream'),
            (Join-Path $consumer 'alias~1.json'),
            (Join-Path $consumer 'NUL.json'),
            (Join-Path $consumer 'ambiguous. '),
            '\\localhost\c$\publication.json')) {
        Assert-NativeException {
            $null = $canonical.Invoke($null, [object[]]@($path))
        } ([ArgumentException])
    }
    $junction = Join-Path $root 'junction'
    try {
        New-Item -ItemType Junction -Path $junction -Target $consumer | Out-Null
        Assert-NativeException {
            $null = $canonical.Invoke($null, [object[]]@((Join-Path $junction 'publication.json')))
        } ([ArgumentException])
    }
    finally {
        # Remove the junction itself without traversing the target.
        if ([IO.Directory]::Exists($junction)) { [IO.Directory]::Delete($junction) }
    }
    $paths = $assembly.GetType('SharpProof.Host.PublicationPaths', $true)
    $runtimeGuard = $paths.GetMethod('ValidateWorkerRuntime', $flags)
    Assert-NativeException {
        $null = $runtimeGuard.Invoke($null, [object[]]@(
                [string[]]@((Join-Path (Split-Path $hostDll) 'result.json').ToUpperInvariant()), $hostDll))
    } ([ArgumentException])
}

function Assert-NativeException {
    param([scriptblock]$Action, [type]$ExpectedType)
    try { & $Action | Out-Null }
    catch {
        # PowerShell's method adapter can unwrap TargetInvocationException or
        # add its own MethodInvocationException. Inspect the actual cause.
        $cause = $_.Exception
        while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
        if (-not $ExpectedType.IsInstanceOfType($cause)) { throw }
        return
    }
    throw "Expected native exception $($ExpectedType.FullName) was not raised."
}

try {
    [IO.Directory]::CreateDirectory($consumer) | Out-Null
    $env:NUGET_PACKAGES = $cache
    [IO.File]::WriteAllText((Join-Path $root 'global.json'),
        (@{ sdk = @{ version = $sdk; rollForward = 'disable' } } | ConvertTo-Json), $encoding)
    $escapedSource = [Security.SecurityElement]::Escape($source)
    [IO.File]::WriteAllText((Join-Path $root 'NuGet.config'), @"
<configuration><packageSources><clear /><add key="exact-packages" value="$escapedSource" /></packageSources></configuration>
"@, $encoding)
    [IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpProofProfile>advisory</SharpProofProfile>
    <SharpProofVerify>true</SharpProofVerify>
    <SharpProofVerifyPolicy>require-proven</SharpProofVerifyPolicy>
    <SharpProofVerifyCacheEnabled>false</SharpProofVerifyCacheEnabled>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="SharpProof.Verifier" Version="$version" PrivateAssets="all" /></ItemGroup>
</Project>
"@, $encoding)
    Write-ProofSource
    Push-Location $consumer
    try {
        & dotnet restore Consumer.csproj --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Native proof consumer restore failed.' }
    }
    finally { Pop-Location }

    Invoke-ProofBuild | Out-Null
    $proven = Assert-ProofOutcome 'Proven'
    Write-ProofSource '0'
    Invoke-ProofBuild -ExpectFailure | Out-Null
    $refuted = Assert-ProofOutcome 'Refuted'
    if ($proven.inputHash -ceq $refuted.inputHash) { throw 'A changed source reused stale proof evidence.' }

    Write-ProofSource
    Invoke-ProofBuild -ExtraArguments @('-p:SharpProofVerifyQueryRlimit=1') -ExpectFailure | Out-Null
    Assert-ProofOutcome 'Unknown' | Out-Null

    Invoke-ProofBuild -ExtraArguments @(
        '-p:SharpProofVerifyMethodWallTimeMilliseconds=1',
        '-p:SharpProofVerifyProjectWallTimeMilliseconds=1',
        '-p:SharpProofVerifyTerminationGraceMilliseconds=100') -ExpectFailure | Out-Null
    $timeout = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($timeout.runStatus -cnotin @('TimedOut', 'Failed') -or
        @($timeout.claimResults | Where-Object outcome -CEQ 'Proven').Count -ne 0) {
        throw 'The native project deadline did not fail closed.'
    }

    # A fresh project build must refuse a missing or modified exact DLL. The
    # isolated package cache belongs only to this test and is restored after it.
    $dll = Join-Path $cache "sharpproof.verifier/$version/tools/native/win-$Architecture/libz3.dll"
    $original = [IO.File]::ReadAllBytes($dll)
    try {
        [IO.File]::Delete($dll)
        $output = Invoke-ProofBuild -ExpectFailure
        if ($output -notmatch 'native payload is missing') { throw 'Missing Z3 did not fail at package validation.' }
        $modified = [byte[]]$original.Clone()
        $modified[0] = $modified[0] -bxor 255
        [IO.File]::WriteAllBytes($dll, $modified)
        $output = Invoke-ProofBuild -ExpectFailure
        if ($output -notmatch 'wrong SHA-256 digest') { throw 'Modified Z3 was not rejected by its pinned digest.' }
    }
    finally { [IO.File]::WriteAllBytes($dll, $original) }

    Invoke-ProofBuild | Out-Null
    Assert-ProofOutcome 'Proven' | Out-Null
    Test-WindowsPublicationPaths
    Write-Host 'Native Windows proven/refuted/unknown claims, deadline, path guards, source freshness, and pinned DLL rejection passed.'
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    # Resolve and bound the exact temporary directory before recursive cleanup.
    $fullRoot = [IO.Path]::GetFullPath($root)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $fullRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($fullRoot) -notlike 'SharpProof Windows proofs *') {
        throw "Unexpected native proof temporary directory: $fullRoot"
    }
    if ([IO.Directory]::Exists($fullRoot)) { [IO.Directory]::Delete($fullRoot, $true) }
}
