[CmdletBinding()]
param(
    [ValidateSet('Representative', 'Full')][string]$Scope = 'Representative',
    [string]$OutputDirectory = 'artifacts/reproducibility',
    [ValidateRange(60, 7200)][int]$CommandTimeoutSeconds = 2100,
    [switch]$RequirePackageBytes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Run independent reproducibility builds in the canonical Linux amd64 container.'
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) { throw 'Reproducibility qualification requires Linux amd64.' }
if ($RequirePackageBytes -and $Scope -ne 'Full') { throw '-RequirePackageBytes requires -Scope Full.' }
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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
$clones = @((Join-Path $temporary 'a'), (Join-Path $temporary 'independent longer path/checkout-b'))
$sdk = [string](Get-Content (Join-Path $repository 'global.json') -Raw | ConvertFrom-Json).sdk.version
$report = [ordered]@{
    schemaVersion = 1; sourceCommit = $commit; scope = $Scope; sdkVersion = $sdk
    configuration = 'Release'; continuousIntegrationBuild = $true
    origin = 'https://github.com/alexyorke/SharpProof.git'
    toolchainCatalogSha256 = (Get-FileHash (Join-Path $repository 'eng/container/toolchain.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    workspacePaths = $clones; managedPayloadsEqual = $false; managedPayloadCount = 0
    packageBytesEqual = $null; archiveComparisons = @(); payloadMismatches = @(); runs = @()
    packageScope = 'SDK 10 archive container bytes are measured separately; managed payload equality does not imply ZIP equality.'
    nativeInputs = @()
}

function Get-NativeInputReceipt {
    if ([string]::IsNullOrWhiteSpace($env:SHARPPROOF_NATIVE_ROOT) -or -not [IO.Directory]::Exists($env:SHARPPROOF_NATIVE_ROOT)) {
        throw 'The canonical pinned native payload directory is unavailable.'
    }
    $nativeRoot = (Resolve-Path -LiteralPath $env:SHARPPROOF_NATIVE_ROOT).Path
    return @(
        Get-ChildItem -LiteralPath $nativeRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($nativeRoot, $_.FullName).Replace('\', '/')
                bytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )
}

function Invoke-ReproCommand([string]$Executable, [string[]]$Arguments, [string]$Directory, [string]$LogName) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
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
        $timedOut = -not $process.WaitForExit($CommandTimeoutSeconds * 1000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $output "$LogName.log"), $text, [Text.UTF8Encoding]::new($false))
        if ($timedOut) { throw "Reproducibility phase $LogName exceeded $CommandTimeoutSeconds seconds; inspect its local log." }
        if ($process.ExitCode -ne 0) { throw "Reproducibility phase $LogName failed with exit code $($process.ExitCode); inspect its local log." }
        return $text
    }
    finally { $process.Dispose() }
}

function Get-RepresentativePayloads([string]$Clone) {
    $hashes = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($project in @('SharpProof.Attributes', 'SharpProof.Worker.Protocol', 'SharpProof.Host')) {
        $directory = Join-Path $Clone "$project/bin/Release"
        foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($Clone, $file.FullName).Replace('\', '/')
            if ($file.Name -notlike 'SharpProof.*' -or $file.Extension -notin @('.dll', '.pdb') -or $relative -match '/ref(?:int)?/') { continue }
            $hashes.Add($relative, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
        }
    }
    if ($hashes.Count -eq 0) { throw 'The representative build produced no managed payload authority.' }
    return ,$hashes
}

function Get-PackedPayloads([string]$Clone) {
    $feed = Join-Path $Clone 'artifacts/container-packages'
    Import-Module (Join-Path $Clone 'scripts/SharpProof.PackageIdentity.psm1') -Force
    $files = @(Get-ChildItem -LiteralPath $feed -File | Where-Object Extension -in @('.nupkg', '.snupkg'))
    $identity = Get-SharpProofPackageIdentitySet -Files $files -RequireRepository
    if ($identity.RepositoryCommits.Count -ne 1 -or $identity.RepositoryCommits[0] -cne $commit) { throw 'A reproduction package does not name the exact source commit.' }
    $hashes = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
    $archives = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in $files | Sort-Object Name) {
        $archives.Add($file.Name, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
        $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            foreach ($entry in $archive.Entries) {
                $name = [IO.Path]::GetFileName($entry.FullName)
                if (-not $name.StartsWith('SharpProof.', [StringComparison]::Ordinal) -or [IO.Path]::GetExtension($name) -notin @('.dll', '.pdb')) { continue }
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                finally { $stream.Dispose() }
                $hashes.Add($file.Name + '/' + $entry.FullName, $hash)
            }
        }
        finally { $archive.Dispose() }
    }
    if ($hashes.Count -eq 0) { throw 'The packed reproduction produced no managed payload authority.' }
    return [pscustomobject]@{ Payloads = $hashes; Archives = $archives }
}

