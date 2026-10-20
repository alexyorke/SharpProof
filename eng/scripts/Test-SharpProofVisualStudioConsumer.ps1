[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [Parameter(Mandatory = $true)][ValidateSet(17, 18)][int]$MajorVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64 -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'Visual Studio acceptance requires a native Windows x64 OS and PowerShell process.'
}
$vswhereCommand = Get-Command vswhere -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$vswhere = if ($null -ne $vswhereCommand) { $vswhereCommand.Source } else {
    Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) 'Microsoft Visual Studio/Installer/vswhere.exe'
}
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { throw 'The installed Visual Studio vswhere utility was not found.' }
$selection = @('-latest', '-products', '*', '-version', "[$MajorVersion.0,$($MajorVersion + 1).0)", '-requires', 'Microsoft.Component.MSBuild')
$installationJson = (& $vswhere @selection -format json -utf8 | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'vswhere could not enumerate the requested Visual Studio major version.' }
$installations = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($installationJson | ConvertFrom-Json)))
if ($installations.Count -ne 1 -or ([version]$installations[0].installationVersion).Major -ne $MajorVersion) {
    throw "No exact Visual Studio $MajorVersion installation with MSBuild was selected."
}
$installation = $installations[0]
$directorySeparator = [IO.Path]::DirectorySeparatorChar
$msbuildPattern = @('MSBuild', 'Current', 'Bin', 'amd64', 'MSBuild.exe') -join $directorySeparator
$msbuildCandidates = @(& $vswhere @selection -find $msbuildPattern)
if ($LASTEXITCODE -ne 0 -or $msbuildCandidates.Count -ne 1) { throw 'vswhere did not identify exactly one installed amd64 MSBuild.exe.' }
$msbuild = (Resolve-Path -LiteralPath $msbuildCandidates[0]).Path
$installationPrefix = ([IO.Path]::GetFullPath($installation.installationPath)).TrimEnd($directorySeparator) + $directorySeparator
if (-not $msbuild.StartsWith($installationPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The resolved MSBuild executable is outside the selected Visual Studio installation.' }

Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
$feed = (Resolve-Path -LiteralPath $PackageSource).Path
$files = @(Get-ChildItem -LiteralPath $feed -File | Where-Object Extension -In @('.nupkg', '.snupkg'))
$identities = Get-SharpProofPackageIdentitySet -Files $files -RequireRepository
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $identities.RepositoryCommits.Count -ne 1 -or $identities.RepositoryCommits[0] -cne $commit) {
    throw 'Visual Studio acceptance requires three main packages and two symbol packages from this exact checkout commit.'
}
$version = [string]$identities.Version
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd($directorySeparator)
$root = Join-Path $parent ('SharpProof-VisualStudio-' + [guid]::NewGuid().ToString('N'))
$cache = Join-Path $root 'packages'
$encoding = [Text.UTF8Encoding]::new($false)
[IO.Directory]::CreateDirectory($root) | Out-Null

function Invoke-VisualStudioMSBuild([string]$Directory, [string[]]$Arguments, [switch]$Quiet) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $msbuild
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    foreach ($key in @($start.Environment.Keys)) {
        if ($key.StartsWith('SHARPPROOF', [StringComparison]::OrdinalIgnoreCase)) { [void]$start.Environment.Remove($key) }
    }
    $start.Environment['NUGET_PACKAGES'] = $cache
    $start.Environment['DOTNET_CLI_HOME'] = Join-Path $root 'dotnet-home'
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Could not launch the selected Visual Studio MSBuild executable.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(180000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'Visual Studio MSBuild acceptance command exceeded 180 seconds.'
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if (-not $Quiet -or $process.ExitCode -ne 0) { Write-Host (($output -split '\r?\n' | Select-Object -Last 100) -join "`n") }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output }
    }
    finally { $process.Dispose() }
}

