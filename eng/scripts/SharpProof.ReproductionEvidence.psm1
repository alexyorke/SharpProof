Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assert-SharpProofJsonProperties.ps1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

function Get-SharpProofReproductionCloneArguments {
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Repository,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Clone,
        [switch]$Isolated
    )
    $arguments = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $arguments.AddRange([string[]]@('-c', 'core.autocrlf=false', 'clone', '--quiet', '--no-hardlinks', '--no-checkout'))
    if ($Isolated) { $arguments.Add('--dissociate') }
    $arguments.Add($Repository)
    $arguments.Add($Clone)
    return , $arguments.ToImmutable()
}

function Get-SharpProofReproductionExecutionPlan {
    param(
        [ValidateSet('Sequential', 'Build', 'Compare')][string]$ExecutionMode = 'Sequential',
        [ValidateSet('Representative', 'Full')][string]$Scope = 'Representative',
        [switch]$IncludeDeveloperPackages,
        [ValidateRange(-1, 1)][int]$RunIndex = -1,
        [string]$WorkflowRunId = '',
        [ValidateRange(0, 2147483647)][int]$WorkflowRunAttempt = 0,
        [string]$FirstRunDirectory = '',
        [string]$SecondRunDirectory = '',
        [ValidateRange(0, 2147483647)][int]$FirstRunAttempt = 0,
        [ValidateRange(0, 2147483647)][int]$SecondRunAttempt = 0
    )
    if ($ExecutionMode -ne 'Sequential' -and ($Scope -ne 'Full' -or -not $IncludeDeveloperPackages -or
            $WorkflowRunId -cnotmatch '\A[1-9][0-9]{0,18}\z' -or $WorkflowRunAttempt -lt 1)) {
        throw 'Distributed reproduction requires Full scope, all developer packages, and exact workflow run/attempt identities.'
    }
    if (($ExecutionMode -eq 'Build' -and $RunIndex -notin @(0, 1)) -or ($ExecutionMode -ne 'Build' -and $RunIndex -ne -1)) {
        throw 'Build reproduction requires exactly one explicit run index; other modes do not accept one.'
    }
    if ($ExecutionMode -eq 'Compare') {
        if ([string]::IsNullOrWhiteSpace($FirstRunDirectory) -or [string]::IsNullOrWhiteSpace($SecondRunDirectory)) {
            throw 'Comparison requires both independently produced run directories.'
        }
        if ($FirstRunAttempt -eq 0) { $FirstRunAttempt = $WorkflowRunAttempt }
        if ($SecondRunAttempt -eq 0) { $SecondRunAttempt = $WorkflowRunAttempt }
        if ($FirstRunAttempt -gt $WorkflowRunAttempt -or $SecondRunAttempt -gt $WorkflowRunAttempt) {
            throw 'A reproduction producer attempt cannot be newer than its comparison attempt.'
        }
    }
    elseif ($FirstRunDirectory -or $SecondRunDirectory -or $FirstRunAttempt -or $SecondRunAttempt) {
        throw 'Run directories and producer attempts are supported only for comparison.'
    }
    $indices = [Collections.Immutable.ImmutableArray]::CreateBuilder[int]()
    if ($ExecutionMode -eq 'Build') { $indices.Add($RunIndex) }
    elseif ($ExecutionMode -eq 'Sequential') { $indices.Add(0); $indices.Add(1) }
    return , ([ordered]@{
            Indices = $indices.ToImmutable()
            FirstRunAttempt = $FirstRunAttempt; SecondRunAttempt = $SecondRunAttempt
        }).AsReadOnly()
}

function Assert-ReproductionProperties([object]$Value, [string[]]$Names, [string]$Description) {
    if ($Value -isnot [Collections.IDictionary]) { throw "$Description must be an object." }
    Assert-SharpProofExactJsonProperties -Actual @($Value.Keys) -Expected $Names -Description $Description
}

function Assert-ReproductionRelativePath([string]$Path) {
    if ([string]::IsNullOrEmpty($Path) -or $Path -match '[\\:\x00-\x1f]' -or $Path.StartsWith('/') -or
        @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0) {
        throw 'Reproduction evidence contains an unsafe relative path.'
    }
}

