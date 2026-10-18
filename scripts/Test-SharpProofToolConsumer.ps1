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
    Add-Type -Path $protocolPath
    $response = [SharpProof.Worker.Protocol.WorkerVerifyResponse]::new()
    $response.InputHash = 'a' * 64
    $response.RunStatus = 'Failed'
    $response.FailureReason = 'InputUnavailable'
    $response.Summary.CacheStatus = 'Disabled'
    $response.Summary.Versions.WorkerVersion = 'tool-consumer-fixture'
    $response.Summary.Versions.ApiSpecVersion = 'tool-consumer-fixture'
    $response.Summary.Versions.WorkerBinarySha256 = 'a' * 64
    $response.Summary.Versions.ApiSpecContentSha256 = 'b' * 64
    $failure = [SharpProof.Worker.Protocol.WorkerProtocolError]::new()
    $failure.Code = 'input.unavailable'
    $failure.Message = 'Stored reader fixture; no verification was performed.'
    $response.Errors = @($failure)
    [SharpProof.Worker.Protocol.WorkerProtocolJson]::SealManifest($response.Manifest)
    if (-not [SharpProof.Worker.Protocol.WorkerProtocolJson]::Validate($response).IsValid) { throw 'The stored reader fixture violates the protocol.' }
    [IO.File]::WriteAllText((Join-Path $scratch 'stored-result.json'), [SharpProof.Worker.Protocol.WorkerProtocolJson]::SerializeResponse($response))
    $explanation = (Invoke-ToolCheck @('sharpproof', 'explain', 'stored-result.json', '--json')) | ConvertFrom-Json
    if ($explanation.scope -notmatch '^Stored evidence only' -or $explanation.runStatus -cne 'Failed' -or $explanation.errors.Count -ne 1) {
        throw 'Installed explain lost the stored evidence boundary or recorded failure.'
    }
    [IO.File]::WriteAllText((Join-Path $scratch 'invalid-result.json'), '{}')
    $invalid = @(& dotnet sharpproof explain invalid-result.json 2>&1)
    if ($LASTEXITCODE -ne 2 -or ($invalid -join "`n") -notmatch 'SharpProof:') { throw 'Installed tool accepted malformed evidence.' }
    $executable = Join-Path $scratch '.config/dotnet-tools.json'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'Tool acceptance did not use a local manifest.' }
    Write-Host "Optional tool $version installed and executed from exact feed; consumer evidence: $scratch"
}
finally { Pop-Location }
