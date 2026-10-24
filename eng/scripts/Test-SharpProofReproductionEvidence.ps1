[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Reproduction evidence controls require the canonical Linux amd64 container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReproductionEvidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1') -Force

$checks = 0
$commit = 'a' * 40
$tree = 'd' * 40
$catalog = 'b' * 64
$image = 'ghcr.io/alexyorke/sharpproof@sha256:' + ('0' * 64)
$version = '1.0.0-preview.1'
$native = @(([ordered]@{ path = 'z3/4.12.2/linux-x64/libz3.so'; bytes = 42; sha256 = 'c' * 64 }).AsReadOnly())
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-reproduction-evidence-' + [Guid]::NewGuid().ToString('N'))
$readArguments = @{
    SourceCommit = $commit; SourceTree = $tree; WorkflowRunId = '123456789'; SdkVersion = '10.0.402'
    ToolchainCatalogSha256 = $catalog; PackageVersion = $version; DeveloperConsumerImage = $image; NativeInputs = $native
}
function Assert-ReproductionFailure([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Reproduction evidence accepted a malformed or mismatched control.' }
}
function Save-ReproductionFixture([object]$Fixture) {
    $Fixture.Receipt | ConvertTo-Json -Depth 12 -Compress |
        Set-Content -LiteralPath $Fixture.Path -Encoding utf8NoBOM
}
function New-ReproductionFixture([int]$Index, [int]$Attempt = 1) {
    $directory = Join-Path $fixtureRoot ([Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $logs = [ordered]@{}
    foreach ($name in (Get-SharpProofReproductionLogNames -RunIndex $Index)) {
        $text = if ($name -ceq "run-$Index-sdk.log") { '10.0.402' }
        elseif ($name -ceq "run-$Index-full-pack.log") {
            "SharpProof.Package.IntegrationTests -> fixture.dll`nExported exact-package consumer harness: fixture`nValidated exact SharpProof package source: fixture`nThird-party inventory verified: fixture"
        }
        else { "Successful fixture phase $name" }
        $path = Join-Path $directory $name
        [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
        $logs[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $archives = [ordered]@{}
    $payloads = [ordered]@{}
    foreach ($archive in @("SharpProof.$version.nupkg", "SharpProof.Attributes.$version.nupkg", "SharpProof.Attributes.$version.snupkg",
            "SharpProof.Verifier.$version.nupkg", "SharpProof.Verifier.$version.snupkg")) {
        $archives[$archive] = $(if ($Index -eq 0) { 'e' * 64 } else { 'f' * 64 })
        $extension = if ($archive.EndsWith('.snupkg', [StringComparison]::Ordinal)) { 'pdb' } else { 'dll' }
        $payloads["$archive/lib/net10.0/SharpProof.Fixture.$extension"] = '1' * 64
    }
    $developerArchives = [ordered]@{}
    foreach ($id in @('SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')) {
        $developerArchives["$id.$version.nupkg"] = $(if ($Index -eq 0) { '2' * 64 } else { '3' * 64 })
    }
    $developerPayloads = [ordered]@{}
    foreach ($entry in @('SharpProof.Tool/tools/net10.0/any/SharpProof.Tool.dll',
            'SharpProof.CodeFixes/analyzers/dotnet/cs/SharpProof.CodeFixes.dll',
            'SharpProof.Templates/content/container/compose.yaml.template')) { $developerPayloads[$entry] = '4' * 64 }
    $relative = if ($Index -eq 0) { 'a' } else { 'independent longer path/checkout-b' }
    $workspace = '/tmp/sharpproof-repro-' + [Guid]::NewGuid().ToString('N') + '/' + $relative
    $receipt = [ordered]@{
        schemaVersion = 1; runIndex = $Index; workflowRunId = '123456789'; workflowRunAttempt = $Attempt
        sourceCommit = $commit; sourceTree = $tree; scope = 'Full'; sdkVersion = '10.0.402'; configuration = 'Release'
        continuousIntegrationBuild = $true; origin = 'https://github.com/alexyorke/SharpProof.git'; toolchainCatalogSha256 = $catalog
        packageVersion = $version; developerConsumerImage = $image; workspacePath = $workspace; cloneRelativePath = $relative
        gitCommonDirectory = $workspace + '/.git'; freshClone = $true; analyzersEnabled = $true
        core            = [ordered]@{ payloads = $payloads; archives = $archives }
        developer       = [ordered]@{ payloads = $developerPayloads; archives = $developerArchives }
        nativeInputsBefore = $native; nativeInputsAfter = $native
        consumerHarness = [ordered]@{ receiptSha256 = '5' * 64; payloadFileCount = 224; payloadBytes = 88774375 }
        logs            = $logs
    }
    $fixture = ([ordered]@{ Directory = $directory; Path = (Join-Path $directory "run-$Index-receipt.json"); Receipt = $receipt }).AsReadOnly()
    Save-ReproductionFixture $fixture
    return , $fixture
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $standalone = Get-SharpProofReproductionExecutionPlan
    if (($standalone.Indices -join '|') -cne '0|1') { throw 'Standalone reproduction must retain both original sequential builds.' }
    $checks++
    $cloneArguments = Get-SharpProofReproductionCloneArguments -Repository '/tmp/source checkout' -Clone '/tmp/independent longer path/checkout-b'
    if (($cloneArguments -join '|') -cne '-c|core.autocrlf=false|clone|--quiet|--no-hardlinks|--no-checkout|/tmp/source checkout|/tmp/independent longer path/checkout-b') {
        throw 'Standalone reproduction changed its original clone arguments or split a path containing spaces.'
    }
    $checks++
    $isolatedArguments = Get-SharpProofReproductionCloneArguments -Repository '/tmp/source checkout' -Clone '/tmp/independent longer path/checkout-b' -Isolated
    if ($isolatedArguments.Length -ne $cloneArguments.Length + 1 -or $isolatedArguments[6] -cne '--dissociate' -or
        $isolatedArguments[7] -cne $cloneArguments[6] -or $isolatedArguments[8] -cne $cloneArguments[7] -or
        $isolatedArguments -ccontains '--shared') {
        throw 'Distributed reproduction must dissociate Git object storage while retaining both exact paths.'
    }
    $checks++
    foreach ($index in @(0, 1)) {
        $plan = Get-SharpProofReproductionExecutionPlan -ExecutionMode Build -Scope Full -IncludeDeveloperPackages `
            -RunIndex $index -WorkflowRunId '123456789' -WorkflowRunAttempt 1
        if ($plan.Indices.Length -ne 1 -or $plan.Indices[0] -ne $index) { throw 'A distributed producer must execute exactly its own full build.' }
        $checks++
    }
    $retry = Get-SharpProofReproductionExecutionPlan -ExecutionMode Compare -Scope Full -IncludeDeveloperPackages `
        -WorkflowRunId '123456789' -WorkflowRunAttempt 2 -FirstRunDirectory first -SecondRunDirectory second -FirstRunAttempt 1
    if ($retry.Indices.Length -ne 0 -or $retry.FirstRunAttempt -ne 1 -or $retry.SecondRunAttempt -ne 2) {
        throw 'Comparison must accept exact successful producers from earlier attempts without rebuilding either.'
    }
    $checks++
    foreach ($invalid in @(
            @{ ExecutionMode = 'Build'; Scope = 'Full'; IncludeDeveloperPackages = $true; WorkflowRunId = '123456789'; WorkflowRunAttempt = 1 },
            @{ ExecutionMode = 'Build'; Scope = 'Representative'; IncludeDeveloperPackages = $true; RunIndex = 0; WorkflowRunId = '123456789'; WorkflowRunAttempt = 1 },
            @{ ExecutionMode = 'Build'; Scope = 'Full'; RunIndex = 0; WorkflowRunId = '123456789'; WorkflowRunAttempt = 1 },
            @{ ExecutionMode = 'Build'; Scope = 'Full'; IncludeDeveloperPackages = $true; RunIndex = 0; WorkflowRunId = '0123'; WorkflowRunAttempt = 1 },
            @{ ExecutionMode = 'Build'; Scope = 'Full'; IncludeDeveloperPackages = $true; RunIndex = 0; WorkflowRunId = '123456789'; WorkflowRunAttempt = 0 },
            @{ ExecutionMode = 'Sequential'; RunIndex = 0 },
            @{ ExecutionMode = 'Sequential'; FirstRunDirectory = 'first' },
            @{ ExecutionMode = 'Compare'; Scope = 'Full'; IncludeDeveloperPackages = $true; WorkflowRunId = '123456789'; WorkflowRunAttempt = 1; FirstRunDirectory = 'first' },
            @{ ExecutionMode = 'Compare'; Scope = 'Full'; IncludeDeveloperPackages = $true; WorkflowRunId = '123456789'; WorkflowRunAttempt = 2; FirstRunDirectory = 'first'; SecondRunDirectory = 'second'; FirstRunAttempt = 3 })) {
        Assert-ReproductionFailure { Get-SharpProofReproductionExecutionPlan @invalid }
        $checks++
    }
    $first = New-ReproductionFixture 0 1
    $second = New-ReproductionFixture 1 2
    $firstEvidence = Read-SharpProofReproductionRunEvidence -Directory $first.Directory -RunIndex 0 -ProducerAttempt 1 @readArguments
    $secondEvidence = Read-SharpProofReproductionRunEvidence -Directory $second.Directory -RunIndex 1 -ProducerAttempt 2 @readArguments
    $comparison = Compare-SharpProofDeveloperPackageSnapshots -First $firstEvidence.Developer -Second $secondEvidence.Developer
    if (-not $comparison.PayloadsEqual -or $comparison.PackageBytesEqual -or $comparison.PayloadCount -ne 3) {
        throw 'Original developer payload comparison and separate raw ZIP policy must survive receipt import.'
    }
    $checks++
    $output = Join-Path $fixtureRoot 'combined'
    Copy-SharpProofReproductionRunLogs -Evidence $firstEvidence -OutputDirectory $output
    Copy-SharpProofReproductionRunLogs -Evidence $secondEvidence -OutputDirectory $output
    if (@(Get-ChildItem -LiteralPath $output -Filter '*.log' -File).Count -ne 22) { throw 'Comparison must retain every original phase log.' }
    $checks++
    Assert-ReproductionFailure { Copy-SharpProofReproductionRunLogs -Evidence $firstEvidence -OutputDirectory $first.Directory }
    Assert-ReproductionFailure { Copy-SharpProofReproductionRunLogs -Evidence $firstEvidence -OutputDirectory (Join-Path $first.Directory 'output') }
    $checks += 2

    foreach ($mutation in @(
            { param($value) $value.sourceCommit = '6' * 40 },
            { param($value) $value.sourceTree = '7' * 40 },
            { param($value) $value.workflowRunId = '123456790' },
            { param($value) $value.workflowRunAttempt = 2 },
            { param($value) $value.workflowRunAttempt = '1' },
            { param($value) $value.runIndex = 1 },
            { param($value) $value.sdkVersion = '10.0.401' },
            { param($value) $value.toolchainCatalogSha256 = '8' * 64 },
            { param($value) $value.sourceCommit = @($commit) },
            { param($value) $value.configuration = 'Debug' },
            { param($value) $value.analyzersEnabled = $false },
            { param($value) $value.freshClone = $false },
            { param($value) $value.gitCommonDirectory = '/tmp/shared/.git' },
            { param($value) $value.cloneRelativePath = 'independent longer path/checkout-b' },
            { param($value) $value.nativeInputsAfter = @([ordered]@{ path = 'z3/4.12.2/linux-x64/libz3.so'; bytes = 43; sha256 = 'c' * 64 }) },
            { param($value) $value.nativeInputsBefore = @($value.nativeInputsBefore[0], $value.nativeInputsBefore[0]) },
            { param($value) $value.consumerHarness.payloadFileCount = 0 },
            { param($value) $value.core.archives.Remove("SharpProof.Verifier.$version.snupkg") },
            { param($value) $value.core.payloads['../unsafe.dll'] = '9' * 64 },
            { param($value) $value.core.payloads["SharpProof.$version.nupkg/lib/net10.0/SharpProof.Fixture.dll"] = 'invalid' },
            { param($value) $value.developer.payloads.Remove('SharpProof.Tool/tools/net10.0/any/SharpProof.Tool.dll') },
            { param($value) $value.developer.payloads['Other/payload.dll'] = 'a' * 64 },
            { param($value) $value.logs.Remove('run-0-developer-Tool-restore.log') },
            { param($value) $value['extra'] = $true })) {
        $fixture = New-ReproductionFixture 0
        & $mutation $fixture.Receipt
        Save-ReproductionFixture $fixture
        Assert-ReproductionFailure { Read-SharpProofReproductionRunEvidence -Directory $fixture.Directory -RunIndex 0 -ProducerAttempt 1 @readArguments }
        $checks++
    }
    $duplicate = New-ReproductionFixture 0
    $json = [IO.File]::ReadAllText($duplicate.Path).Replace('"schemaVersion":1', '"schemaVersion":1,"schemaVersion":1')
    [IO.File]::WriteAllText($duplicate.Path, $json)
    Assert-ReproductionFailure { Read-SharpProofReproductionRunEvidence -Directory $duplicate.Directory -RunIndex 0 -ProducerAttempt 1 @readArguments }
    $checks++
    foreach ($phase in @('sdk', 'full-pack', 'developer-Tool-build')) {
        $fixture = New-ReproductionFixture 0
        $path = Join-Path $fixture.Directory "run-0-$phase.log"
        [IO.File]::WriteAllText($path, 'altered log')
        Assert-ReproductionFailure { Read-SharpProofReproductionRunEvidence -Directory $fixture.Directory -RunIndex 0 -ProducerAttempt 1 @readArguments }
        $checks++
    }
    $changedAfterRead = New-ReproductionFixture 0
    $evidence = Read-SharpProofReproductionRunEvidence -Directory $changedAfterRead.Directory -RunIndex 0 -ProducerAttempt 1 @readArguments
    [IO.File]::WriteAllText((Join-Path $changedAfterRead.Directory 'run-0-sdk.log'), 'changed after import')
    Assert-ReproductionFailure { Copy-SharpProofReproductionRunLogs -Evidence $evidence -OutputDirectory $output }
    $checks++
}
finally {
    if ([IO.Directory]::Exists($fixtureRoot)) { [IO.Directory]::Delete($fixtureRoot, $true) }
}
Write-Host "Reproduction evidence controls passed: $checks standalone, independent-run, retry, identity, native, payload, and log checks."
