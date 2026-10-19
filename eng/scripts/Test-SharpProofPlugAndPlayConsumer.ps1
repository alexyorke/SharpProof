[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [ValidateSet('SharpProof', 'SharpProof.Verifier')][string]$PackageId = 'SharpProof',
    [Parameter(Mandatory = $true)][ValidateSet('linux', 'windows', 'macos')][string]$OsFamily,
    [Parameter(Mandatory = $true)][ValidateSet('x64', 'arm64')][string]$Architecture,
    [ValidateSet('net10.0', 'net48')][string]$TargetFramework = 'net10.0',
    [string]$MSBuildPath,
    [ValidateRange(30, 600)][int]$CommandTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($TargetFramework -ceq 'net48' -and -not $IsWindows) {
    throw 'The .NET Framework runtime acceptance test requires native Windows.'
}
if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
    if (-not $IsWindows) { throw 'Visual Studio MSBuild acceptance requires native Windows.' }
    $MSBuildPath = (Resolve-Path -LiteralPath $MSBuildPath).Path
    if (-not [IO.Path]::GetFileName($MSBuildPath).Equals('MSBuild.exe', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Visual Studio acceptance must use the actual MSBuild.exe.'
    }
}
$consumerCommandTimeoutSeconds = $CommandTimeoutSeconds
$actualFamily = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } elseif ($IsLinux) { 'linux' } else { 'unknown' }
$actualHost = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$actualProcess = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if ($actualFamily -cne $OsFamily -or $actualHost -cne $Architecture -or $actualProcess -cne $Architecture) {
    throw "Expected native $OsFamily/$Architecture, found $actualFamily host=$actualHost process=$actualProcess."
}
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
$source = (Resolve-Path -LiteralPath $PackageSource).Path
$files = @(Get-ChildItem -LiteralPath $source -File | Where-Object Extension -In @('.nupkg', '.snupkg'))
$identities = Get-SharpProofPackageIdentitySet -Files $files -RequireRepository
$version = [string]$identities.Version
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$expectedCommit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $identities.RepositoryCommits.Count -ne 1 -or $identities.RepositoryCommits[0] -cne $expectedCommit) {
    throw 'Plug-and-play acceptance requires packages built from this exact checkout commit.'
}
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $IsWindows) {
    # macOS exposes the temp tree through /var, while the SDK publishes its
    # physical /private/var path. Establish one physical root before creating
    # the fixture so exact evidence-path comparisons use the same authority.
    Push-Location -LiteralPath $parent
    try {
        $physicalParent = (& /bin/pwd -P | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($physicalParent) -or
            -not [IO.Path]::IsPathRooted($physicalParent) -or
            -not (Test-Path -LiteralPath $physicalParent -PathType Container)) {
            throw 'Unable to establish the physical consumer temp directory.'
        }
        $parent = [IO.Path]::GetFullPath($physicalParent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    }
    finally { Pop-Location }
}
$root = Join-Path $parent ('SharpProof-PlugAndPlay-' + [guid]::NewGuid().ToString('N'))
$consumer = Join-Path $root 'fresh consumer with spaces'
$cache = Join-Path $root 'packages'
$encoding = [Text.UTF8Encoding]::new($false)
[IO.Directory]::CreateDirectory($consumer) | Out-Null

function Invoke-ConsumerCommand([string[]]$Arguments, [string]$WorkingDirectory = $consumer) {
    $commandFile = $dotnet
    $commandArgumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    [void]$commandArgumentBuilder.AddRange([object[]]@($Arguments))
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        $commandFile = $MSBuildPath
        $verb = $Arguments[0]
        if ($verb -ceq 'run') {
            # Execute the already-built application, not the modern worker CLR.
            $commandFile = Join-Path $WorkingDirectory "bin/Release/$TargetFramework/Application.exe"
            if (-not (Test-Path -LiteralPath $commandFile -PathType Leaf)) { throw 'The downstream executable is missing.' }
            $commandArgumentBuilder.Clear()
        }
        elseif ($verb -ceq 'query-host') {
            $commandArgumentBuilder.Clear()
            [void]$commandArgumentBuilder.AddRange([object[]]@($Arguments[1], '-nologo', '-verbosity:quiet',
                    '-getProperty:MSBuildRuntimeType,MSBuildVersion,NETCoreSdkVersion,_SharpProofVerifierHostSupported'))
        }
        else {
            if ($verb -cnotin @('restore', 'build', 'pack', 'clean')) { throw "Unexpected Visual Studio command: $verb" }
            $target = if ($verb -ceq 'build' -and $Arguments -ccontains '-t:Rebuild') { 'Rebuild' } else { $verb }
            $commandArgumentBuilder.Clear()
            [void]$commandArgumentBuilder.AddRange([object[]]@($Arguments[1], "-t:$target", '-nologo'))
            for ($index = 2; $index -lt $Arguments.Count; $index++) {
                $argument = $Arguments[$index]
                switch -CaseSensitive ($argument) {
                    '-c' { [void]$commandArgumentBuilder.Add('-p:Configuration=' + $Arguments[++$index]) }
                    '-o' { [void]$commandArgumentBuilder.Add('-p:PackageOutputPath=' + $Arguments[++$index]) }
                    '--configfile' { [void]$commandArgumentBuilder.Add('-p:RestoreConfigFile=' + (Join-Path $WorkingDirectory $Arguments[++$index])) }
                    '--nologo' { }
                    '--no-restore' { }
                    '-t:Rebuild' { }
                    default { [void]$commandArgumentBuilder.Add($argument) }
                }
            }
        }
    }
    $commandArguments = $commandArgumentBuilder.ToImmutable()
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $commandFile
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $commandArguments) { [void]$start.ArgumentList.Add($argument) }
    foreach ($key in @($start.Environment.Keys)) {
        if ($key.StartsWith('SHARPPROOF', [StringComparison]::OrdinalIgnoreCase)) { [void]$start.Environment.Remove($key) }
    }
    # The harness supplies package/feed isolation, never verifier configuration.
    $start.Environment['NUGET_PACKAGES'] = $cache
    $start.Environment['DOTNET_CLI_HOME'] = Join-Path $root 'dotnet-home'
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $start.Environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Unable to start the consumer SDK.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($consumerCommandTimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Consumer command exceeded $consumerCommandTimeoutSeconds seconds."
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        Write-Host (($output -split '\r?\n' | Select-Object -Last 100) -join "`n")
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output }
    }
    finally { $process.Dispose() }
}

