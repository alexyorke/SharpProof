[CmdletBinding()]
param(
    [ValidateSet('Representative', 'Full')][string]$Scope = 'Representative',
    [string]$OutputDirectory = 'artifacts/reproducibility',
    [ValidateRange(60, 7200)][int]$CommandTimeoutSeconds = 2100,
    [switch]$RequirePackageBytes,
    [switch]$IncludeDeveloperPackages,
    [string]$DeveloperConsumerImage = '',
    [ValidateSet('Sequential', 'Build', 'Compare')][string]$ExecutionMode = 'Sequential',
    [ValidateRange(-1, 1)][int]$RunIndex = -1,
    [string]$WorkflowRunId = '',
    [ValidateRange(0, 2147483647)][int]$WorkflowRunAttempt = 0,
    [string]$FirstRunDirectory = '',
    [string]$SecondRunDirectory = '',
    [ValidateRange(0, 2147483647)][int]$FirstRunAttempt = 0,
    [ValidateRange(0, 2147483647)][int]$SecondRunAttempt = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$reproductionTimeoutSeconds = $CommandTimeoutSeconds
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReleaseConfiguration.psm1')
Assert-SharpProofContainer 'Run independent reproducibility builds in the canonical Linux amd64 container.'
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) { throw 'Reproducibility qualification requires Linux amd64.' }
if ($RequirePackageBytes -and $Scope -ne 'Full') { throw '-RequirePackageBytes requires -Scope Full.' }
if ($IncludeDeveloperPackages -and ($Scope -ne 'Full' -or $DeveloperConsumerImage -cnotmatch '\Aghcr\.io/alexyorke/sharpproof(?:-preview)?@sha256:[a-f0-9]{64}\z')) {
    throw 'Optional package reproduction requires Full scope and an explicit fixture consumer-image digest.'
}
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReproductionEvidence.psm1') -Force
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$executionPlan = Get-SharpProofReproductionExecutionPlan -ExecutionMode $ExecutionMode -Scope $Scope `
    -IncludeDeveloperPackages:$IncludeDeveloperPackages -RunIndex $RunIndex -WorkflowRunId $WorkflowRunId `
    -WorkflowRunAttempt $WorkflowRunAttempt -FirstRunDirectory $FirstRunDirectory -SecondRunDirectory $SecondRunDirectory `
    -FirstRunAttempt $FirstRunAttempt -SecondRunAttempt $SecondRunAttempt
$FirstRunAttempt = $executionPlan.FirstRunAttempt
$SecondRunAttempt = $executionPlan.SecondRunAttempt
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Cannot identify the exact source commit.' }
$dirty = (& git -C $repository status --porcelain --untracked-files=all | Out-String)
if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace($dirty)) { throw 'Reproducibility requires a clean exact-commit checkout; commit source changes first.' }
$artifacts = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory)) }
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { throw 'Reproducibility output must be a child of this checkout artifacts directory.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$temporary = Join-Path $parent ('sharpproof-repro-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$clones = [Collections.Immutable.ImmutableArray]::Create[string]((Join-Path $temporary 'a'), (Join-Path $temporary 'independent longer path/checkout-b'))
$sdk = [string](Get-Content (Join-Path $repository 'global.json') -Raw | ConvertFrom-Json).sdk.version
$packageVersion = Get-SharpProofEffectivePackageVersion -RepositoryRoot $repository
$sourceTree = (& git -C $repository rev-parse 'HEAD^{tree}').Trim()
if ($LASTEXITCODE -ne 0 -or $sourceTree -cnotmatch '\A[a-f0-9]{40}\z') { throw 'Cannot identify the exact source tree.' }
$report = [ordered]@{
    schemaVersion = 1; sourceCommit = $commit; scope = $Scope; sdkVersion = $sdk
    configuration = 'Release'; continuousIntegrationBuild = $true
    origin                      = 'https://github.com/alexyorke/SharpProof.git'
    toolchainCatalogSha256      = (Get-FileHash (Join-Path $repository 'eng/container/toolchain.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    workspacePaths = $clones; managedPayloadsEqual = $false; managedPayloadCount = 0
    packageBytesEqual = $null; archiveComparisons = [Collections.Immutable.ImmutableArray[object]]::Empty; payloadMismatches = [Collections.Immutable.ImmutableArray[string]]::Empty; runs = [Collections.Immutable.ImmutableArray[object]]::Empty
    packageScope                = 'SDK 10 archive container bytes are measured separately; managed payload equality does not imply ZIP equality.'
    nativeInputs                = [Collections.Immutable.ImmutableArray[object]]::Empty
    developerPackagesIncluded   = [bool]$IncludeDeveloperPackages
    developerConsumerImage      = if ($IncludeDeveloperPackages) { $DeveloperConsumerImage } else { $null }
    developerPayloadsEqual = $null; developerPayloadCount = 0; developerPackageBytesEqual = $null
    developerPayloadMismatches  = [Collections.Immutable.ImmutableArray[string]]::Empty
    developerArchiveComparisons = [Collections.Immutable.ImmutableArray[object]]::Empty
    failure                     = $null
    developerScope              = 'Separate optional Tool, CodeFixes, and Templates product payloads including nuspecs and dependencies; SDK ZIP relationship/core-property metadata excluded from payload comparison. Raw archive hashes are reported separately. The explicit image digest is a fixture serialization input, never published-image authority.'
}

function Get-NativeInputReceipt {
    if ([string]::IsNullOrWhiteSpace($env:SHARPPROOF_NATIVE_ROOT) -or -not [IO.Directory]::Exists($env:SHARPPROOF_NATIVE_ROOT)) {
        throw 'The canonical pinned native payload directory is unavailable.'
    }
    $nativeRoot = (Resolve-Path -LiteralPath $env:SHARPPROOF_NATIVE_ROOT).Path
    $receipts = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    Get-ChildItem -LiteralPath $nativeRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [void]$receipts.Add(([ordered]@{
                    path   = [IO.Path]::GetRelativePath($nativeRoot, $_.FullName).Replace('\', '/')
                    bytes  = $_.Length
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }).AsReadOnly())
    }
    return , ($receipts.ToImmutable())
}

function Invoke-ReproCommand([string]$Executable, [Nullable[Collections.Immutable.ImmutableArray[string]]]$Arguments, [string]$Directory, [string]$LogName) {
    if ($null -eq $Arguments -or $Arguments.IsDefault) {
        $argumentSnapshot = [Collections.Immutable.ImmutableArray[string]]::Empty
    }
    else {
        $argumentSnapshot = $Arguments
    }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $argumentSnapshot) { [void]$start.ArgumentList.Add($argument) }
    $start.Environment['GITHUB_ACTIONS'] = 'true'
    $start.Environment['GITHUB_SHA'] = $commit
    $start.Environment['GITHUB_REPOSITORY'] = 'alexyorke/SharpProof'
    $start.Environment['GITHUB_WORKSPACE'] = $Directory
    $start.Environment['SHARPPROOF_REPO_ROOT'] = $Directory
    foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_COMMON_DIR', 'SHARPPROOF_GOLDEN_SOURCE_ROOT')) { [void]$start.Environment.Remove($name) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        Write-Host "Reproducibility phase: $LogName"
        if (-not $process.Start()) { throw "Cannot start reproducibility phase $LogName." }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($reproductionTimeoutSeconds * 1000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $diagnosticText = Get-SharpProofReproductionDiagnosticText -Text $text
        [IO.File]::WriteAllText((Join-Path $output "$LogName.log"), $diagnosticText, [Text.UTF8Encoding]::new($false))
        if ($timedOut -or $process.ExitCode -ne 0) {
            $report.failure = ([ordered]@{
                    phase = $LogName; exitCode = $process.ExitCode; timedOut = $timedOut; logFile = "$LogName.log"
                }).AsReadOnly()
            $summary = Get-SharpProofReproductionDiagnosticText -Text $diagnosticText -Tail
            if ($timedOut) {
                throw "Reproducibility phase $LogName exceeded $reproductionTimeoutSeconds seconds; inspect its phase log. Diagnostic tail:`n$summary"
            }
            throw "Reproducibility phase $LogName failed with exit code $($process.ExitCode); inspect its phase log. Diagnostic tail:`n$summary"
        }
        return $text
    }
    finally { $process.Dispose() }
}

