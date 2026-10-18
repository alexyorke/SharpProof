[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackageSource)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Qualify the optional tool in the canonical Linux amd64 container.'
$PackageSource = (Resolve-Path -LiteralPath $PackageSource).Path
$packages = @(Get-ChildItem -LiteralPath $PackageSource -Filter 'SharpProof.Tool.*.nupkg' -File)
if ($packages.Count -ne 1) { throw 'The optional tool feed must contain exactly one SharpProof.Tool package.' }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $spec = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::Ordinal) })
    if ($spec.Count -ne 1) { throw 'Expected one tool nuspec.' }
    $reader = [IO.StreamReader]::new($spec[0].Open())
    try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($metadata.package.metadata.id -cne 'SharpProof.Tool' -or
        $metadata.package.metadata.packageTypes.packageType.name -cne 'DotnetTool') {
        throw 'The candidate is not a SharpProof .NET tool package.'
    }
    $version = [string]$metadata.package.metadata.version
}
finally { $archive.Dispose() }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-tool-consumer-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
Copy-Item -LiteralPath (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path 'global.json') -Destination (Join-Path $scratch 'global.json')
$feed = [Security.SecurityElement]::Escape($PackageSource)
$configuration = Join-Path $scratch 'NuGet.Config'
[IO.File]::WriteAllText($configuration, "<configuration><packageSources><clear/><add key=`"exact-tool-feed`" value=`"$feed`"/></packageSources></configuration>")
function Invoke-ToolCheck([string[]]$Arguments) {
    $captured = @(& dotnet @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Tool acceptance command failed: $($Arguments -join ' ')`n$($captured -join "`n")" }
    return ($captured -join "`n")
}
Push-Location $scratch
try {
    [void](Invoke-ToolCheck @('new', 'tool-manifest'))
    [void](Invoke-ToolCheck @('tool', 'install', 'SharpProof.Tool', '--version', $version, '--configfile', $configuration))
    $help = Invoke-ToolCheck @('sharpproof', '--help')
    if ($help -notmatch 'explain <result.json>' -or $help -notmatch 'doctor') { throw 'Installed tool help does not expose its real commands.' }
    $doctor = (Invoke-ToolCheck @('sharpproof', 'doctor', '--json')) | ConvertFrom-Json
    if ($doctor.sdk -notmatch '^\d+\.\d+\.\d+' -or $doctor.scope -notmatch 'no project evaluation') { throw 'Installed tool doctor scope or SDK discovery failed.' }
    # Construct stored failure evidence with the exact packaged public protocol API.
    # This is a reader fixture, not a verification run or synthetic proof.
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $protocolEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('/SharpProof.Worker.Protocol.dll', [StringComparison]::Ordinal) })
        if ($protocolEntries.Count -ne 1) { throw 'Expected exactly one packaged tool protocol assembly.' }
        $protocolPath = Join-Path $scratch 'SharpProof.Worker.Protocol.dll'
        [IO.Compression.ZipFileExtensions]::ExtractToFile($protocolEntries[0], $protocolPath)
    }
    finally { $archive.Dispose() }
    # PowerShell can run on an older runtime than the tool. Execute the exact
    # packaged protocol assembly in an isolated .NET 10 fixture process instead.
    $fixture = Join-Path $scratch 'fixture'
    [void][IO.Directory]::CreateDirectory($fixture)
    $fixtureProject = Join-Path $fixture 'Fixture.csproj'
    $escapedProtocol = [Security.SecurityElement]::Escape($protocolPath)
    [IO.File]::WriteAllText($fixtureProject, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><Reference Include="SharpProof.Worker.Protocol"><HintPath>$escapedProtocol</HintPath></Reference></ItemGroup>
</Project>
"@)
    $fixtureConfiguration = Join-Path $fixture 'NuGet.Config'
    [IO.File]::WriteAllText($fixtureConfiguration, '<configuration><packageSources><clear/></packageSources></configuration>')
    [IO.File]::WriteAllText((Join-Path $fixture 'Program.cs'), @'
using SharpProof.Worker.Protocol;
var response = new WorkerVerifyResponse
{
    InputHash = new string('a', 64),
    RunStatus = WorkerRunStatus.Failed,
    FailureReason = WorkerRunFailureReason.InputUnavailable,
    Errors = [new() { Code = "input.unavailable", Message = "Stored reader fixture; no verification was performed." }]
};
response.Summary.CacheStatus = WorkerCacheStatus.Disabled;
response.Summary.Versions.WorkerVersion = "tool-consumer-fixture";
response.Summary.Versions.ApiSpecVersion = "tool-consumer-fixture";
response.Summary.Versions.WorkerBinarySha256 = new string('a', 64);
response.Summary.Versions.ApiSpecContentSha256 = new string('b', 64);
WorkerProtocolJson.SealManifest(response.Manifest);
if (!WorkerProtocolJson.Validate(response).IsValid)
    throw new InvalidDataException("The stored reader fixture violates the protocol.");
File.WriteAllText(args[0], WorkerProtocolJson.SerializeResponse(response));
'@)
    [void](Invoke-ToolCheck @('restore', $fixtureProject, '--configfile', $fixtureConfiguration))
    [void](Invoke-ToolCheck @('run', '--project', $fixtureProject, '--configuration', 'Release', '--no-restore', '--',
            (Join-Path $scratch 'stored-result.json')))
    $explanation = (Invoke-ToolCheck @('sharpproof', 'explain', 'stored-result.json', '--json')) | ConvertFrom-Json
    if ($explanation.scope -notmatch '^Stored evidence only' -or $explanation.runStatus -cne 'Failed' -or $explanation.errors.Count -ne 1) {
        throw 'Installed explain lost the stored evidence boundary or recorded failure.'
    }
    [IO.File]::WriteAllText((Join-Path $scratch 'invalid-result.json'), '{}')
    $invalid = @(& dotnet sharpproof explain invalid-result.json 2>&1)
    if ($LASTEXITCODE -ne 2 -or ($invalid -join "`n") -notmatch 'SharpProof:') { throw 'Installed tool accepted malformed evidence.' }
    $manifests = @(
        foreach ($relativeManifest in @('dotnet-tools.json', '.config/dotnet-tools.json')) {
            $manifestPath = Join-Path $scratch $relativeManifest
            if (Test-Path -LiteralPath $manifestPath -PathType Leaf) { $manifestPath }
        }
    )
    if ($manifests.Count -ne 1) { throw 'Tool acceptance requires exactly one local manifest in the isolated consumer.' }
    $manifest = Get-Content -LiteralPath $manifests[0] -Raw | ConvertFrom-Json -AsHashtable
    if (-not $manifest.ContainsKey('tools') -or $manifest.tools -isnot [Collections.IDictionary]) {
        throw 'Local tool manifest has no tool registrations.'
    }
    $toolKeys = @($manifest.tools.Keys | Where-Object { $_ -ieq 'SharpProof.Tool' })
    if ($manifest.tools.Count -ne 1 -or $toolKeys.Count -ne 1) { throw 'Local manifest must register only SharpProof.Tool.' }
    $registeredTool = $manifest.tools[$toolKeys[0]]
    if ($registeredTool.version -cne $version -or @($registeredTool.commands).Count -ne 1 -or
        $registeredTool.commands[0] -cne 'dotnet-sharpproof') {
        throw 'Local manifest does not pin the exact installed SharpProof.Tool version and command.'
    }
    Write-Host "Optional tool $version installed and executed from exact feed; consumer evidence: $scratch"
    # The rejected malformed-result subprocess is a passing negative control.
    $global:LASTEXITCODE = 0
}
finally { Pop-Location }