function Write-ConsumerSource([string]$ReturnedExpression) {
    [IO.File]::WriteAllText((Join-Path $consumer 'Subject.cs'), @"
using SharpProof.Attributes;
public static class Subject
{
    public static System.Type ContractAttributeType => typeof(ZeroAllocationsAttribute);

    public static long Identity(long value)
    {
        Contract.Ensures(Contract.Result<long>() == value);
        return $ReturnedExpression;
    }
}
"@, $encoding)
}

function Assert-ConsumerEvidence([string]$Outcome) {
    $directory = Join-Path $consumer "obj/Release/$TargetFramework/SharpProof"
    $requestPath = Join-Path $directory 'request.json'
    $resultPath = Join-Path $directory 'result.json'
    $manifestPath = Join-Path $directory 'compiler-manifest.json'
    foreach ($path in @($requestPath, $resultPath, $manifestPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Default evidence file is missing: $path" }
    }
    $request = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $compiler = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $claims = @($result.claimResults)
    $manifestClaims = @($result.manifest.claims)
    if ($result.runStatus -cne 'Complete' -or $result.failureReason -cne 'None' -or
        $claims.Count -ne 1 -or $manifestClaims.Count -ne 1 -or
        $claims[0].outcome -cne $Outcome -or $claims[0].vacuity -cne 'None' -or
        $claims[0].reason -cne 'None' -or $manifestClaims[0].kind -cne 'Postcondition' -or
        $claims[0].claimId -cne $manifestClaims[0].claimId) {
        throw "Expected exactly one complete, nonvacuous $Outcome postcondition."
    }
    $requestHash = (Get-FileHash -LiteralPath $requestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($result.requestHash -cne $requestHash) { throw 'Published evidence request SHA256 does not match the exact request file.' }
    if ($request.compilerManifest.sha256 -cne $manifestHash) { throw 'Published request compiler-manifest SHA256 does not match the exact manifest file.' }
    if (-not [IO.Path]::IsPathRooted($request.compilerManifest.path)) { throw 'Published compiler-manifest path is not absolute.' }
    $publishedManifestPath = [IO.Path]::GetFullPath($request.compilerManifest.path)
    $expectedManifestPath = [IO.Path]::GetFullPath($manifestPath)
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($publishedManifestPath, $expectedManifestPath, $pathComparison)) {
        throw "Published compiler-manifest path mismatch: expected '$expectedManifestPath'; actual '$publishedManifestPath'."
    }
    if ($result.manifest.hash -cne $compiler.manifest.hash) { throw 'Published response claim-manifest hash differs from the compiler manifest.' }
    if ($result.inputHash -notmatch '^[0-9a-f]{64}$') { throw 'Published response input hash is malformed.' }
    $callables = @($result.callableResults)
    if ($callables.Count -ne 1 -or $callables[0].coverage -cne 'Complete') { throw 'The selected callable was not completely verified.' }
    if ($Outcome -ceq 'Refuted') {
        $model = @($claims[0].model)
        if ($model.Count -ne 1 -or $model[0].variable -cne 'parameter:0' -or $model[0].kind -cne 'Integer') { throw 'The refutation has no exact identity-input counterexample.' }
        # Returning zero violates Identity for every nonzero Int64 input.
        $counterexample = [long]0
        if (-not [long]::TryParse([string]$model[0].value, [Globalization.NumberStyles]::Integer,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$counterexample) -or $counterexample -eq 0) {
            throw 'No concrete nonzero Int64 identity counterexample was published.'
        }
    }
    return $result.inputHash
}

try {
    [IO.File]::Copy((Join-Path $repository 'global.json'), (Join-Path $consumer 'global.json'))
    $escapedSource = [Security.SecurityElement]::Escape($source)
    # The old framework needs reference assemblies, not a different verifier.
    # Mapping keeps all product packages bound exclusively to the exact feed.
    $frameworkSources = ''
    $frameworkMapping = ''
    if ($TargetFramework -ceq 'net48') {
        $frameworkSources = '<add key="framework-references" value="https://api.nuget.org/v3/index.json" />'
        $frameworkMapping = '<packageSource key="framework-references"><package pattern="Microsoft.NETFramework.ReferenceAssemblies*" /><package pattern="NETStandard.*" /><package pattern="Microsoft.NETCore.Platforms" /><package pattern="System.*" /></packageSource>'
    }
    [IO.File]::WriteAllText((Join-Path $consumer 'NuGet.config'), @"
<configuration><packageSources><clear /><add key="exact-packed-feed" value="$escapedSource" />$frameworkSources</packageSources><packageSourceMapping><packageSource key="exact-packed-feed"><package pattern="SharpProof*" /></packageSource>$frameworkMapping</packageSourceMapping></configuration>
"@, $encoding)
    $escapedVersion = [Security.SecurityElement]::Escape($version)
    [IO.File]::Copy((Join-Path $PSScriptRoot '../signing/SharpProof.Attributes.snk'), (Join-Path $consumer 'Consumer.snk'))
    [IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>$TargetFramework</TargetFramework><NuGetAudit>false</NuGetAudit><SignAssembly>true</SignAssembly><PublicSign>false</PublicSign><AssemblyOriginatorKeyFile>Consumer.snk</AssemblyOriginatorKeyFile><WarningsAsErrors>CS8002</WarningsAsErrors></PropertyGroup>
  <ItemGroup><PackageReference Include="$PackageId" Version="$escapedVersion" /></ItemGroup>
</Project>
"@, $encoding)
    Write-ConsumerSource 'value'
    $restore = Invoke-ConsumerCommand @('restore', 'Consumer.csproj', '--configfile', 'NuGet.config', '--nologo')
    if ($restore.ExitCode -ne 0) { throw 'Fresh one-package consumer restore failed.' }
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        $hostQuery = Invoke-ConsumerCommand @('query-host', 'Consumer.csproj')
        if ($hostQuery.ExitCode -ne 0) { throw 'Cannot evaluate the Visual Studio compiler host.' }
        $hostProperties = ($hostQuery.Output | ConvertFrom-Json).Properties
        if ($hostProperties.MSBuildRuntimeType -cne 'Full' -or
            [version]$hostProperties.MSBuildVersion -lt [version]'18.0' -or
            [version]$hostProperties.NETCoreSdkVersion -lt [version]'10.0.100' -or
            $hostProperties._SharpProofVerifierHostSupported -cne 'true') {
            throw 'The full-verification test must use qualified Full MSBuild 18+ and SDK 10+, with the verifier active.'
        }
        Write-Host "Qualified Full MSBuild $($hostProperties.MSBuildVersion), SDK $($hostProperties.NETCoreSdkVersion)."
    }
    $buildArguments = @('build', 'Consumer.csproj', '-c', 'Release', '--no-restore', '--nologo', '/nodeReuse:false', '-p:UseSharedCompilation=false')
    $passing = Invoke-ConsumerCommand $buildArguments
    if ($passing.ExitCode -ne 0) { throw 'Fresh default consumer did not build and prove its contract.' }
    $passingHash = Assert-ConsumerEvidence 'Proven'
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        $incremental = Invoke-ConsumerCommand $buildArguments
        if ($incremental.ExitCode -ne 0) { throw 'The unchanged Visual Studio consumer did not retain a valid proof.' }
        $null = Assert-ConsumerEvidence 'Proven'
    }
    Write-ConsumerSource '0'
    $failing = Invoke-ConsumerCommand ($buildArguments + @('-t:Rebuild'))
    if ($failing.ExitCode -eq 0 -or $failing.Output -notmatch '\bSP0051\b') { throw 'False contract did not fail with the refuted-contract diagnostic SP0051.' }
    $failingHash = Assert-ConsumerEvidence 'Refuted'
    if ($passingHash -ceq $failingHash) { throw 'Modified source reused stale proof evidence.' }

    # A library author's single product reference must remain usable by a
    # downstream application, including the public attribute runtime assembly.
    Write-ConsumerSource 'value'
    $libraryFeed = Join-Path $root 'library-feed'
    [IO.Directory]::CreateDirectory($libraryFeed) | Out-Null
    $pack = Invoke-ConsumerCommand @('pack', 'Consumer.csproj', '-c', 'Release', '--no-restore',
        '--nologo', '-o', $libraryFeed, '-p:PackageId=PlugAndPlay.Library', '-p:PackageVersion=1.0.0',
        '/nodeReuse:false', '-p:UseSharedCompilation=false')
    if ($pack.ExitCode -ne 0) { throw 'Packing the verified one-reference library failed.' }
    $null = Assert-ConsumerEvidence 'Proven'
    if (-not (Test-Path -LiteralPath (Join-Path $libraryFeed 'PlugAndPlay.Library.1.0.0.nupkg') -PathType Leaf)) {
        throw 'The consumer library package was not produced.'
    }
    $downstream = Join-Path $root 'downstream application with spaces'
    [IO.Directory]::CreateDirectory($downstream) | Out-Null
    [IO.File]::Copy((Join-Path $consumer 'global.json'), (Join-Path $downstream 'global.json'))
    $escapedLibraryFeed = [Security.SecurityElement]::Escape($libraryFeed)
    [IO.File]::WriteAllText((Join-Path $downstream 'NuGet.config'), @"
<configuration><packageSources><clear /><add key="exact-packed-feed" value="$escapedSource" /><add key="library-feed" value="$escapedLibraryFeed" />$frameworkSources</packageSources><packageSourceMapping><packageSource key="exact-packed-feed"><package pattern="SharpProof*" /></packageSource><packageSource key="library-feed"><package pattern="PlugAndPlay.Library" /></packageSource>$frameworkMapping</packageSourceMapping></configuration>
"@, $encoding)
    [IO.File]::WriteAllText((Join-Path $downstream 'Application.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>$TargetFramework</TargetFramework><OutputType>Exe</OutputType><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><PackageReference Include="PlugAndPlay.Library" Version="1.0.0" /></ItemGroup>
</Project>
"@, $encoding)
    [IO.File]::WriteAllText((Join-Path $downstream 'Program.cs'), @'
public static class Program
{
    public static int Main()
    {
        var result = Subject.Identity(42);
        var attributes = Subject.ContractAttributeType.Assembly.GetName().Name;
        var token = System.BitConverter.ToString(Subject.ContractAttributeType.Assembly.GetName().GetPublicKeyToken()).Replace("-", "").ToLowerInvariant();
        System.Console.WriteLine($"Subject.Identity={result}; Attributes={attributes}; Token={token}");
        System.Console.WriteLine($"RuntimeCore={typeof(object).Assembly.GetName().Name}; CLR={System.Environment.Version.Major}");
        return result == 42 && attributes == "SharpProof.Attributes" && token == "75825c3e099f5fac" ? 0 : 1;
    }
}
'@, $encoding)
    $downstreamRestore = Invoke-ConsumerCommand -WorkingDirectory $downstream -Arguments @(
        'restore', 'Application.csproj', '--configfile', 'NuGet.config', '--nologo')
    if ($downstreamRestore.ExitCode -ne 0) { throw 'The downstream library consumer could not restore.' }
    $assetsPath = Join-Path $downstream 'obj/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($id in @('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier')) {
        if (-not $assets.libraries.Contains("$id/$version")) { throw "The downstream application did not restore the transitive $id $version dependency." }
    }
    $downstreamBuild = Invoke-ConsumerCommand -WorkingDirectory $downstream -Arguments @(
        'build', 'Application.csproj', '-c', 'Release', '--no-restore', '--nologo',
        '/nodeReuse:false', '-p:UseSharedCompilation=false')
    if ($downstreamBuild.ExitCode -ne 0) { throw 'The downstream one-library-reference application did not build.' }
    $applicationOutput = Join-Path $downstream "bin/Release/$TargetFramework"
    if (-not (Test-Path -LiteralPath (Join-Path $applicationOutput 'SharpProof.Attributes.dll') -PathType Leaf)) {
        throw 'The downstream application did not receive the attribute runtime assembly.'
    }
    $nativeFiles = @(Get-ChildItem -LiteralPath $applicationOutput -Recurse -File |
            Where-Object { $_.Name -in @('libz3.so', 'libz3.dylib', 'libz3.dll', 'z3.dll') })
    if ($nativeFiles.Count -ne 0) { throw 'Verifier native build-tool payloads leaked into application runtime output.' }
    $run = Invoke-ConsumerCommand -WorkingDirectory $downstream -Arguments @(
        'run', '--project', 'Application.csproj', '-c', 'Release', '--no-build', '--no-restore')
    if ($run.ExitCode -ne 0 -or $run.Output -notmatch 'Subject\.Identity=42; Attributes=SharpProof\.Attributes') {
        throw 'The downstream library and attribute runtime dependencies did not execute successfully.'
    }
    if ($TargetFramework -ceq 'net48' -and $run.Output -notmatch 'RuntimeCore=mscorlib; CLR=4') {
        throw 'The Framework consumer must execute on the Windows CLR, not the modern .NET worker runtime.'
    }
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        $clean = Invoke-ConsumerCommand @('clean', 'Consumer.csproj', '-c', 'Release', '--nologo')
        if ($clean.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $consumer "obj/Release/$TargetFramework/SharpProof/result.json"))) {
            throw 'Visual Studio clean must invalidate the published verification result.'
        }
    }
    Write-Host "Plug-and-play $PackageId ($TargetFramework) passed on native $OsFamily/${Architecture}: one reference, no SharpProof settings, complete proof, bound refutation, and downstream library execution."
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($root)
    if (-not $resolvedRoot.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($resolvedRoot) -notlike 'SharpProof-PlugAndPlay-*') { throw 'Refusing to remove an unexpected consumer directory.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
