Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assert-SharpProofJsonProperties.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1')

function Get-SharpProofConsumerHarnessTests {
    return @(
        'SharpProof.Package.Test.PackageLayoutSmokeTests.PackageGraphAndLayoutsAreExact'
        'SharpProof.Package.Test.PackageLayoutSmokeTests.StrictAnalyzerSetDiscoversEachEntrypointOnce'
        'SharpProof.Package.Test.PackageLayoutSmokeTests.VerifierPackageTransitivelySuppliesPortableProduct'
    )
}

function Get-ConsumerHarnessHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ConsumerHarnessFiles([string]$Root) {
    $directory = Get-Item -LiteralPath $Root -Force
    if (-not $directory.PSIsContainer -or
        ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Consumer harness directories must be ordinary directories.'
    }
    foreach ($item in Get-ChildItem -LiteralPath $directory.FullName -Force | Sort-Object Name -CaseSensitive) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Consumer harness must not contain links: $($item.Name)"
        }
        if ($item.PSIsContainer) {
            if ($item.Name -ieq 'obj') { throw 'Consumer harness must not contain compiler intermediates.' }
            Get-ConsumerHarnessFiles -Root $item.FullName
        }
        else { $item }
    }
}

function Get-ConsumerHarnessInventory([string]$Root) {
    foreach ($file in @(Get-ConsumerHarnessFiles -Root $Root)) {
        [ordered]@{
            path   = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
            bytes  = $file.Length
            sha256 = Get-ConsumerHarnessHash -Path $file.FullName
        }
    }
}

function Get-ConsumerHarnessPackages([string]$PackageSource, [string]$RepositoryCommit) {
    $source = (Resolve-Path -LiteralPath $PackageSource).Path
    $files = @(Get-ConsumerHarnessFiles -Root $source | Where-Object Extension -In @('.nupkg', '.snupkg'))
    if (@($files | Where-Object DirectoryName -CNE $source).Count -ne 0) {
        throw 'Consumer harness requires a flat package source.'
    }
    $identities = Get-SharpProofPackageIdentitySet -Files $files -RequireRepository
    foreach ($file in $files | Sort-Object Name -CaseSensitive) {
        $identity = Get-SharpProofPackageIdentity -Path $file.FullName -RequireSingleIdentity -RequireRepository
        if ($identity.RepositoryCommit -cne $RepositoryCommit) {
            throw 'Consumer harness package commit does not match the checkout.'
        }
        [ordered]@{
            path             = $file.Name
            id               = $identity.Id
            version          = $identities.Version
            repositoryCommit = $identity.RepositoryCommit
            bytes            = $file.Length
            sha256           = Get-ConsumerHarnessHash -Path $file.FullName
        }
    }
}

function Get-ConsumerHarnessContext([string]$RepositoryRoot, [string]$Configuration) {
    $commit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Consumer harness cannot resolve the exact checkout commit.'
    }
    $toolchain = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/container/toolchain.json') -Raw | ConvertFrom-Json
    $global = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Raw | ConvertFrom-Json
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -cne $toolchain.dotnet.sdkVersion -or $sdk -cne $global.sdk.version) {
        throw 'Consumer harness requires the exact catalog-pinned SDK.'
    }
    $inputs = foreach ($path in @('global.json', 'eng/container/toolchain.json', 'eng/Versions.props',
            'src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj')) {
        [ordered]@{ path = $path; sha256 = Get-ConsumerHarnessHash -Path (Join-Path $RepositoryRoot $path) }
    }
    return [ordered]@{
        repositoryCommit = $commit
        configuration    = $Configuration
        platform         = 'linux/amd64'
        sdkVersion       = $sdk
        inputs           = @($inputs)
    }
}

function Assert-ConsumerHarnessPayload([object[]]$Files) {
    $paths = @($Files | ForEach-Object { [string]$_.path })
    foreach ($required in @('SharpProof.Package.Test.dll', 'SharpProof.Package.Test.deps.json',
            'SharpProof.Package.Test.runtimeconfig.json', 'nunit.framework.dll', 'NUnit3.TestAdapter.dll', 'testhost.dll')) {
        if ($paths -cnotcontains $required) { throw "Consumer harness is missing runtime dependency '$required'." }
    }
}

function New-SharpProofConsumerHarnessReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][ValidateSet('Debug', 'Release')][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$PackageSource,
        [Parameter(Mandatory = $true)][string]$PayloadDirectory
    )
    $context = Get-ConsumerHarnessContext -RepositoryRoot $RepositoryRoot -Configuration $Configuration
    $files = @(Get-ConsumerHarnessInventory -Root $PayloadDirectory)
    Assert-ConsumerHarnessPayload -Files $files
    return [ordered]@{
        schemaVersion = 1
        context       = $context
        assembly      = 'payload/SharpProof.Package.Test.dll'
        tests         = @(Get-SharpProofConsumerHarnessTests)
        packages      = @(Get-ConsumerHarnessPackages -PackageSource $PackageSource -RepositoryCommit $context.repositoryCommit)
        files         = $files
    }
}

