[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.NativeToolCoverage.psm1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1')
Assert-SharpProofContainer 'Run native Tool evidence regressions in the canonical container.'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-native-evidence-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporaryRoot)
$controlCount = 0
function Get-FixtureHash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-FixtureJson([string]$Path, $Value) { [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 40) + "`n"), [Text.UTF8Encoding]::new($false)) }
function Write-FixturePackage([string]$Path, [string]$Id, [string]$Version, [string]$Assembly = '', [string]$Pdb = '') {
    $stream = [IO.File]::Create($Path)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $zip.CreateEntry("$Id.nuspec")
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write("<package><metadata><id>$Id</id><version>$Version</version></metadata></package>") }
        finally { $writer.Dispose() }
        foreach ($item in @(@{ Path = $Assembly; Name = 'SharpProof.Tool.dll' }, @{ Path = $Pdb; Name = 'SharpProof.Tool.pdb' })) {
            if (-not [string]::IsNullOrEmpty($item.Path)) {
                $entry = $zip.CreateEntry('tools/net10.0/any/' + $item.Name)
                $inputStream = [IO.File]::OpenRead($item.Path); $output = $entry.Open()
                try { $inputStream.CopyTo($output) } finally { $output.Dispose(); $inputStream.Dispose() }
            }
        }
    }
    finally { $zip.Dispose(); $stream.Dispose() }
}
function Save-FixtureReceipts($Fixture) {
    Write-FixtureJson (Join-Path $Fixture.ProducerRoot 'producer.json') $Fixture.Producer
    $Fixture.Consumer.producerReceiptSha256 = Get-FixtureHash (Join-Path $Fixture.ProducerRoot 'producer.json')
    Write-FixtureJson (Join-Path $Fixture.ConsumerRoot 'consumer.json') $Fixture.Consumer
}
function Set-FixtureReport($Fixture, [string]$Text) {
    $path = Join-Path $Fixture.ConsumerRoot 'windows-tool.cobertura.xml'
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
    $Fixture.Consumer.files.report.sha256 = Get-FixtureHash $path
}
function Protect-FixtureArtifacts($Fixture) {
    foreach ($entry in @(@{ Id = '456'; Root = $Fixture.ProducerRoot; Name = 'input.zip' }, @{ Id = '789'; Root = $Fixture.ConsumerRoot; Name = 'output.zip' })) {
        $zip = Join-Path $Fixture.Root $entry.Name
        if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
        [IO.Compression.ZipFile]::CreateFromDirectory($entry.Root, $zip)
        $Fixture.Records[$entry.Id].digest = 'sha256:' + (Get-FixtureHash $zip)
    }
}
function New-Fixture([string]$Name) {
    $root = Join-Path $temporaryRoot $Name
    [void][IO.Directory]::CreateDirectory($root)
    Copy-Item -LiteralPath $baseline -Destination (Join-Path $root 'repository') -Recurse
    $repo = Join-Path $root 'repository'
    $producerRoot = Join-Path $repo 'producer'; $consumerRoot = Join-Path $repo 'consumer'
    $authority = Get-Content (Join-Path $producerRoot 'coverage-authority.json') -Raw | ConvertFrom-Json -AsHashtable
    $producer = Get-Content (Join-Path $producerRoot 'producer.json') -Raw | ConvertFrom-Json -AsHashtable
    $consumer = Get-Content (Join-Path $consumerRoot 'consumer.json') -Raw | ConvertFrom-Json -AsHashtable
    $records = @{
        '456' = @{ id = 456; name = "coverage-windows-tool-input-$sourceCommit-1"; expired = $false; workflow_run = @{ id = 123; head_sha = $workflowHead } }
        '789' = @{ id = 789; name = "coverage-windows-tool-output-$sourceCommit-2"; expired = $false; workflow_run = @{ id = 123; head_sha = $workflowHead } }
    }
    $fixture = @{ Root = $root; RepositoryRoot = $repo; ProducerRoot = $producerRoot; ConsumerRoot = $consumerRoot; Authority = $authority
        Producer = $producer; Consumer = $consumer; Records = $records; DestinationRoot = Join-Path $root 'staged'
    }
    Protect-FixtureArtifacts $fixture
    return $fixture
}
function Invoke-Fixture($Fixture) {
    $records = $Fixture.Records
    $lookup = { param([string]$Id) return $records[$Id] }.GetNewClosure()
    $root = $Fixture.Root
    $archiveLookup = { param([string]$Id) return (Join-Path $root $(if ($Id -ceq '456') { 'input.zip' } else { 'output.zip' })) }.GetNewClosure()
    return Import-SharpProofNativeToolCoverage -RepositoryRoot $Fixture.RepositoryRoot -ProducerRoot $Fixture.ProducerRoot `
        -ConsumerRoot $Fixture.ConsumerRoot -Authority $Fixture.Authority -DestinationRoot $Fixture.DestinationRoot `
        -RunId '123' -WorkflowHeadSha $workflowHead -ProducerArtifactId '456' -ProducerAttempt '1' `
        -ConsumerArtifactId '789' -ConsumerAttempt '2' -ArtifactLookup $lookup -ArtifactArchiveLookup $archiveLookup
}
function Assert-FixtureRejected([string]$Name, [scriptblock]$Mutation, [string]$Expected) {
    $fixture = New-Fixture $Name
    & $Mutation $fixture
    Save-FixtureReceipts $fixture
    $failure = $null
    try { $null = Invoke-Fixture $fixture } catch { $failure = $_.Exception.Message }
    if ([string]::IsNullOrEmpty($failure) -or -not $failure.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Control '$Name' expected '$Expected', got '$failure'."
    }
    if (Test-Path -LiteralPath $fixture.DestinationRoot) { throw "Rejected control '$Name' staged evidence." }
    $script:controlCount++
}
try {
    $baseline = Join-Path $temporaryRoot 'baseline'
    foreach ($relative in @('src/SharpProof.Tool', 'eng', 'artifacts/bin/SharpProof.Tool/Release/net10.0', 'producer/feed', 'consumer')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $baseline $relative))
    }
    Copy-Item (Join-Path $repositoryRoot 'eng/Versions.props') (Join-Path $baseline 'eng/Versions.props')
    [xml]$versions = Get-Content (Join-Path $baseline 'eng/Versions.props') -Raw
    $collectorPin = $versions.SelectSingleNode('/Project/PropertyGroup/DotNetCoverageToolVersion').InnerText
    $sourceCommit = 'a' * 40; $workflowHead = 'b' * 40; $version = '0.0.0-ci.123.1'
    # This synthetic PE/PDB tests byte and symbol authentication. It does not
    # claim Windows execution; only the real Windows job supplies that evidence.
    $source = @(
        'public enum AccessControlSections { Access = 2, Owner = 4 }',
        'public sealed class FileInfo {',
        ' public FileInfo(string path) { }',
        ' public int GetAccessControl(AccessControlSections section) { return 1; }',
        ' public int Create() { return 1; }',
        '}',
        'public static class Fixture {',
        ' public static int Run(string target, string temporary) {',
        '  var security = new FileInfo(target).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);',
        '  return new FileInfo(temporary).Create();',
        ' }',
        '}'
    ) -join "`n"
    $sourcePath = Join-Path $baseline 'src/SharpProof.Tool/SetupInitializerFiles.cs'
    [IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
    $assemblyRelative = 'artifacts/bin/SharpProof.Tool/Release/net10.0/SharpProof.Tool.dll'
    $pdbRelative = 'artifacts/bin/SharpProof.Tool/Release/net10.0/SharpProof.Tool.pdb'
    $assemblyPath = Join-Path $baseline $assemblyRelative; $pdbPath = Join-Path $baseline $pdbRelative
    $dotnetExecutable = [IO.FileInfo](Get-Command dotnet).Source
    $resolvedExecutable = $dotnetExecutable.ResolveLinkTarget($true)
    $dotnetRoot = if ($null -ne $resolvedExecutable) { $resolvedExecutable.DirectoryName } else { $dotnetExecutable.DirectoryName }
    $sdk = (Get-Content (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $csc = Join-Path $dotnetRoot "sdk/$sdk/Roslyn/bincore/csc.dll"
    & dotnet $csc /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug:portable /deterministic+ `
        "/r:$([object].Assembly.Location)" "/out:$assemblyPath" "/pdb:$pdbPath" $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'The isolated native evidence PE/PDB fixture did not compile.' }
    $module = Get-SharpProofNativeToolModuleIdentity $assemblyPath $pdbPath
    $moduleDescription = @{}; foreach ($key in $module.Keys) { $moduleDescription[$key] = $module[$key] }
    $authorityModule = @{}; foreach ($key in $module.Keys) { $authorityModule[$key] = $module[$key] }
    $authorityModule.assemblyPath = $assemblyRelative; $authorityModule.pdbPath = $pdbRelative
    $authorityModule.documents = @(@{ path = 'src/SharpProof.Tool/SetupInitializerFiles.cs'; sequencePoints = @(9, 10)
            sequencePointRanges = @(@{ startLine = 9; endLine = 9 }, @{ startLine = 10; endLine = 10 })
        })
    $authority = @{ schemaVersion = 1; commit = $sourceCommit; configuration = 'Release'; modules = @($authorityModule) }
    $producerRoot = Join-Path $baseline 'producer'; $consumerRoot = Join-Path $baseline 'consumer'
    Copy-Item $assemblyPath (Join-Path $producerRoot 'SharpProof.Tool.dll'); Copy-Item $pdbPath (Join-Path $producerRoot 'SharpProof.Tool.pdb')
    Write-FixtureJson (Join-Path $producerRoot 'coverage-authority.json') $authority
    Write-FixturePackage (Join-Path $producerRoot "feed/SharpProof.Tool.$version.nupkg") 'SharpProof.Tool' $version $assemblyPath $pdbPath
    Write-FixturePackage (Join-Path $producerRoot "feed/dotnet-coverage.$collectorPin.nupkg") 'dotnet-coverage' $collectorPin
    $producer = @{ schemaVersion = 1; kind = 'sharpproof-windows-tool-coverage-input'; sourceCommit = $sourceCommit; runId = '123'; producerAttempt = '1'
        packageVersion = $version; phase = 'semantic-extras'; module = $moduleDescription; files = @{}; collector = @{ id = 'dotnet-coverage'; version = $collectorPin }
    }
    foreach ($entry in @(@{ Key = 'authority'; Path = 'coverage-authority.json' }, @{ Key = 'assembly'; Path = 'SharpProof.Tool.dll' }, @{ Key = 'pdb'; Path = 'SharpProof.Tool.pdb' },
            @{ Key = 'package'; Path = "feed/SharpProof.Tool.$version.nupkg" }, @{ Key = 'collector'; Path = "feed/dotnet-coverage.$collectorPin.nupkg" })) {
        $producer.files[$entry.Key] = @{ path = $entry.Path; sha256 = Get-FixtureHash (Join-Path $producerRoot $entry.Path) }
    }
    $consumer = @{ schemaVersion = 1; kind = 'sharpproof-windows-tool-coverage'; sourceCommit = $sourceCommit; runId = '123'; runAttempt = '2'; producerAttempt = '1'
        producerArtifactId = '456'; packageVersion = $version; module = $moduleDescription; producerReceiptSha256 = ''; files = @{}
        checks  = @{ nativeWindows = $true; architecture = 'X64'; outsideCheckout = $true; installedAssemblySha256 = $producer.files.assembly.sha256
            installedPdbSha256 = $producer.files.pdb.sha256; assemblyUnmodified = $true; existingProject = $true; protectedDacl = $true
            daclPreserved = $true; ownerPreserved = $true; projectChanged = $true; initializerWritten = $true; noStagingFiles = $true
        }
        command = @{ tool = 'dotnet-sharpproof'; arguments = @('init', '--project', 'Consumer.csproj', '--verification', 'portable', '--apply', '--format', 'json') }
    }
    $report = '<coverage><packages><package name="SharpProof.Tool"><classes><class name="Fixture" filename="src/SharpProof.Tool/SetupInitializerFiles.cs"><lines><line number="9" hits="1"/><line number="10" hits="1"/></lines></class></classes></package></packages></coverage>'
    [IO.File]::WriteAllText((Join-Path $consumerRoot 'windows-tool.cobertura.xml'), $report)
    [IO.File]::WriteAllBytes((Join-Path $consumerRoot 'windows-tool.coverage'), [byte[]]@(1, 2, 3))
    foreach ($entry in @(@{ Key = 'report'; Path = 'windows-tool.cobertura.xml' }, @{ Key = 'binary'; Path = 'windows-tool.coverage' })) {
        $consumer.files[$entry.Key] = @{ path = $entry.Path; sha256 = Get-FixtureHash (Join-Path $consumerRoot $entry.Path) }
    }
    Save-FixtureReceipts @{ ProducerRoot = $producerRoot; ConsumerRoot = $consumerRoot; Producer = $producer; Consumer = $consumer }
    $positive = New-Fixture 'positive'
    $originalHash = Get-FixtureHash (Join-Path $positive.ConsumerRoot 'windows-tool.cobertura.xml')
    $staged = Invoke-Fixture $positive
    [xml]$stamped = Get-Content $staged -Raw
    if ($stamped.coverage.sharpProofAuthority.commit -cne $sourceCommit -or
        $originalHash -cne (Get-FixtureHash (Join-Path $positive.ConsumerRoot 'windows-tool.cobertura.xml')) -or
        -not (Test-Path (Join-Path $positive.DestinationRoot 'native-validation.json'))) { throw 'Valid evidence was not staged without changing its raw input.' }
    $controlCount++
    $optionalPdb = New-Fixture 'package-pdb-optional'
    Write-FixturePackage (Join-Path $optionalPdb.ProducerRoot $optionalPdb.Producer.files.package.path) 'SharpProof.Tool' $version `
    (Join-Path $optionalPdb.ProducerRoot 'SharpProof.Tool.dll')
    $optionalPdb.Producer.files.package.sha256 = Get-FixtureHash (Join-Path $optionalPdb.ProducerRoot $optionalPdb.Producer.files.package.path)
    Save-FixtureReceipts $optionalPdb
    Protect-FixtureArtifacts $optionalPdb
    $null = Invoke-Fixture $optionalPdb
    $controlCount++
    Assert-FixtureRejected 'wrong-artifact-id' { param($f) $f.Records['456'].id = 457 } 'immutable artifact origin'
    Assert-FixtureRejected 'wrong-artifact-name' { param($f) $f.Records['789'].name += '-other' } 'immutable artifact origin'
    Assert-FixtureRejected 'wrong-origin-head' { param($f) $f.Records['456'].workflow_run.head_sha = 'c' * 40 } 'immutable artifact origin'
    Assert-FixtureRejected 'wrong-origin-run' { param($f) $f.Records['789'].workflow_run.id = 124 } 'immutable artifact origin'
    Assert-FixtureRejected 'expired-artifact' { param($f) $f.Records['789'].expired = $true } 'immutable artifact origin'
    Assert-FixtureRejected 'producer-source' { param($f) $f.Producer.sourceCommit = 'c' * 40 } 'producer run/source'
    Assert-FixtureRejected 'producer-phase' { param($f) $f.Producer.phase = 'all' } 'producer run/source'
    Assert-FixtureRejected 'producer-version' { param($f) $f.Producer.packageVersion = '1.0.0' } 'producer run/source'
    Assert-FixtureRejected 'consumer-attempt' { param($f) $f.Consumer.runAttempt = '1' } 'consumer run/source'
    Assert-FixtureRejected 'consumer-producer-id' { param($f) $f.Consumer.producerArtifactId = '457' } 'consumer run/source'
    Assert-FixtureRejected 'receipt-extra-property' { param($f) $f.Consumer.untrusted = 1 } 'consumer receipt schema'
    Assert-FixtureRejected 'wrong-module' { param($f) $f.Consumer.module.moduleMvid = [guid]::NewGuid().ToString('D') } 'module moduleMvid'
    Assert-FixtureRejected 'collector-pin' { param($f) $f.Producer.collector.version = '0.0.1' } 'collector pin'
    Assert-FixtureRejected 'producer-extra-file' { param($f) [IO.File]::WriteAllText((Join-Path $f.ProducerRoot 'extra.txt'), 'x') } 'file inventory'
    Assert-FixtureRejected 'consumer-extra-file' { param($f) [IO.File]::WriteAllText((Join-Path $f.ConsumerRoot 'extra.txt'), 'x') } 'file inventory'
    Assert-FixtureRejected 'assembly-hash' { param($f) $f.Producer.files.assembly.sha256 = '0' * 64 } 'file hash differs'
    Assert-FixtureRejected 'independent-assembly' { param($f) [IO.File]::AppendAllText((Join-Path $f.RepositoryRoot $assemblyRelative), 'changed') } 'independently built authority bytes'
    Assert-FixtureRejected 'pdb-binding' { param($f) $f.Authority.modules[0].pdbCodeViewGuid = [guid]::NewGuid().ToString('D') } 'module pdbCodeViewGuid'
    Assert-FixtureRejected 'tool-package-version' { param($f)
        $path = Join-Path $f.ProducerRoot $f.Producer.files.package.path
        Write-FixturePackage $path 'SharpProof.Tool' '1.0.0' (Join-Path $f.ProducerRoot 'SharpProof.Tool.dll')
        $f.Producer.files.package.sha256 = Get-FixtureHash $path
    } 'package id/version'
    Assert-FixtureRejected 'tool-package-payload' { param($f)
        $path = Join-Path $f.ProducerRoot $f.Producer.files.package.path
        Write-FixturePackage $path 'SharpProof.Tool' $version (Join-Path $f.ProducerRoot 'SharpProof.Tool.pdb')
        $f.Producer.files.package.sha256 = Get-FixtureHash $path
    } 'package executable/symbol differs'
    Assert-FixtureRejected 'empty-binary' { param($f)
        $path = Join-Path $f.ConsumerRoot 'windows-tool.coverage'; [IO.File]::WriteAllBytes($path, [byte[]]@())
        $f.Consumer.files.binary.sha256 = Get-FixtureHash $path
    } 'collector binary is empty'
    Assert-FixtureRejected 'binary-hash' { param($f) $f.Consumer.files.binary.sha256 = '0' * 64 } 'file hash differs'
    Assert-FixtureRejected 'report-hash' { param($f) $f.Consumer.files.report.sha256 = '0' * 64 } 'file hash differs'
    foreach ($check in @('nativeWindows', 'outsideCheckout', 'assemblyUnmodified', 'existingProject', 'protectedDacl', 'daclPreserved', 'ownerPreserved', 'projectChanged', 'initializerWritten', 'noStagingFiles')) {
        Assert-FixtureRejected ("behavior-$check") { param($f) $f.Consumer.checks[$check] = $false }.GetNewClosure() 'native behavior did not pass'
    }
    Assert-FixtureRejected 'boolean-type' { param($f) $f.Consumer.checks.nativeWindows = 'true' } 'native behavior did not pass'
    Assert-FixtureRejected 'architecture' { param($f) $f.Consumer.checks.architecture = 'Arm64' } 'bytes/architecture'
    Assert-FixtureRejected 'installed-bytes' { param($f) $f.Consumer.checks.installedAssemblySha256 = '0' * 64 } 'bytes/architecture'
    Assert-FixtureRejected 'wrong-command' { param($f) $f.Consumer.command.arguments[0] = 'doctor' } 'installed command'
    Assert-FixtureRejected 'other-module' { param($f) Set-FixtureReport $f ($report.Replace('package name="SharpProof.Tool"', 'package name="SharpProof.Worker"')) } 'another module'
    Assert-FixtureRejected 'other-document' { param($f) Set-FixtureReport $f ($report.Replace('SetupInitializerFiles.cs', 'Other.cs')) } 'outside Tool PDB authority'
    Assert-FixtureRejected 'out-of-range-line' { param($f) Set-FixtureReport $f ($report.Replace('number="9"', 'number="11"')) } 'outside Tool PDB ranges'
    Assert-FixtureRejected 'negative-hits' { param($f) Set-FixtureReport $f ($report.Replace('hits="1"', 'hits="-1"')) } 'line/hit count'
    Assert-FixtureRejected 'overflow-hits' { param($f) Set-FixtureReport $f ($report.Replace('hits="1"', 'hits="2147483648"')) } 'line/hit count'
    Assert-FixtureRejected 'missing-first-dacl-hit' { param($f) Set-FixtureReport $f ($report.Replace('number="9" hits="1"', 'number="9" hits="0"')) } 'both authenticated DACL'
    Assert-FixtureRejected 'missing-second-dacl-hit' { param($f) Set-FixtureReport $f ($report.Replace('number="10" hits="1"', 'number="10" hits="0"')) } 'both authenticated DACL'
    Assert-FixtureRejected 'prestamped-report' { param($f) Set-FixtureReport $f ($report.Replace('<coverage>', '<coverage><sharpProofAuthority/>')) } 'pre-stamped authority'
    Assert-FixtureRejected 'noncanonical-file-path' { param($f) $f.Producer.files.package.path = '../feed/package.nupkg' } 'file descriptor identity'
    Assert-FixtureRejected 'linked-input' { param($f)
        $path = Join-Path $f.ConsumerRoot 'windows-tool.coverage'; [IO.File]::Delete($path)
        [void][IO.File]::CreateSymbolicLink($path, (Join-Path $f.ConsumerRoot 'windows-tool.cobertura.xml'))
    } 'linked evidence'
    Assert-FixtureRejected 'wrong-authenticated-archive-digest' { param($f) $f.Records['789'].digest = 'sha256:' + ('0' * 64) } 'authenticated Actions digest'
    Assert-FixtureRejected 'co-resealed-report-and-receipt' { param($f) Set-FixtureReport $f ($report.Replace('hits="1"', 'hits="99"')) } 'extracted artifact member differs'
    Assert-FixtureRejected 'unsafe-authenticated-zip-entry' { param($f)
        $path = Join-Path $f.Root 'output.zip'
        $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Update)
        try { $null = $zip.CreateEntry('../escape') } finally { $zip.Dispose() }
        $f.Records['789'].digest = 'sha256:' + (Get-FixtureHash $path)
    } 'unsafe, linked, or duplicate artifact member'
    Assert-FixtureRejected 'duplicate-authenticated-zip-entry' { param($f)
        $path = Join-Path $f.Root 'output.zip'
        $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Update)
        try { $null = $zip.CreateEntry('consumer.json') } finally { $zip.Dispose() }
        $f.Records['789'].digest = 'sha256:' + (Get-FixtureHash $path)
    } 'unsafe, linked, or duplicate artifact member'
    Write-Host "Native Tool supplemental coverage regressions passed: $controlCount controls."
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporaryRoot)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-native-evidence-[a-f0-9]{32}\z') { throw 'Unsafe native evidence fixture cleanup.' }
    [IO.Directory]::Delete($resolved, $true)
}