function Get-RepresentativePayloads([string]$Clone) {
    $hashes = [Collections.Immutable.ImmutableSortedDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
    foreach ($project in @('SharpProof.Attributes', 'SharpProof.Worker.Protocol', 'SharpProof.Host')) {
        $directory = Join-Path $Clone "artifacts/bin/$project/Release"
        foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($Clone, $file.FullName).Replace('\', '/')
            if ($file.Name -notlike 'SharpProof.*' -or $file.Extension -notin @('.dll', '.pdb') -or $relative -match '/ref(?:int)?/') { continue }
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hashes.ContainsKey($relative)) { throw [ArgumentException]::new('An item with the same key has already been added. Key: ' + ([Collections.Generic.KeyValuePair[string, string]]::new($relative, $hash)).ToString()) }
            $hashes.Add($relative, $hash)
        }
    }
    if ($hashes.Count -eq 0) { throw 'The representative build produced no managed payload authority.' }
    return , ($hashes.ToImmutable())
}

function Get-PackedPayloads([string]$Clone) {
    $feed = Join-Path $Clone 'artifacts/container-packages'
    Import-Module (Join-Path $Clone 'eng/scripts/SharpProof.PackageIdentity.psm1') -Force
    $fileBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[IO.FileInfo]()
    Get-ChildItem -LiteralPath $feed -File | Where-Object Extension -In @('.nupkg', '.snupkg') | ForEach-Object { [void]$fileBuilder.Add($_) }
    $files = $fileBuilder.ToImmutable()
    $identity = Get-SharpProofPackageIdentitySet -Files ([object[]]@($files)) -RequireRepository
    if ($identity.RepositoryCommits.Count -ne 1 -or $identity.RepositoryCommits[0] -cne $commit) { throw 'A reproduction package does not name the exact source commit.' }
    $hashes = [Collections.Immutable.ImmutableSortedDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
    $archives = [Collections.Immutable.ImmutableSortedDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
    foreach ($file in $files | Sort-Object Name) {
        $archiveHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($archives.ContainsKey($file.Name)) { throw [ArgumentException]::new('An item with the same key has already been added. Key: ' + ([Collections.Generic.KeyValuePair[string, string]]::new($file.Name, $archiveHash)).ToString()) }
        $archives.Add($file.Name, $archiveHash)
        $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            foreach ($entry in $archive.Entries) {
                $name = [IO.Path]::GetFileName($entry.FullName)
                if (-not $name.StartsWith('SharpProof.', [StringComparison]::Ordinal) -or [IO.Path]::GetExtension($name) -notin @('.dll', '.pdb')) { continue }
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                finally { $stream.Dispose() }
                $key = $file.Name + '/' + $entry.FullName
                if ($hashes.ContainsKey($key)) { throw [ArgumentException]::new('An item with the same key has already been added. Key: ' + ([Collections.Generic.KeyValuePair[string, string]]::new($key, $hash)).ToString()) }
                $hashes.Add($key, $hash)
            }
        }
        finally { $archive.Dispose() }
    }
    if ($hashes.Count -eq 0) { throw 'The packed reproduction produced no managed payload authority.' }
    return , ([ordered]@{ Payloads = $hashes.ToImmutable(); Archives = $archives.ToImmutable() }).AsReadOnly()
}

function Assert-ReproductionClone([string]$Clone, [switch]$Fresh) {
    $cloneHead = (& git -C $Clone rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $cloneHead -cne $commit) { throw 'An independent clone changed its exact source commit.' }
    $common = (& git -C $Clone rev-parse --path-format=absolute --git-common-dir).Trim()
    if ($LASTEXITCODE -ne 0 -or $common -cne ($Clone + '/.git')) { throw 'An independent clone shares Git metadata outside its private checkout.' }
    if ([IO.File]::Exists((Join-Path $common 'objects/info/alternates'))) { throw 'An independent clone still borrows Git object storage.' }
    $cloneDirty = (& git -C $Clone status --porcelain --untracked-files=all | Out-String)
    if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace($cloneDirty)) { throw 'An independent clone changed its source inputs.' }
    if ($Fresh -and ([IO.Directory]::Exists((Join-Path $Clone 'artifacts/bin')) -or [IO.Directory]::Exists((Join-Path $Clone 'artifacts/obj')))) {
        throw 'An independent clone already contains compiler output.'
    }
    return $common
}