function Assert-ConsumerHarnessRecords([object[]]$Actual, [object[]]$Expected, [string]$Description) {
    if ($Actual.Count -ne $Expected.Count) { throw "Consumer harness $Description file set differs from the receipt." }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $Actual) {
        $path = [string]$record.path
        if ([string]::IsNullOrWhiteSpace($path) -or $path -match '[\\:\x00-\x1f]' -or
            $path.StartsWith('/') -or @($path.Split('/') | Where-Object { $_ -in @('', '.', '..', 'obj') }).Count -ne 0 -or
            -not $seen.Add($path)) {
            throw "Consumer harness $Description contains an unsafe or duplicate path."
        }
        $match = @($Expected | Where-Object { $_.path -ceq $path })
        if ($match.Count -ne 1) { throw "Consumer harness $Description file set differs from the receipt." }
        $reference = $match[0]
        Assert-SharpProofExactJsonProperties -Actual @($record.Keys) -Expected @($reference.Keys) -Description "Consumer harness $Description record" -RejectDuplicates
        foreach ($key in $reference.Keys) {
            if ($key -ceq 'bytes') {
                if ($record[$key] -isnot [long] -and $record[$key] -isnot [int]) {
                    throw "Consumer harness $Description has an invalid byte count."
                }
            }
            if ([string]$record[$key] -cne [string]$reference[$key]) {
                throw "Consumer harness $Description '$path' differs from the receipt ($key)."
            }
        }
    }
}

function Test-SharpProofConsumerHarnessReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$HarnessRoot,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][ValidateSet('Debug', 'Release')][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$PackageSource
    )
    $root = (Resolve-Path -LiteralPath $HarnessRoot).Path
    # Enumerate before opening any supplied executable or receipt. Links and
    # intermediate directories are rejected even when omitted from the receipt.
    $allFiles = @(Get-ConsumerHarnessFiles -Root $root)
    $receiptPath = Join-Path $root 'receipt.json'
    $json = [IO.File]::ReadAllText($receiptPath)
    $document = [Text.Json.JsonDocument]::Parse($json)
    try { Assert-UniqueJsonProperties -Value $document.RootElement -Context 'Consumer harness receipt' }
    finally { $document.Dispose() }
    $receipt = ConvertFrom-Json -InputObject $json -AsHashtable
    Assert-SharpProofExactJsonProperties -Actual @($receipt.Keys) -Expected @('schemaVersion', 'context', 'assembly', 'tests', 'packages', 'files') -Description 'Consumer harness receipt'
    if ($receipt.schemaVersion -isnot [long] -and $receipt.schemaVersion -isnot [int]) { throw 'Consumer harness schema is invalid.' }
    if ($receipt.schemaVersion -ne 1 -or $receipt.assembly -cne 'payload/SharpProof.Package.Test.dll') {
        throw 'Consumer harness schema or assembly is invalid.'
    }
    $expected = Get-ConsumerHarnessContext -RepositoryRoot $RepositoryRoot -Configuration $Configuration
    Assert-SharpProofExactJsonProperties -Actual @($receipt.context.Keys) -Expected @($expected.Keys) -Description 'Consumer harness context'
    foreach ($key in @('repositoryCommit', 'configuration', 'platform', 'sdkVersion')) {
        if ($receipt.context[$key] -cne $expected[$key]) { throw "Consumer harness context mismatch: $key." }
    }
    Assert-ConsumerHarnessRecords -Actual @($receipt.context.inputs) -Expected @($expected.inputs) -Description 'source inputs'
    $tests = @(Get-SharpProofConsumerHarnessTests)
    if (@($receipt.tests).Count -ne $tests.Count -or ($receipt.tests -join '|') -cne ($tests -join '|')) {
        throw 'Consumer harness must run the complete focused consumer test set.'
    }
    $packages = @(Get-ConsumerHarnessPackages -PackageSource $PackageSource -RepositoryCommit $expected.repositoryCommit)
    Assert-ConsumerHarnessRecords -Actual @($receipt.packages) -Expected $packages -Description 'packages'
    $payload = Join-Path $root 'payload'
    $files = @(Get-ConsumerHarnessInventory -Root $payload)
    Assert-ConsumerHarnessPayload -Files $files
    Assert-ConsumerHarnessRecords -Actual @($receipt.files) -Expected $files -Description 'payload'
    if ($allFiles.Count -ne $files.Count + 1 -or
        @($allFiles | Where-Object { $_.FullName -cne $receiptPath -and
                -not $_.FullName.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) }).Count -ne 0) {
        throw 'Consumer harness has unexpected files outside its payload.'
    }
    return [ordered]@{
        AssemblyPath     = Join-Path $root $receipt.assembly
        AdapterPath      = $payload
        ReceiptSha256    = Get-ConsumerHarnessHash -Path $receiptPath
        RepositoryCommit = $expected.repositoryCommit
        Tests            = $tests
    }
}

function Assert-SharpProofConsumerHarnessResults {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    $namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $results = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $namespace))
    $expected = @(Get-SharpProofConsumerHarnessTests)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($results.Count -ne $expected.Count) { throw 'Consumer harness did not execute every focused consumer test.' }
    foreach ($result in $results) {
        $id = $result.GetAttribute('testId')
        $definitions = @($document.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $namespace) |
                Where-Object { $_.GetAttribute('id') -ceq $id })
        if ($definitions.Count -ne 1) { throw 'Consumer harness result has no unique test definition.' }
        $method = $definitions[0].SelectSingleNode('t:TestMethod', $namespace)
        if ($null -eq $method) { throw 'Consumer harness result has no test method.' }
        $name = $method.GetAttribute('className') + '.' + $method.GetAttribute('name')
        if ($expected -cnotcontains $name -or -not $seen.Add($name) -or $result.GetAttribute('outcome') -cne 'Passed') {
            throw 'Consumer harness has an unexpected, duplicate, skipped, or failed test.'
        }
    }
    $summary = $document.SelectSingleNode('/t:TestRun/t:ResultSummary', $namespace)
    if ($null -eq $summary -or $summary.GetAttribute('outcome') -cne 'Completed') {
        throw 'Consumer harness test run did not complete.'
    }
}

Export-ModuleMember -Function Get-SharpProofConsumerHarnessTests, New-SharpProofConsumerHarnessReceipt,
Test-SharpProofConsumerHarnessReceipt, Assert-SharpProofConsumerHarnessResults