function ConvertFrom-ReproductionHashMap([object]$Value, [string]$Description) {
    if ($Value -isnot [Collections.IDictionary] -or $Value.Count -eq 0) { throw "$Description must be a nonempty hash map." }
    $hashes = [Collections.Immutable.ImmutableSortedDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
    foreach ($key in $Value.Keys) {
        if ($key -isnot [string] -or $Value[$key] -isnot [string] -or $Value[$key] -cnotmatch '\A[a-f0-9]{64}\z') {
            throw "$Description contains an invalid SHA256 entry."
        }
        Assert-ReproductionRelativePath $key
        $hashes.Add($key, $Value[$key])
    }
    return , $hashes.ToImmutable()
}

function ConvertTo-ReproductionNativeJson([object]$Value) {
    if ($Value -isnot [array] -and $Value -isnot [Collections.Generic.IReadOnlyList[object]]) {
        throw 'Reproduction native inputs must be an array.'
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($entry in $Value) {
        Assert-ReproductionProperties $entry @('path', 'bytes', 'sha256') 'Reproduction native input'
        if ($entry.path -isnot [string] -or $entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '\A[a-f0-9]{64}\z' -or
            ($entry.bytes -isnot [int] -and $entry.bytes -isnot [long]) -or $entry.bytes -le 0 -or -not $names.Add($entry.path)) {
            throw 'Reproduction native input has an invalid path, size, digest, or duplicate.'
        }
        Assert-ReproductionRelativePath $entry.path
        $entries.Add(([ordered]@{ path = $entry.path; bytes = [long]$entry.bytes; sha256 = $entry.sha256 }).AsReadOnly())
    }
    if ($entries.Count -eq 0) { throw 'Reproduction native inputs cannot be empty.' }
    return ConvertTo-Json -InputObject ([object[]]@($entries | Sort-Object -Property path -CaseSensitive)) -Depth 4 -Compress
}

function Get-SharpProofReproductionLogNames([ValidateRange(0, 1)][int]$RunIndex) {
    $names = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $names.Add("run-$RunIndex-sdk.log")
    $names.Add("run-$RunIndex-full-pack.log")
    foreach ($product in @('Tool', 'CodeFixes', 'Templates')) {
        foreach ($phase in @('restore', 'build', 'pack')) { $names.Add("run-$RunIndex-developer-$product-$phase.log") }
    }
    return , $names.ToImmutable()
}

function Read-SharpProofReproductionRunEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][ValidateRange(0, 1)][int]$RunIndex,
        [Parameter(Mandatory)][string]$SourceCommit,
        [Parameter(Mandatory)][string]$SourceTree,
        [Parameter(Mandatory)][string]$WorkflowRunId,
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$ProducerAttempt,
        [Parameter(Mandatory)][string]$SdkVersion,
        [Parameter(Mandatory)][string]$ToolchainCatalogSha256,
        [Parameter(Mandatory)][string]$PackageVersion,
        [Parameter(Mandatory)][string]$DeveloperConsumerImage,
        [Parameter(Mandatory)][object]$NativeInputs
    )
    if ($WorkflowRunId -cnotmatch '\A[1-9][0-9]{0,18}\z' -or $SourceCommit -cnotmatch '\A[a-f0-9]{40}\z' -or
        $SourceTree -cnotmatch '\A[a-f0-9]{40}\z') { throw 'Reproduction comparison requires exact source and workflow identities.' }
    $root = [IO.Path]::GetFullPath($Directory)
    $receiptPath = Resolve-SharpProofContainedPath -Root $root -Path "run-$RunIndex-receipt.json" -ParameterName 'Reproduction receipt'
    $bytes = [IO.File]::ReadAllBytes($receiptPath)
    if ($bytes.Length -eq 0 -or $bytes.Length -gt 16MB) { throw 'Reproduction receipt size is invalid.' }
    $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $document = [Text.Json.JsonDocument]::Parse($json)
    try { Assert-UniqueJsonProperties -Value $document.RootElement -Context 'Reproduction run receipt' }
    finally { $document.Dispose() }
    $receipt = ConvertFrom-Json -InputObject $json -AsHashtable
    Assert-ReproductionProperties $receipt @(
        'schemaVersion', 'runIndex', 'workflowRunId', 'workflowRunAttempt', 'sourceCommit', 'sourceTree',
        'scope', 'sdkVersion', 'configuration', 'continuousIntegrationBuild', 'origin', 'toolchainCatalogSha256',
        'packageVersion', 'developerConsumerImage', 'workspacePath', 'cloneRelativePath', 'gitCommonDirectory',
        'freshClone', 'analyzersEnabled', 'core', 'developer', 'nativeInputsBefore', 'nativeInputsAfter',
        'consumerHarness', 'logs') 'Reproduction run receipt'
    foreach ($property in @('sourceCommit', 'sourceTree', 'scope', 'sdkVersion', 'configuration', 'origin',
            'toolchainCatalogSha256', 'packageVersion', 'developerConsumerImage', 'cloneRelativePath', 'gitCommonDirectory')) {
        if ($receipt[$property] -isnot [string]) { throw 'Reproduction identity fields must be strings.' }
    }
    if (($receipt.schemaVersion -isnot [int] -and $receipt.schemaVersion -isnot [long]) -or $receipt.schemaVersion -ne 1 -or
        ($receipt.runIndex -isnot [int] -and $receipt.runIndex -isnot [long]) -or $receipt.runIndex -ne $RunIndex -or
        ($receipt.workflowRunAttempt -isnot [int] -and $receipt.workflowRunAttempt -isnot [long]) -or
        $receipt.workflowRunAttempt -ne $ProducerAttempt -or $receipt.workflowRunId -isnot [string] -or
        $receipt.workflowRunId -cne $WorkflowRunId -or $receipt.sourceCommit -cne $SourceCommit -or $receipt.sourceTree -cne $SourceTree -or
        $receipt.scope -cne 'Full' -or $receipt.sdkVersion -cne $SdkVersion -or $receipt.configuration -cne 'Release' -or
        $receipt.continuousIntegrationBuild -isnot [bool] -or -not $receipt.continuousIntegrationBuild -or
        $receipt.freshClone -isnot [bool] -or -not $receipt.freshClone -or
        $receipt.analyzersEnabled -isnot [bool] -or -not $receipt.analyzersEnabled -or
        $receipt.origin -cne 'https://github.com/alexyorke/SharpProof.git' -or
        $receipt.toolchainCatalogSha256 -cne $ToolchainCatalogSha256 -or $receipt.packageVersion -cne $PackageVersion -or
        $receipt.developerConsumerImage -cne $DeveloperConsumerImage) {
        throw 'Reproduction run source, workflow, toolchain, configuration, or qualification identity differs.'
    }
    $relative = if ($RunIndex -eq 0) { 'a' } else { 'independent longer path/checkout-b' }
    if ($receipt.workspacePath -isnot [string] -or $receipt.cloneRelativePath -cne $relative -or
        -not [IO.Path]::IsPathRooted($receipt.workspacePath) -or
        $receipt.workspacePath -cnotmatch ('/sharpproof-repro-[a-f0-9]{32}/' + [regex]::Escape($relative) + '\z') -or
        $receipt.gitCommonDirectory -cne ($receipt.workspacePath + '/.git')) {
        throw 'Reproduction run does not describe the required isolated independent source path.'
    }
    $nativeBefore = ConvertTo-ReproductionNativeJson $receipt.nativeInputsBefore
    if ($nativeBefore -cne (ConvertTo-ReproductionNativeJson $receipt.nativeInputsAfter) -or
        $nativeBefore -cne (ConvertTo-ReproductionNativeJson $NativeInputs)) {
        throw 'Reproduction native input identities differ before, after, or between runners.'
    }
    Assert-ReproductionProperties $receipt.core @('payloads', 'archives') 'Core reproduction snapshot'
    Assert-ReproductionProperties $receipt.developer @('payloads', 'archives') 'Developer reproduction snapshot'
    $corePayloads = ConvertFrom-ReproductionHashMap $receipt.core.payloads 'Core reproduction payloads'
    $coreArchives = ConvertFrom-ReproductionHashMap $receipt.core.archives 'Core reproduction archives'
    $developerPayloads = ConvertFrom-ReproductionHashMap $receipt.developer.payloads 'Developer reproduction payloads'
    $developerArchives = ConvertFrom-ReproductionHashMap $receipt.developer.archives 'Developer reproduction archives'
    Assert-SharpProofExactJsonProperties -Actual @($coreArchives.Keys) -Expected @(
        "SharpProof.$PackageVersion.nupkg", "SharpProof.Attributes.$PackageVersion.nupkg", "SharpProof.Attributes.$PackageVersion.snupkg",
        "SharpProof.Verifier.$PackageVersion.nupkg", "SharpProof.Verifier.$PackageVersion.snupkg") -Description 'Core reproduction archive identities'
    Assert-SharpProofExactJsonProperties -Actual @($developerArchives.Keys) -Expected @(
        "SharpProof.Tool.$PackageVersion.nupkg", "SharpProof.CodeFixes.$PackageVersion.nupkg", "SharpProof.Templates.$PackageVersion.nupkg") -Description 'Developer reproduction archive identities'
    foreach ($key in $corePayloads.Keys) {
        $slash = $key.IndexOf('/')
        if ($slash -le 0 -or -not $coreArchives.ContainsKey($key.Substring(0, $slash)) -or
            -not [IO.Path]::GetFileName($key).StartsWith('SharpProof.', [StringComparison]::Ordinal) -or
            [IO.Path]::GetExtension($key) -cnotin @('.dll', '.pdb')) {
            throw 'Core reproduction payload is outside its original archive or managed DLL/PDB scope.'
        }
    }
    foreach ($required in @('SharpProof.Tool/tools/net10.0/any/SharpProof.Tool.dll',
            'SharpProof.CodeFixes/analyzers/dotnet/cs/SharpProof.CodeFixes.dll',
            'SharpProof.Templates/content/container/compose.yaml.template')) {
        if (-not $developerPayloads.ContainsKey($required)) { throw 'Developer reproduction is missing required product payload authority.' }
    }
    foreach ($key in $developerPayloads.Keys) {
        if ($key.Split('/')[0] -cnotin @('SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')) {
            throw 'Developer reproduction payload is outside the three qualified products.'
        }
    }
    Assert-ReproductionProperties $receipt.consumerHarness @('receiptSha256', 'payloadFileCount', 'payloadBytes') 'Consumer harness proof'
    if ($receipt.consumerHarness.receiptSha256 -isnot [string] -or $receipt.consumerHarness.receiptSha256 -cnotmatch '\A[a-f0-9]{64}\z' -or
        ($receipt.consumerHarness.payloadFileCount -isnot [int] -and $receipt.consumerHarness.payloadFileCount -isnot [long]) -or
        $receipt.consumerHarness.payloadFileCount -le 0 -or
        ($receipt.consumerHarness.payloadBytes -isnot [int] -and $receipt.consumerHarness.payloadBytes -isnot [long]) -or
        $receipt.consumerHarness.payloadBytes -le 0) { throw 'Reproduction consumer harness export proof is invalid.' }
    $logNames = Get-SharpProofReproductionLogNames -RunIndex $RunIndex
    Assert-ReproductionProperties $receipt.logs ([string[]]@($logNames)) 'Reproduction phase logs'
    $sdkLog = $null
    $packLog = $null
    foreach ($name in $logNames) {
        $path = Resolve-SharpProofContainedPath -Root $root -Path $name -ParameterName 'Reproduction phase log'
        $logBytes = [IO.File]::ReadAllBytes($path)
        if ($receipt.logs[$name] -isnot [string] -or $receipt.logs[$name] -cnotmatch '\A[a-f0-9]{64}\z' -or
            $logBytes.Length -eq 0 -or
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($logBytes)).ToLowerInvariant() -cne $receipt.logs[$name]) {
            throw 'Reproduction phase log is missing, empty, or differs from its receipt.'
        }
        $logText = [Text.UTF8Encoding]::new($false, $true).GetString($logBytes)
        if ($name -ceq "run-$RunIndex-sdk.log") { $sdkLog = $logText.Trim() }
        if ($name -ceq "run-$RunIndex-full-pack.log") { $packLog = $logText }
    }
    if ($sdkLog -cne $SdkVersion -or -not $packLog.Contains('SharpProof.Package.IntegrationTests ->') -or
        -not $packLog.Contains('Exported exact-package consumer harness:') -or
        -not $packLog.Contains('Validated exact SharpProof package source:') -or
        -not $packLog.Contains('Third-party inventory verified:')) {
        throw 'Reproduction phase logs do not establish SDK and full package/harness qualification.'
    }
    return , ([ordered]@{
            Directory = $root; Receipt = $receipt; ReceiptSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            Core      = ([ordered]@{ payloads = $corePayloads; archives = $coreArchives }).AsReadOnly()
            Developer = ([ordered]@{ Payloads = $developerPayloads; Archives = $developerArchives }).AsReadOnly()
        }).AsReadOnly()
}