try {
    $report.nativeInputs = @(Get-NativeInputReceipt)
    if ($report.nativeInputs.Count -eq 0) { throw 'The canonical native payload input set is empty.' }
    $runs = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt 2; $index++) {
        $clone = $clones[$index]
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($clone)) | Out-Null
        & git -c core.autocrlf=false clone --quiet --no-hardlinks --no-checkout $repository $clone
        if ($LASTEXITCODE -ne 0) { throw 'Creating an independent local source clone failed.' }
        & git -C $clone remote set-url origin $report.origin
        if ($LASTEXITCODE -ne 0) { throw 'Setting the canonical Source Link origin failed.' }
        & git -C $clone -c core.autocrlf=false checkout --quiet --detach $commit
        if ($LASTEXITCODE -ne 0 -or (& git -C $clone rev-parse HEAD).Trim() -cne $commit) { throw 'The independent clone did not select the exact source commit.' }
        $runSdk = (Invoke-ReproCommand dotnet @('--version') $clone "run-$index-sdk").Trim()
        if ($runSdk -cne $sdk) { throw 'An independent workspace selected a different SDK.' }
        if ($Scope -eq 'Full') {
            $null = Invoke-ReproCommand pwsh @('-NoLogo', '-NoProfile', '-File',
                (Join-Path $clone 'scripts/Invoke-SharpProofContainer.ps1'), '-Command', 'pack', '-Configuration', 'Release') $clone "run-$index-full-pack"
            $packed = Get-PackedPayloads $clone
            $runs.Add([pscustomobject]@{ payloads = $packed.Payloads; archives = $packed.Archives })
        }
        else {
            foreach ($project in @('eng/DefensiveCodingAnalyzer/DefensiveCodingAnalyzer.csproj',
                    'SharpProof.Attributes/SharpProof.Attributes.csproj',
                    'SharpProof.Worker.Protocol/SharpProof.Worker.Protocol.csproj', 'SharpProof.Host/SharpProof.Host.csproj')) {
                $label = [IO.Path]::GetFileNameWithoutExtension($project)
                $null = Invoke-ReproCommand dotnet @('restore', $project, '--locked-mode', '-p:ContinuousIntegrationBuild=true') $clone "run-$index-$label-restore"
                $arguments = @('build', $project, '-c', 'Release', '--no-restore', '-p:ContinuousIntegrationBuild=true', ('-p:RepositoryCommit=' + $commit))
                if ($label -eq 'DefensiveCodingAnalyzer') { $arguments += '-p:RunAnalyzersDuringBuild=false' }
                $null = Invoke-ReproCommand dotnet $arguments $clone "run-$index-$label-build"
            }
            $null = Invoke-ReproCommand pwsh @('-NoLogo', '-NoProfile', '-File',
                (Join-Path $clone 'scripts/Export-SharpProofEmbeddedSymbols.ps1'), '-RepositoryRoot', $clone, '-Configuration', 'Release') $clone "run-$index-export-symbols"
            $runs.Add([pscustomobject]@{ payloads = (Get-RepresentativePayloads $clone); archives = $null })
        }
    }
    $mismatches = [Collections.Generic.List[string]]::new()
    $keys = @(@($runs[0].payloads.Keys) + @($runs[1].payloads.Keys) | Sort-Object -Unique)
    foreach ($key in $keys) {
        if (-not $runs[0].payloads.ContainsKey($key) -or -not $runs[1].payloads.ContainsKey($key) -or
            $runs[0].payloads[$key] -cne $runs[1].payloads[$key]) { $mismatches.Add($key) }
    }
    $report.runs = @($runs.ToArray())
    $report.managedPayloadCount = $keys.Count
    $report.payloadMismatches = @($mismatches.ToArray())
    $report.managedPayloadsEqual = $mismatches.Count -eq 0
    if ($Scope -eq 'Full') {
        $archiveKeys = @(@($runs[0].archives.Keys) + @($runs[1].archives.Keys) | Sort-Object -Unique)
        $comparisons = @($archiveKeys | ForEach-Object {
            $firstHash = if ($runs[0].archives.ContainsKey($_)) { $runs[0].archives[$_] } else { $null }
            $secondHash = if ($runs[1].archives.ContainsKey($_)) { $runs[1].archives[$_] } else { $null }
            [ordered]@{ file = $_; firstSha256 = $firstHash; secondSha256 = $secondHash; equal = $firstHash -ceq $secondHash }
        })
        $report.archiveComparisons = $comparisons
        $report.packageBytesEqual = @($comparisons | Where-Object { -not $_.equal }).Count -eq 0
    }
    $sourceHead = (& git -C $repository rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceHead -cne $commit) { throw 'The source checkout changed during the reproduction experiment.' }
    if (($report.nativeInputs | ConvertTo-Json -Depth 4 -Compress) -cne ((Get-NativeInputReceipt) | ConvertTo-Json -Depth 4 -Compress)) { throw 'The native payload inputs changed during the reproduction experiment.' }
    if (-not $report.managedPayloadsEqual) { throw "Managed payload byte identity failed for $($mismatches.Count) entries; first differences: $(($mismatches | Select-Object -First 10) -join ', ')." }
    if ($RequirePackageBytes -and -not $report.packageBytesEqual) { throw 'Managed payloads match, but raw package ZIP bytes differ. The SDK 10 container result must not be called byte-reproducible.' }
    Write-Host "Independent path reproduction passed: $($keys.Count) managed DLL/PDB entries; scope=$Scope; raw package ZIP equality=$($report.packageBytesEqual)."
}
finally {
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'report.json') -Encoding utf8NoBOM
    $fullTemporary = [IO.Path]::GetFullPath($temporary)
    if (-not $fullTemporary.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($fullTemporary) -notlike 'sharpproof-repro-*') { throw 'Refusing to remove an unexpected reproducibility workspace.' }
    Remove-Item -LiteralPath $fullTemporary -Recurse -Force
}
