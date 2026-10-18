[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackageSource)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
$feed = (Resolve-Path -LiteralPath $PackageSource).Path
$files = @(Get-ChildItem -LiteralPath $feed -File | Where-Object Extension -In @('.nupkg', '.snupkg'))
$identities = Get-SharpProofPackageIdentitySet -Files $files -RequireRepository
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $identities.RepositoryCommits.Count -ne 1 -or $identities.RepositoryCommits[0] -cne $commit) {
    throw 'Offline stack-trace qualification requires exact-commit packed artifacts.'
}
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$root = Join-Path $parent ('sharpproof-offline-stack-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $root 'dll-only-shadow-copy'
$probe = Join-Path $root 'probe'
[IO.Directory]::CreateDirectory($payload) | Out-Null
[IO.Directory]::CreateDirectory($probe) | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
$sdkVersion = [string](Get-Content (Join-Path $repository 'global.json') -Raw | ConvertFrom-Json).sdk.version
$sdkLines = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate the installed SDK needed for the compiler-host probes.' }
$sdkLine = @($sdkLines | Where-Object { $_ -match ('^' + [regex]::Escape($sdkVersion) + ' \[(.+)\]$') })
if ($sdkLine.Count -ne 1) { throw 'The exact SDK for the shipped compiler-host exception probes is unavailable.' }
$null = $sdkLine[0] -match ('^' + [regex]::Escape($sdkVersion) + ' \[(.+)\]$')
$sdkRoot = Join-Path $Matches[1] $sdkVersion

try {
    foreach ($identity in $identities.Identities | Where-Object { $_.File.Extension -eq '.nupkg' -and $_.Identity.Id -ne 'SharpProof' }) {
        $archive = [IO.Compression.ZipFile]::OpenRead($identity.File.FullName)
        try {
            foreach ($entry in $archive.Entries) {
                if (-not $entry.FullName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) { continue }
                $relative = $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar)
                $destination = [IO.Path]::GetFullPath((Join-Path $payload $relative))
                if (-not $destination.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { throw 'A packed assembly path escapes the isolated shadow copy.' }
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
            }
        }
        finally { $archive.Dispose() }
    }
    if (@(Get-ChildItem -LiteralPath $payload -Filter '*.pdb' -File -Recurse).Count) { throw 'The exception probe unexpectedly has sidecar symbols.' }
    [IO.File]::Copy((Join-Path $repository 'global.json'), (Join-Path $probe 'global.json'))
    [IO.File]::WriteAllText((Join-Path $probe 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>', $encoding)
    [IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>', $encoding)
    [IO.File]::WriteAllText((Join-Path $probe 'Program.cs'), @'
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

var payload = Path.GetFullPath(args[0]);
var sdk = Path.GetFullPath(args[1]);
var role = args[2];
if (Directory.EnumerateFiles(payload, "*.pdb", SearchOption.AllDirectories).Any())
    throw new InvalidOperationException("The shipped exception probe must run with DLLs only.");
var compilerRole = role is "analyzer" or "generator";
var directories = compilerRole
    ? new[] { "tools/analyzers/dotnet/cs", "tools/shared/netstandard2.0", "tools/collector", "lib/netstandard2.0" }
    : new[] { "tools/net10", "lib/netstandard2.0" };
AssemblyLoadContext.Default.Resolving += (_, name) => {
    foreach (var directory in directories.Select(d => Path.Combine(payload, d))
        .Concat(new[] { Path.Combine(sdk, "Roslyn/bincore"), sdk })) {
        var candidate = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }
    return null;
};
var entry = role switch {
    "analyzer" => ("tools/analyzers/dotnet/cs/SharpProof.Analyzer.dll", "SharpProof.Analyzer.SharpProofAnalyzer", "Initialize"),
    "generator" => ("tools/analyzers/dotnet/cs/SharpProof.ContractForGenerator.dll", "SharpProof.ContractForGenerator.ContractForValidatorGenerator", "Initialize"),
    "build-task" => ("tools/net10/SharpProof.BuildTasks.dll", "SharpProof.BuildTasks.RunVerifier", "ResolveDotNetHost"),
    "worker" => ("tools/net10/SharpProof.Worker.dll", "SharpProof.Worker.WorkerHost", "Failure"),
    _ => throw new ArgumentException("Unknown shipped tooling role.")
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(payload, entry.Item1));
var type = assembly.GetType(entry.Item2, throwOnError: true)!;
var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
    .Where(m => m.Name == entry.Item3 && m.GetParameters().Length == (role == "worker" ? 2 : 1)).ToArray();
if (methods.Length != 1) throw new InvalidOperationException("The guarded shipped probe method is ambiguous or missing.");
var method = methods[0];
var parameters = method.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
Exception failure;
try {
    method.Invoke(method.IsStatic ? null : Activator.CreateInstance(type), parameters);
    throw new InvalidOperationException("The guarded shipped method unexpectedly accepted invalid input.");
}
catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException) {
    failure = exception.InnerException;
}
var frames = new StackTrace(failure, fNeedFileInfo: true).GetFrames();
var sourceFrame = frames.FirstOrDefault(frame => frame.GetMethod()?.Module.Assembly == assembly &&
    !string.IsNullOrWhiteSpace(frame.GetFileName()) && frame.GetFileName()!.EndsWith(".cs", StringComparison.Ordinal) &&
    frame.GetFileLineNumber() > 0);
if (sourceFrame is null) throw new InvalidOperationException(role + " has no shipped source file and line in its exception stack:\n" + failure);
Console.WriteLine(role + ": " + sourceFrame.GetFileName() + ":" + sourceFrame.GetFileLineNumber());
'@, $encoding)
    Push-Location $probe
    try {
        $build = (& dotnet build Probe.csproj -c Release --nologo '-p:UseSharedCompilation=false' 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) { throw "Offline exception probe build failed: $build" }
        foreach ($role in @('analyzer', 'generator', 'build-task', 'worker')) {
            $output = (& dotnet (Join-Path $probe 'bin/Release/net10.0/Probe.dll') $payload $sdkRoot $role 2>&1 | Out-String)
            if ($LASTEXITCODE -ne 0) { throw "The actual shipped $role exception has no offline source location: $output" }
            Write-Host $output.TrimEnd()
        }
    }
    finally { Pop-Location }
    Write-Output 'Actual packed analyzer, generator, build-task, and worker exceptions have source file and line with no sidecar PDBs or symbol-server lookup.'
}
finally {
    $fullRoot = [IO.Path]::GetFullPath($root)
    if (-not $fullRoot.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($fullRoot) -notlike 'sharpproof-offline-stack-*') { throw 'Refusing to remove an unexpected exception probe directory.' }
    Remove-Item -LiteralPath $fullRoot -Recurse -Force
}
