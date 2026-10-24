[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AuthorityPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReleaseConfiguration.psm1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1')
Assert-SharpProofContainer 'Export the Windows installed-tool coverage input before canonical coverage collection.'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$OutputDirectory = Resolve-SharpProofContainedPath -Root $repositoryRoot -Path $OutputDirectory -ParameterName 'OutputDirectory'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Windows coverage input must be a new directory.' }
$authority = Get-Content -LiteralPath $AuthorityPath -Raw | ConvertFrom-Json
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -cnotmatch '\A[a-f0-9]{40}\z' -or
    $authority.commit -cne $sourceCommit -or $authority.configuration -cne 'Release' -or
    $env:GITHUB_RUN_ID -cnotmatch '\A[1-9][0-9]*\z' -or
    $env:GITHUB_RUN_ATTEMPT -cnotmatch '\A[1-9][0-9]*\z') {
    throw 'Windows coverage input requires exact canonical source, run, attempt, and Release authority.'
}
$modules = @($authority.modules | Where-Object { $_.project -ceq 'SharpProof.Tool' -and $_.assemblyName -ceq 'SharpProof.Tool' })
if ($modules.Count -ne 1) { throw 'Windows coverage requires exactly one authoritative Tool module.' }
$module = $modules[0]
$assemblyPath = Resolve-SharpProofContainedPath -Root $repositoryRoot -Path $module.assemblyPath -ParameterName 'AssemblyPath'
$pdbPath = Resolve-SharpProofContainedPath -Root $repositoryRoot -Path $module.pdbPath -ParameterName 'PdbPath'
$assemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
$pdbHash = (Get-FileHash -LiteralPath $pdbPath -Algorithm SHA256).Hash.ToLowerInvariant()
$version = Get-SharpProofEffectivePackageVersion -RepositoryRoot $repositoryRoot
if ($version -cne "0.0.0-ci.$($env:GITHUB_RUN_ID).$($env:GITHUB_RUN_ATTEMPT)") {
    throw 'Windows coverage input must use this coverage producer run and attempt package version.'
}
[xml]$versions = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/Versions.props') -Raw
$collectorVersion = [string]$versions.Project.PropertyGroup.DotNetCoverageToolVersion
if ($collectorVersion -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') { throw 'The coverage collector must have an exact version pin.' }
$feed = Join-Path $OutputDirectory 'feed'
[void][IO.Directory]::CreateDirectory($feed)
Copy-Item -LiteralPath $AuthorityPath -Destination (Join-Path $OutputDirectory 'coverage-authority.json')
Copy-Item -LiteralPath $assemblyPath -Destination (Join-Path $OutputDirectory 'SharpProof.Tool.dll')
Copy-Item -LiteralPath $pdbPath -Destination (Join-Path $OutputDirectory 'SharpProof.Tool.pdb')
# Pack the already-built authority bytes. Rebuilding here would invalidate the
# independent Linux module identity that the native supplement must retain.
& dotnet pack (Join-Path $repositoryRoot 'src/SharpProof.Tool/SharpProof.Tool.csproj') `
    --configuration Release --no-build --no-restore --output $feed `
    -p:SharpProofPackTool=true -p:ContinuousIntegrationBuild=true `
    "-p:RepositoryCommit=$sourceCommit" "-p:SourceRevisionId=$sourceCommit"
if ($LASTEXITCODE -ne 0) { throw 'Packing the existing coverage Tool failed.' }
$packages = @(Get-ChildItem -LiteralPath $feed -Filter 'SharpProof.Tool.*.nupkg' -File)
if ($packages.Count -ne 1) { throw 'Expected one exact Tool archive.' }
$package = $packages[0]
$identity = Get-SharpProofPackageIdentity -Path $package.FullName -RequireRepository -RequireSingleIdentity
if ($identity.Id -cne 'SharpProof.Tool' -or $identity.Version -cne $version -or $identity.RepositoryCommit -cne $sourceCommit) {
    throw 'Coverage Tool package identity differs from the canonical producer.'
}
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'tools/net10.0/any/SharpProof.Tool.dll' })
    if ($entries.Count -ne 1) { throw 'Coverage Tool archive has an unexpected executable payload.' }
    $stream = $entries[0].Open()
    try { $packedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
    finally { $stream.Dispose() }
}
finally { $archive.Dispose() }
if ($packedHash -cne $assemblyHash -or
    (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $assemblyHash -or
    (Get-FileHash -LiteralPath $pdbPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pdbHash) {
    throw 'Packing changed or substituted the authoritative Tool bytes.'
}
$collectorName = "dotnet-coverage.$collectorVersion.nupkg"
$collectorPath = Join-Path $feed $collectorName
Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/dotnet-coverage/$collectorVersion/$collectorName" -OutFile $collectorPath
$collectorIdentity = Get-SharpProofPackageIdentity -Path $collectorPath -RequireSingleIdentity
if ($collectorIdentity.Id -cne 'dotnet-coverage' -or $collectorIdentity.Version -cne $collectorVersion) {
    throw 'The pinned Microsoft coverage collector archive has unexpected identity.'
}
function Get-InputFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [ordered]@{ path = $Path; sha256 = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $Path) -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$receipt = [ordered]@{
    schemaVersion   = 1
    kind            = 'sharpproof-windows-tool-coverage-input'
    sourceCommit    = $sourceCommit
    runId           = $env:GITHUB_RUN_ID
    producerAttempt = $env:GITHUB_RUN_ATTEMPT
    packageVersion  = $version
    phase           = 'semantic-extras'
    module          = [ordered]@{ project = $module.project; assemblyName = $module.assemblyName; moduleMvid = $module.moduleMvid; pdbCodeViewGuid = $module.pdbCodeViewGuid }
    files           = [ordered]@{
        authority = Get-InputFile 'coverage-authority.json'
        package   = Get-InputFile ('feed/' + $package.Name)
        assembly  = Get-InputFile 'SharpProof.Tool.dll'
        pdb       = Get-InputFile 'SharpProof.Tool.pdb'
        collector = Get-InputFile ('feed/' + $collectorName)
    }
    collector       = [ordered]@{ id = 'dotnet-coverage'; version = $collectorVersion }
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'producer.json'), ($receipt | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
Write-Host 'Sealed the unmodified canonical Tool package, symbols, authority, and pinned coverage collector for native Windows consumption.'