try {
    $report.nativeInputs = Get-NativeInputReceipt
    if ($report.nativeInputs.Length -eq 0) { throw 'The canonical native payload input set is empty.' }
    $runs = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $developerRuns = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($index in $executionPlan.Indices) {
        $clone = $clones[$index]
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($clone)) | Out-Null
        $cloneArguments = Get-SharpProofReproductionCloneArguments -Repository $repository -Clone $clone -Isolated:($ExecutionMode -eq 'Build')
        [string[]]$nativeCloneArguments = @($cloneArguments)
        & git @nativeCloneArguments
        if ($LASTEXITCODE -ne 0) { throw 'Creating an independent local source clone failed.' }
        & git -C $clone remote set-url origin $report.origin
        if ($LASTEXITCODE -ne 0) { throw 'Setting the canonical Source Link origin failed.' }
        & git -C $clone -c core.autocrlf=false checkout --quiet --detach $commit
        if ($LASTEXITCODE -ne 0 -or (& git -C $clone rev-parse HEAD).Trim() -cne $commit) { throw 'The independent clone did not select the exact source commit.' }
        if ($ExecutionMode -eq 'Build') { $cloneCommon = Assert-ReproductionClone -Clone $clone -Fresh }
        $runSdk = (Invoke-ReproCommand dotnet ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('--version'))) $clone "run-$index-sdk").Trim()
        if ($runSdk -cne $sdk) { throw 'An independent workspace selected a different SDK.' }
        if ($Scope -eq 'Full') {
            $null = Invoke-ReproCommand pwsh ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('-NoLogo', '-NoProfile', '-File',
                        (Join-Path $clone 'eng/scripts/Invoke-SharpProofContainer.ps1'), '-Command', 'pack', '-Configuration', 'Release'))) $clone "run-$index-full-pack"
            $packed = Get-PackedPayloads $clone
            $runs.Add(([ordered]@{ payloads = $packed.Payloads; archives = $packed.Archives }).AsReadOnly())
            if ($IncludeDeveloperPackages) {
                $developerFeed = Join-Path $clone 'artifacts/developer-reproduction-packages'
                foreach ($name in @('Tool', 'CodeFixes', 'Templates')) {
                    $developerPlan = Get-SharpProofDeveloperPackageBuildPlan -Name $name -OutputDirectory $developerFeed `
                        -RepositoryCommit $commit -ConsumerImage $DeveloperConsumerImage
                    foreach ($phase in $developerPlan) {
                        $null = Invoke-ReproCommand dotnet $phase.Arguments $clone "run-$index-developer-$name-$($phase.Phase)"
                    }
                }
                $developerRuns.Add((Get-SharpProofDeveloperPackageSnapshot -PackageSource $developerFeed -PackageVersion $packageVersion -RepositoryCommit $commit))
            }
        }
        else {
            foreach ($project in @('src/DefensiveCodingAnalyzer/DefensiveCodingAnalyzer.csproj',
                    'src/SharpProof.Attributes/SharpProof.Attributes.csproj',
                    'src/SharpProof.Worker.Protocol/SharpProof.Worker.Protocol.csproj', 'src/SharpProof.Host/SharpProof.Host.csproj')) {
                $label = [IO.Path]::GetFileNameWithoutExtension($project)
                $null = Invoke-ReproCommand dotnet ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('restore', $project, '--locked-mode', '-p:ContinuousIntegrationBuild=true'))) $clone "run-$index-$label-restore"
                $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
                [void]$argumentBuilder.AddRange([string[]]@('build', $project, '-c', 'Release', '--no-restore', '-p:ContinuousIntegrationBuild=true', ('-p:RepositoryCommit=' + $commit)))
                if ($label -eq 'DefensiveCodingAnalyzer') { [void]$argumentBuilder.Add('-p:RunAnalyzersDuringBuild=false') }
                $arguments = $argumentBuilder.ToImmutable()
                $null = Invoke-ReproCommand dotnet $arguments $clone "run-$index-$label-build"
            }
            $null = Invoke-ReproCommand pwsh ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('-NoLogo', '-NoProfile', '-File',
                        (Join-Path $clone 'eng/scripts/Export-SharpProofEmbeddedSymbols.ps1'), '-RepositoryRoot', $clone, '-Configuration', 'Release'))) $clone "run-$index-export-symbols"
            $runs.Add(([ordered]@{ payloads = (Get-RepresentativePayloads $clone); archives = $null }).AsReadOnly())
        }
        if ($ExecutionMode -eq 'Build') {
            if ((Assert-ReproductionClone -Clone $clone) -cne $cloneCommon) { throw 'The independent clone changed its Git isolation.' }
            $nativeAfter = Get-NativeInputReceipt
            $harness = Join-Path $clone 'artifacts/consumer-harness'
            $harnessReceipt = Join-Path $harness 'receipt.json'
            $harnessFiles = @(Get-ChildItem -LiteralPath (Join-Path $harness 'payload') -Recurse -File)
            $logHashes = [ordered]@{}
            foreach ($name in (Get-SharpProofReproductionLogNames -RunIndex $index)) {
                $logHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            $receipt = ([ordered]@{
                    schemaVersion = 1; runIndex = $index; workflowRunId = $WorkflowRunId; workflowRunAttempt = $WorkflowRunAttempt
                    sourceCommit = $commit; sourceTree = $sourceTree; scope = 'Full'; sdkVersion = $sdk; configuration = 'Release'
                    continuousIntegrationBuild = $true; origin = $report.origin; toolchainCatalogSha256 = $report.toolchainCatalogSha256
                    packageVersion = $packageVersion; developerConsumerImage = $DeveloperConsumerImage; workspacePath = $clone
                    cloneRelativePath = $(if ($index -eq 0) { 'a' } else { 'independent longer path/checkout-b' })
                    gitCommonDirectory = $cloneCommon; freshClone = $true; analyzersEnabled = $true
                    core              = $runs[0]
                    developer         = ([ordered]@{ payloads = $developerRuns[0].Payloads; archives = $developerRuns[0].Archives }).AsReadOnly()
                    nativeInputsBefore = $report.nativeInputs; nativeInputsAfter = $nativeAfter
                    consumerHarness   = ([ordered]@{
                            receiptSha256 = (Get-FileHash -LiteralPath $harnessReceipt -Algorithm SHA256).Hash.ToLowerInvariant()
                            payloadFileCount = $harnessFiles.Count; payloadBytes = [long]($harnessFiles | Measure-Object -Property Length -Sum).Sum
                        }).AsReadOnly()
                    logs              = $logHashes.AsReadOnly()
                }).AsReadOnly()
            $receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output "run-$index-receipt.json") -Encoding utf8NoBOM
            $null = Read-SharpProofReproductionRunEvidence -Directory $output -RunIndex $index -SourceCommit $commit -SourceTree $sourceTree `
                -WorkflowRunId $WorkflowRunId -ProducerAttempt $WorkflowRunAttempt -SdkVersion $sdk `
                -ToolchainCatalogSha256 $report.toolchainCatalogSha256 -PackageVersion $packageVersion `
                -DeveloperConsumerImage $DeveloperConsumerImage -NativeInputs $nativeAfter
        }
    }
    if ($ExecutionMode -eq 'Build') {
        $sourceHead = (& git -C $repository rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $sourceHead -cne $commit) { throw 'The source checkout changed during the reproduction experiment.' }
        $report.runs = $runs.ToImmutable()
        Write-Host "Independent reproduction run $RunIndex completed with all normal build, package, native, and harness checks."
        return
    }
    if ($ExecutionMode -eq 'Compare') {
        $directories = @($FirstRunDirectory, $SecondRunDirectory)
        $attempts = @($FirstRunAttempt, $SecondRunAttempt)
        $evidence = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt 2; $index++) {
            $inputDirectory = Resolve-SharpProofContainedPath -Root $repository -Path $directories[$index] -ParameterName 'Reproduction run input'
            $item = Read-SharpProofReproductionRunEvidence -Directory $inputDirectory -RunIndex $index -SourceCommit $commit -SourceTree $sourceTree `
                -WorkflowRunId $WorkflowRunId -ProducerAttempt $attempts[$index] -SdkVersion $sdk `
                -ToolchainCatalogSha256 $report.toolchainCatalogSha256 -PackageVersion $packageVersion `
                -DeveloperConsumerImage $DeveloperConsumerImage -NativeInputs $report.nativeInputs
            $evidence.Add($item)
            $runs.Add($item.Core)
            $developerRuns.Add($item.Developer)
        }
        if ($evidence[0].Receipt.workspacePath -ceq $evidence[1].Receipt.workspacePath -or
            $evidence[0].Receipt.gitCommonDirectory -ceq $evidence[1].Receipt.gitCommonDirectory) {
            throw 'Reproduction runs do not have distinct independent checkouts.'
        }
        foreach ($item in $evidence) { Copy-SharpProofReproductionRunLogs -Evidence $item -OutputDirectory $output }
        $report.workspacePaths = [Collections.Immutable.ImmutableArray]::Create[string](
            $evidence[0].Receipt.workspacePath, $evidence[1].Receipt.workspacePath)
        $report['workflowRunId'] = $WorkflowRunId
        $report['comparisonAttempt'] = $WorkflowRunAttempt
        $report['runEvidence'] = @($evidence | ForEach-Object {
                ([ordered]@{
                    runIndex = $_.Receipt.runIndex; producerAttempt = $_.Receipt.workflowRunAttempt; receiptSha256 = $_.ReceiptSha256
                    sourceTree = $_.Receipt.sourceTree; workspacePath = $_.Receipt.workspacePath; consumerHarness = $_.Receipt.consumerHarness
                }).AsReadOnly()
            })
    }
    $mismatches = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $keyBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    [Linq.Enumerable]::Concat[string]($runs[0].payloads.Keys, $runs[1].payloads.Keys) | Sort-Object -Unique | ForEach-Object { [void]$keyBuilder.Add($_) }
    $keys = $keyBuilder.ToImmutable()
    foreach ($key in $keys) {
        if (-not $runs[0].payloads.ContainsKey($key) -or -not $runs[1].payloads.ContainsKey($key) -or
            $runs[0].payloads[$key] -cne $runs[1].payloads[$key]) { $mismatches.Add($key) }
    }
    $report.runs = $runs.ToImmutable()
    $report.managedPayloadCount = $keys.Length
    $report.payloadMismatches = $mismatches.ToImmutable()
    $report.managedPayloadsEqual = $mismatches.Count -eq 0
    if ($Scope -eq 'Full') {
        $archiveKeyBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        [Linq.Enumerable]::Concat[string]($runs[0].archives.Keys, $runs[1].archives.Keys) | Sort-Object -Unique | ForEach-Object { [void]$archiveKeyBuilder.Add($_) }
        $archiveKeys = $archiveKeyBuilder.ToImmutable()
        $comparisonBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
        $unequalCount = 0
        foreach ($key in $archiveKeys) {
            $firstHash = if ($runs[0].archives.ContainsKey($key)) { $runs[0].archives[$key] } else { $null }
            $secondHash = if ($runs[1].archives.ContainsKey($key)) { $runs[1].archives[$key] } else { $null }
            $equal = $firstHash -ceq $secondHash
            [void]$comparisonBuilder.Add(([ordered]@{ file = $key; firstSha256 = $firstHash; secondSha256 = $secondHash; equal = $equal }).AsReadOnly())
            if (-not $equal) { $unequalCount++ }
        }
        $report.archiveComparisons = $comparisonBuilder.ToImmutable()
        $report.packageBytesEqual = $unequalCount -eq 0
    }
    if ($IncludeDeveloperPackages) {
        $developerComparison = Compare-SharpProofDeveloperPackageSnapshots -First $developerRuns[0] -Second $developerRuns[1]
        $report.developerPayloadsEqual = $developerComparison.PayloadsEqual
        $report.developerPayloadCount = $developerComparison.PayloadCount
        $report.developerPayloadMismatches = $developerComparison.PayloadMismatches
        $report.developerPackageBytesEqual = $developerComparison.PackageBytesEqual
        $report.developerArchiveComparisons = $developerComparison.ArchiveComparisons
    }
    $sourceHead = (& git -C $repository rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceHead -cne $commit) { throw 'The source checkout changed during the reproduction experiment.' }
    $nativeInputJson = $report.nativeInputs | ConvertTo-Json -Depth 4 -Compress
    $currentNativeInputs = Get-NativeInputReceipt
    if ($nativeInputJson -cne ($currentNativeInputs | ConvertTo-Json -Depth 4 -Compress)) { throw 'The native payload inputs changed during the reproduction experiment.' }
    if (-not $report.managedPayloadsEqual) { throw "Managed payload byte identity failed for $($mismatches.Count) entries; first differences: $(($mismatches | Select-Object -First 10) -join ', ')." }
    if ($RequirePackageBytes -and -not $report.packageBytesEqual) { throw 'Managed payloads match, but raw package ZIP bytes differ. The SDK 10 container result must not be called byte-reproducible.' }
    if ($IncludeDeveloperPackages -and -not $report.developerPayloadsEqual) {
        throw "Optional package payload byte identity failed for $($report.developerPayloadMismatches.Length) entries; first differences: $(($report.developerPayloadMismatches | Select-Object -First 10) -join ', ')."
    }
    if ($RequirePackageBytes -and $IncludeDeveloperPackages -and -not $report.developerPackageBytesEqual) {
        throw 'Optional product payloads match, but raw optional package ZIP bytes differ.'
    }
    Write-Host "Independent path reproduction passed: $($keys.Length) managed DLL/PDB entries; scope=$Scope; raw package ZIP equality=$($report.packageBytesEqual)."
    if ($IncludeDeveloperPackages) { Write-Host "Optional package reproduction: $($report.developerPayloadCount) product payloads; raw package ZIP equality=$($report.developerPackageBytesEqual); fixture image only." }
}
finally {
    $reportSnapshot = $report.AsReadOnly()
    $reportName = if ($ExecutionMode -eq 'Build') { "run-$RunIndex-diagnostics.json" } else { 'report.json' }
    $reportSnapshot | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output $reportName) -Encoding utf8NoBOM
    $fullTemporary = [IO.Path]::GetFullPath($temporary)
    if (-not $fullTemporary.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($fullTemporary) -notlike 'sharpproof-repro-*') { throw 'Refusing to remove an unexpected reproducibility workspace.' }
    Remove-Item -LiteralPath $fullTemporary -Recurse -Force
}
