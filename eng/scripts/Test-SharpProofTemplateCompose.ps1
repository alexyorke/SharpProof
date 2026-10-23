[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidatePattern('^ghcr\.io/alexyorke/sharpproof@sha256:[a-f0-9]{64}$')][string]$ConsumerImage,
    [string]$LocalImage = '',
    [string]$BuildMetadataPath = ''
)
# Host orchestration only: every restore, build, and verification command runs
# through the generated canonical consumer service. The host needs Docker.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
if ([string]::IsNullOrWhiteSpace($LocalImage) -ne [string]::IsNullOrWhiteSpace($BuildMetadataPath)) {
    throw 'A local candidate requires both an explicit image tag and its buildx metadata file.'
}
if ($LocalImage -and $LocalImage -cnotmatch '^[a-z0-9][a-z0-9._/:-]*:[A-Za-z0-9_][A-Za-z0-9_.-]*$') {
    throw 'The local candidate must be an explicit tagged image reference.'
}
$evidenceRoot = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$retainedRoot = Join-Path $evidenceRoot 'consumer'
$feed = Join-Path $evidenceRoot 'packages'
if (-not (Test-Path -LiteralPath $retainedRoot -PathType Container) -or -not (Test-Path -LiteralPath $feed -PathType Container)) {
    throw 'Supply one developer-experience run containing retained consumer source and packages.'
}
$projects = @(Get-ChildItem -LiteralPath $retainedRoot -Filter '*.csproj' -File)
if ($projects.Count -ne 1) { throw 'Expected exactly one retained generated consumer project.' }
[xml]$project = Get-Content -LiteralPath $projects[0].FullName -Raw
$references = @($project.Project.ItemGroup.PackageReference)
if ($references.Count -ne 1 -or $references[0].Include -cne 'SharpProof' -or
    $project.Project.PropertyGroup.TargetFramework -cne 'net10.0' -or $project.Project.PropertyGroup.LangVersion -cne '13.0' -or
    $project.Project.PropertyGroup.SharpProofProfile -cne 'strict' -or $project.Project.PropertyGroup.SharpProofVerify -cne 'true') {
    throw 'The retained project is not the generated strict .NET 10/C# 13 consumer.'
}
$version = [string]$references[0].Version
$sourceCommit = $null
$packageHashes = [Collections.Generic.List[object]]::new()
foreach ($id in @('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier')) {
    $archivePath = Join-Path $feed "$id.$version.nupkg"
    $identity = Get-SharpProofPackageIdentity -Path $archivePath -RequireRepository -RequireSingleIdentity
    if ($identity.Id -cne $id -or $identity.Version -cne $version -or
        ($null -ne $sourceCommit -and $identity.RepositoryCommit -cne $sourceCommit)) {
        throw 'Consumer feed identity, version, or source commits do not match.'
    }
    $sourceCommit = $identity.RepositoryCommit
    $packageHashes.Add([ordered]@{ id = $id; fileName = [IO.Path]::GetFileName($archivePath); sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() })
}
$developerRunPath = Join-Path $evidenceRoot 'run.json'
$hasDeveloperRun = Test-Path -LiteralPath $developerRunPath -PathType Leaf
if ($hasDeveloperRun) {
    $developerRun = Get-Content -LiteralPath $developerRunPath -Raw | ConvertFrom-Json
    if ($developerRun.status -cne 'passed' -or $developerRun.sourceCommit -cne $sourceCommit -or
        $developerRun.packageVersion -cne $version -or $developerRun.consumerImage -cne $ConsumerImage) {
        throw 'The developer-experience run manifest does not qualify these inputs.'
    }
    foreach ($package in $packageHashes) {
        $recorded = @($developerRun.packages | Where-Object id -CEQ $package.id)
        if ($recorded.Count -ne 1 -or $recorded[0].sha256 -cne $package.sha256) { throw 'Consumer feed bytes changed after developer-experience qualification.' }
    }
}
$retainedCompose = Join-Path $retainedRoot 'compose.yaml'
$composeText = Get-Content -LiteralPath $retainedCompose -Raw
if (@([regex]::Matches($composeText, '(?m)^\s+image:\s*' + [regex]::Escape($ConsumerImage) + '\s*$')).Count -ne 1) {
    throw 'The generated Compose image does not match the exact candidate digest.'
}
$runId = [Guid]::NewGuid().ToString('N')
$output = Join-Path $evidenceRoot "compose-acceptance/$runId"
$consumerRoot = Join-Path $output 'consumer'
[void][IO.Directory]::CreateDirectory($consumerRoot)
$inputHashes = [Collections.Generic.List[object]]::new()
foreach ($name in @($projects[0].Name, 'Counter.cs', 'Directory.Build.props', 'global.json', 'compose.yaml')) {
    $inputPath = Join-Path $retainedRoot $name
    Copy-Item -LiteralPath $inputPath -Destination $consumerRoot
    $inputHashes.Add([ordered]@{ fileName = $name; sha256 = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() })
}
$status = 'failed'
$startedAt = [DateTimeOffset]::UtcNow.ToString('O')
$commands = [Collections.Generic.List[object]]::new()
$composeArguments = $null
$effectiveImage = $ConsumerImage
$imageMode = if ($LocalImage) { 'local-buildx-candidate' } else { 'registry-digest' }
$imageId = $null
$imageRevision = $null
$imageIdentityBinding = 'registry-digest-pull'
$localRepoDigest = $null
$buildMetadataImageName = $null
$buildMetadataHash = $null
$cleanupExit = $null
function Get-CanonicalRepository([string]$Repository) {
    $parts = $Repository.Split('/')
    if ($parts[0] -ceq 'docker.io') {
        if ($parts.Count -eq 2) { return 'docker.io/library/' + $parts[1] }
        return $Repository
    }
    if ($parts.Count -eq 1) { return 'docker.io/library/' + $Repository }
    if ($parts[0].Contains('.') -or $parts[0].Contains(':') -or $parts[0] -ceq 'localhost') { return $Repository }
    return 'docker.io/' + $Repository
}
function Get-CanonicalTag([string]$Reference) {
    $tagSeparator = $Reference.LastIndexOf(':')
    if ($tagSeparator -lt 1 -or $Reference.Contains('@')) { throw 'Buildx image names must identify explicit tagged images.' }
    return (Get-CanonicalRepository $Reference.Substring(0, $tagSeparator)) + $Reference.Substring($tagSeparator)
}
function Invoke-Docker([string]$Name, [string[]]$Arguments, [switch]$AllowFailure) {
    $log = Join-Path $output "$Name.log"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & docker @Arguments *> $log
    $observed = $LASTEXITCODE
    $timer.Stop()
    $commands.Add([ordered]@{ phase = $Name; exitCode = $observed; elapsedMilliseconds = $timer.ElapsedMilliseconds })
    if ($observed -ne 0 -and -not $AllowFailure) {
        Get-Content -LiteralPath $log -Tail 30 | Write-Host
        throw "Docker phase $Name failed with exit code $observed."
    }
    return [pscustomobject]@{ ExitCode = $observed; Output = [IO.File]::ReadAllText($log) }
}
try {
    [void](Invoke-Docker 'docker-version' @('version', '--format', '{{.Server.Os}}/{{.Server.Arch}}'))
    [void](Invoke-Docker 'compose-version' @('compose', 'version', '--short'))
    $expectedConfigDigest = $null
    if ($LocalImage) {
        $metadataPath = (Resolve-Path -LiteralPath $BuildMetadataPath).Path
        $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
        $manifestDigest = [string]$metadata.'containerimage.digest'
        if ($null -ne $metadata.PSObject.Properties['containerimage.config.digest']) {
            $expectedConfigDigest = [string]$metadata.'containerimage.config.digest'
            if ($expectedConfigDigest -cnotmatch '^sha256:[a-f0-9]{64}$') { throw 'Buildx metadata contains an invalid image config digest.' }
        }
        $buildMetadataImageName = [string]$metadata.'image.name'
        if ($manifestDigest -cnotmatch '^sha256:[a-f0-9]{64}$' -or $ConsumerImage -cne "ghcr.io/alexyorke/sharpproof@$manifestDigest" -or
            (Get-CanonicalTag $buildMetadataImageName) -cne (Get-CanonicalTag $LocalImage)) {
            throw 'Buildx metadata does not identify the exact candidate manifest and explicit local image tag.'
        }
        $buildMetadataHash = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $effectiveImage = $LocalImage
    }
    else { [void](Invoke-Docker 'pull-consumer' @('pull', '--platform', 'linux/amd64', $ConsumerImage)) }
    $inspectFormat = '{"Id":{{json .Id}},"RepoDigests":{{json .RepoDigests}},"Architecture":{{json .Architecture}},"Os":{{json .Os}},"Config":{"Labels":{{json .Config.Labels}},"User":{{json .Config.User}},"WorkingDir":{{json .Config.WorkingDir}},"Entrypoint":{{json .Config.Entrypoint}},"Cmd":{{json .Config.Cmd}}}}'
    $inspected = (Invoke-Docker 'inspect-consumer' @('image', 'inspect', $effectiveImage, '--format', $inspectFormat)).Output | ConvertFrom-Json
    $imageId = [string]$inspected.Id
    $revisionProperty = $inspected.Config.Labels.PSObject.Properties['org.opencontainers.image.revision']
    if ($null -ne $revisionProperty) { $imageRevision = [string]$revisionProperty.Value }
    if ($LocalImage) {
        if ($null -ne $expectedConfigDigest -and $imageId -ceq $expectedConfigDigest) {
            $imageIdentityBinding = 'buildx-config-digest'
        }
        else {
            $tagRepository = Get-CanonicalRepository $LocalImage.Substring(0, $LocalImage.LastIndexOf(':'))
            $matchingDigests = @($inspected.RepoDigests | Where-Object {
                    $segments = ([string]$_).Split('@')
                    $segments.Count -eq 2 -and $segments[1] -ceq $manifestDigest -and (Get-CanonicalRepository $segments[0]) -ceq $tagRepository
                })
            if ($matchingDigests.Count -eq 0) {
                throw 'The local image is not bound to the buildx config digest or the exact local repository manifest digest.'
            }
            $localRepoDigest = [string]$matchingDigests[0]
            $imageIdentityBinding = 'local-repository-manifest-digest'
        }
    }
    if ($inspected.Os -cne 'linux' -or $inspected.Architecture -cne 'amd64' -or
        $inspected.Config.Labels.'org.opencontainers.image.source' -cne 'https://github.com/alexyorke/SharpProof' -or
        $imageRevision -cne $sourceCommit -or
        $inspected.Config.Labels.'io.sharpproof.consumer.version' -cne $version -or $inspected.Config.Labels.'io.sharpproof.platform' -cne 'linux/amd64' -or
        $inspected.Config.WorkingDir -cne '/workspace/consumer' -or $inspected.Config.User -cne 'root' -or
        (@($inspected.Config.Entrypoint) -join '|') -cne '/usr/local/bin/sharpproof-consumer' -or (@($inspected.Config.Cmd) -join '|') -cne '--info') {
        throw 'The candidate image ID, platform, metadata, or consumer startup contract differs from qualification.'
    }
    $nugetPath = Join-Path $output 'NuGet.config'
    [IO.File]::WriteAllText($nugetPath, '<configuration><packageSources><clear/><add key="fixture" value="/workspace/sharpproof-feed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="fixture"><package pattern="SharpProof"/><package pattern="SharpProof.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>', [Text.UTF8Encoding]::new($false))
    $serviceOverride = [ordered]@{ volumes = @(
            [ordered]@{ type = 'bind'; source = $feed; target = '/workspace/sharpproof-feed'; read_only = $true },
            [ordered]@{ type = 'bind'; source = $nugetPath; target = '/workspace/sharpproof-config/NuGet.config'; read_only = $true }
        )
    }
    if ($LocalImage) { $serviceOverride['image'] = $LocalImage; $serviceOverride['pull_policy'] = 'never' }
    $overridePath = Join-Path $output 'compose.acceptance.json'
    [IO.File]::WriteAllText($overridePath, (ConvertTo-Json -InputObject ([ordered]@{ services = [ordered]@{ verify = $serviceOverride } }) -Depth 8), [Text.UTF8Encoding]::new($false))
    $projectName = 'sharpproof-template-' + $runId.Substring(0, 12)
    $composeArguments = @('compose', '--project-name', $projectName, '--project-directory', $consumerRoot,
        '--file', (Join-Path $consumerRoot 'compose.yaml'), '--file', $overridePath)
    $resolved = (Invoke-Docker 'compose-config' @($composeArguments + @('config', '--format', 'json'))).Output | ConvertFrom-Json
    $service = $resolved.services.verify
    if ($service.image -cne $effectiveImage -or $service.platform -cne 'linux/amd64' -or $service.working_dir -cne '/workspace/consumer') {
        throw 'The resolved generated service changed its image, platform, or working directory.'
    }
    foreach ($target in @('/workspace/consumer/.sharpproof-obj', '/workspace/consumer/.sharpproof-bin', '/home/sharpproof/.nuget/packages')) {
        $mount = @($service.volumes | Where-Object target -CEQ $target)
        if ($mount.Count -ne 1 -or $mount[0].type -cne 'volume') { throw 'Generated consumer intermediates and package caches must remain private named volumes.' }
    }
    [void](Invoke-Docker 'compose-restore' @($composeArguments + @('run', '--rm', '--no-deps', 'verify', 'restore', $projects[0].Name, '--configfile', '/workspace/sharpproof-config/NuGet.config')))
    [void](Invoke-Docker 'compose-build' @($composeArguments + @('run', '--rm', '--no-deps', 'verify', 'build', $projects[0].Name, '-c', 'Release', '--no-restore')))
    $reportRoot = Join-Path $consumerRoot 'artifacts/sharpproof'
    foreach ($name in @('result.json', 'request.json', 'compiler-manifest.json', 'result.sarif')) {
        if (-not (Test-Path -LiteralPath (Join-Path $reportRoot $name) -PathType Leaf)) { throw "The generated service did not export $name." }
    }
    $response = Get-Content -LiteralPath (Join-Path $reportRoot 'result.json') -Raw | ConvertFrom-Json
    if (@($response.claimResults).Count -eq 0 -or @($response.claimResults | Where-Object outcome -CNE 'Proven').Count -ne 0) {
        throw 'The generated Compose consumer did not prove every selected claim.'
    }
    foreach ($name in @('obj', 'bin', '.sharpproof-obj', '.sharpproof-bin')) {
        $hostOutput = Join-Path $consumerRoot $name
        if ((Test-Path -LiteralPath $hostOutput -PathType Container) -and @(Get-ChildItem -LiteralPath $hostOutput -Recurse -File -Force).Count -ne 0) {
            throw 'Consumer compiler outputs leaked from their private Docker volumes onto the host.'
        }
    }
    foreach ($inputFile in $inputHashes) {
        if ((Get-FileHash -LiteralPath (Join-Path $consumerRoot $inputFile.fileName) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputFile.sha256) {
            throw 'The generated Compose workflow changed a retained project input.'
        }
    }
    $status = 'passed'
}
finally {
    if ($null -ne $composeArguments) {
        $cleanup = Invoke-Docker 'compose-cleanup' @($composeArguments + @('down', '--volumes', '--remove-orphans')) -AllowFailure
        $cleanupExit = $cleanup.ExitCode
        if ($cleanupExit -ne 0) { $status = 'failed' }
    }
    $summary = [ordered]@{
        schemaVersion = 1; status = $status; sourceCommit = $sourceCommit; packageVersion = $version
        consumerImage = $ConsumerImage; imageMode = $imageMode; effectiveImage = $effectiveImage; imageId = $imageId
        imageRevision = $imageRevision
        imageIdentityBinding = $imageIdentityBinding; localRepoDigest = $localRepoDigest; buildMetadataImageName = $buildMetadataImageName
        buildMetadataSha256 = $buildMetadataHash; developerRunManifestPresent = $hasDeveloperRun
        startedAtUtc = $startedAt; finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); cleanupExitCode = $cleanupExit
        generatedInputs = @($inputHashes.ToArray()); packages = @($packageHashes.ToArray()); commands = @($commands.ToArray())
    }
    [IO.File]::WriteAllText((Join-Path $output 'compose-run.json'), (ConvertTo-Json -InputObject $summary -Depth 8), [Text.UTF8Encoding]::new($false))
    Write-Host "Generated Compose evidence: $output"
}
if ($status -cne 'passed') { throw 'Generated Compose acceptance or private-volume cleanup failed.' }
Write-Host 'Generated Compose restore, native build, exported proof, and volume isolation passed.'