try {
    $probePath = Join-Path $root 'HostProbe.proj'
    [IO.File]::WriteAllText($probePath, '<Project />', $encoding)
    $probe = Invoke-VisualStudioMSBuild -Directory $root -Quiet -Arguments @(
        $probePath, '-nologo', '/nodeReuse:false', '-getProperty:MSBuildRuntimeType,MSBuildVersion,MSBuildToolsPath')
    if ($probe.ExitCode -ne 0) { throw 'The installed Visual Studio MSBuild host probe failed.' }
    $hostProperties = ($probe.Output | ConvertFrom-Json).Properties
    if ($hostProperties.MSBuildRuntimeType -cne 'Full' -or ([version]$hostProperties.MSBuildVersion).Major -ne $MajorVersion) {
        throw 'The selected executable is not the expected major version of Full MSBuild.'
    }
    Write-Host "Visual Studio provenance: installation=$($installation.installationPath); installationVersion=$($installation.installationVersion); MSBuild=$msbuild; engineVersion=$($hostProperties.MSBuildVersion); runtime=$($hostProperties.MSBuildRuntimeType); packageCommit=$commit."

    if ($MajorVersion -eq 18) {
        & (Join-Path $PSScriptRoot 'Test-SharpProofPlugAndPlayConsumer.ps1') -PackageSource $feed `
            -PackageId SharpProof -OsFamily windows -Architecture x64 -TargetFramework net48 -MSBuildPath $msbuild
        Write-Host 'Visual Studio 18 Full MSBuild one-package proof/refutation/library-consumer acceptance passed; no interactive IDE behavior is asserted.'
    }
    else {
        $consumer = Join-Path $root 'portable consumer with spaces'
        [IO.Directory]::CreateDirectory($consumer) | Out-Null
        [IO.File]::WriteAllText((Join-Path $consumer 'global.json'), '{"sdk":{"version":"9.0.300","rollForward":"disable"}}', $encoding)
        $escapedFeed = [Security.SecurityElement]::Escape($feed)
        [IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), @"
<configuration><packageSources><clear /><add key="exact-product" value="$escapedFeed" /><add key="framework-references" value="https://api.nuget.org/v3/index.json" /></packageSources><packageSourceMapping><packageSource key="exact-product"><package pattern="SharpProof*" /></packageSource><packageSource key="framework-references"><package pattern="Microsoft.NETFramework.ReferenceAssemblies*" /><package pattern="NETStandard.*" /><package pattern="Microsoft.NETCore.Platforms" /></packageSource></packageSourceMapping></configuration>
"@, $encoding)
        [IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <SharpProofProfile>advisory</SharpProofProfile><SharpProofVerify>false</SharpProofVerify>
    <WarningsAsErrors>SP0027;CS8032;CS8034;CS9057;CS8785;AD0001</WarningsAsErrors>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="SharpProof" Version="$([Security.SecurityElement]::Escape($version))" /></ItemGroup>
</Project>
"@, $encoding)
        $properties = Invoke-VisualStudioMSBuild -Directory $consumer -Quiet -Arguments @(
            'Consumer.csproj', '-nologo', '/nodeReuse:false', '-getProperty:NETCoreSdkVersion,MSBuildRuntimeType,MSBuildVersion,RoslynTargetsPath')
        if ($properties.ExitCode -ne 0) { throw 'Visual Studio 17 could not evaluate the minimum SDK consumer.' }
        $evaluated = ($properties.Output | ConvertFrom-Json).Properties
        if ($evaluated.NETCoreSdkVersion -cne '9.0.300' -or $evaluated.MSBuildRuntimeType -cne 'Full' -or
            ([version]$evaluated.MSBuildVersion).Major -ne 17) { throw 'Visual Studio 17 did not select the pinned SDK 9.0.300 and Full MSBuild host.' }
        $compilerPath = Join-Path $evaluated.RoslynTargetsPath 'csc.exe'
        if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) { throw 'The evaluated Visual Studio compiler executable is unavailable.' }
        Write-Host "Portable compiler provenance: SDK=$($evaluated.NETCoreSdkVersion); compiler=$compilerPath; compilerVersion=$([Diagnostics.FileVersionInfo]::GetVersionInfo($compilerPath).ProductVersion)."
        $sourcePath = Join-Path $consumer 'Subject.cs'
        $validSource = @'
using SharpProof.Attributes;
public static class Subject {
    public static void Positive(int value) { Contract.Requires(value > 0); }
    public static void Call() { Positive(1); }
}
'@
        [IO.File]::WriteAllText($sourcePath, $validSource, $encoding)
        $restore = Invoke-VisualStudioMSBuild -Directory $consumer -Arguments @(
            'Consumer.csproj', '-t:Restore', '-nologo', '/nodeReuse:false', '-p:RestoreConfigFile=NuGet.Config')
        if ($restore.ExitCode -ne 0) { throw 'Visual Studio 17 portable consumer restore failed.' }
        $buildArguments = @('Consumer.csproj', '-t:Rebuild', '-p:Configuration=Release', '-nologo', '/nodeReuse:false', '-p:UseSharedCompilation=false')
        # Full MSBuild prints the compiler's /warnaserror+:CODE switches even
        # when compilation succeeds. Match emitted diagnostics, not bare IDs.
        $analyzerFailureDiagnostic = '\b(?:warning|error)[ \t]+(?:CS8032|CS8034|CS9057|CS8785|AD0001)[ \t]*:'
        $passing = Invoke-VisualStudioMSBuild -Directory $consumer -Arguments $buildArguments
        if ($passing.ExitCode -ne 0 -or $passing.Output -match $analyzerFailureDiagnostic) { throw 'Visual Studio 17 portable analyzer passing control failed or could not load its analyzer.' }
        [IO.File]::WriteAllText($sourcePath, $validSource.Replace('Positive(1);', 'Positive(-1);'), $encoding)
        $negative = Invoke-VisualStudioMSBuild -Directory $consumer -Arguments $buildArguments
        if ($negative.ExitCode -eq 0 -or $negative.Output -notmatch '\berror[ \t]+SP0027[ \t]*:' -or
            $negative.Output -match $analyzerFailureDiagnostic) { throw 'Visual Studio 17 did not report the precondition violation through the real portable analyzer.' }
        if (Test-Path -LiteralPath (Join-Path $consumer 'obj/Release/net48/SharpProof/result.json')) { throw 'The advisory-only Visual Studio 17 control unexpectedly ran full verification.' }
        Write-Host 'Visual Studio 17 Full MSBuild portable analysis passed: SDK 9.0.300, passing control, and SP0027 negative control. Full verification and interactive IDE behavior are not asserted.'
    }
}
finally {
    $fullRoot = [IO.Path]::GetFullPath($root)
    if (-not $fullRoot.StartsWith($parent + $directorySeparator, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($fullRoot) -notlike 'SharpProof-VisualStudio-*') { throw 'Refusing to remove an unexpected Visual Studio fixture.' }
    Remove-Item -LiteralPath $fullRoot -Recurse -Force
}