function Copy-SharpProofReproductionRunLogs {
    param([Parameter(Mandatory)][object]$Evidence, [Parameter(Mandatory)][string]$OutputDirectory)
    $output = Resolve-SharpProofPhysicalPath -Path ([IO.Path]::GetFullPath($OutputDirectory))
    $inputRoot = Resolve-SharpProofPhysicalPath -Path ([IO.Path]::GetFullPath($Evidence.Directory))
    if ($output -ceq $inputRoot -or $output.StartsWith($inputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        $inputRoot.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'Reproduction comparison output must not overlap its immutable inputs.'
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    foreach ($name in (Get-SharpProofReproductionLogNames -RunIndex $Evidence.Receipt.runIndex)) {
        $source = Resolve-SharpProofContainedPath -Root $inputRoot -Path $name -ParameterName 'Reproduction phase log'
        $bytes = [IO.File]::ReadAllBytes($source)
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -cne $Evidence.Receipt.logs[$name]) {
            throw 'Reproduction input log changed after validation.'
        }
        $destination = Resolve-SharpProofContainedPath -Root $output -Path $name -ParameterName 'Reproduction output log'
        [IO.File]::WriteAllBytes($destination, $bytes)
    }
}

Export-ModuleMember -Function Get-SharpProofReproductionCloneArguments, Get-SharpProofReproductionExecutionPlan, Get-SharpProofReproductionLogNames, Read-SharpProofReproductionRunEvidence, Copy-SharpProofReproductionRunLogs
