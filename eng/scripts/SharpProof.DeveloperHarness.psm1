Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Assert-SharpProofJsonProperties.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.CanonicalCohortEvidence.ps1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1')

function Get-DeveloperHarnessHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-DeveloperHarnessFiles([string]$Root) {
    $directory = Get-Item -LiteralPath $Root -Force
    if (-not $directory.PSIsContainer -or
        ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Developer harness directories must be ordinary directories.'
    }
    foreach ($item in Get-ChildItem -LiteralPath $directory.FullName -Force | Sort-Object Name -CaseSensitive) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Developer harness must not contain links: $($item.Name)"
        }
        if ($item.PSIsContainer) {
            if ($item.Name -ieq 'obj') { throw 'Developer harness must not contain compiler intermediates.' }
            Get-DeveloperHarnessFiles -Root $item.FullName
        }
        else { $item }
    }
}

function Get-DeveloperHarnessInventory([string]$Root) {
    foreach ($file in @(Get-DeveloperHarnessFiles -Root $Root)) {
        [ordered]@{
            path   = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
            bytes  = $file.Length
            sha256 = Get-DeveloperHarnessHash -Path $file.FullName
        }
    }
}


function Assert-DeveloperHarnessRecords([object[]]$Actual, [object[]]$Expected, [string]$Description) {
    if ($Actual.Count -ne $Expected.Count) { throw "Developer harness $Description file set differs from the receipt." }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $Actual) {
        $path = [string]$record.path
        if ([string]::IsNullOrWhiteSpace($path) -or $path -match '[\\:\x00-\x1f]' -or
            $path.StartsWith('/') -or @($path.Split('/') | Where-Object { $_ -in @('', '.', '..', 'obj') }).Count -ne 0 -or
            -not $seen.Add($path)) {
            throw "Developer harness $Description contains an unsafe or duplicate path."
        }
        $match = @($Expected | Where-Object { $_.path -ceq $path })
        if ($match.Count -ne 1) { throw "Developer harness $Description file set differs from the receipt." }
        $reference = $match[0]
        Assert-SharpProofExactJsonProperties -Actual @($record.Keys) -Expected @($reference.Keys) -Description "Developer harness $Description record" -RejectDuplicates
        foreach ($key in $reference.Keys) {
            if ($key -ceq 'bytes') {
                if ($record[$key] -isnot [long] -and $record[$key] -isnot [int]) {
                    throw "Developer harness $Description has an invalid byte count."
                }
            }
            if ([string]$record[$key] -cne [string]$reference[$key]) {
                throw "Developer harness $Description '$path' differs from the receipt ($key)."
            }
        }
    }
}


function Get-DeveloperHarnessContext([string]$RepositoryRoot, [switch]$Producer) {
    Assert-SharpProofContainer 'Developer harness reuse requires the canonical Linux amd64 container.'
    if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [Runtime.InteropServices.Architecture]::X64 -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
        throw 'Developer harness reuse requires Linux amd64.'
    }
    $commit = (& git -C $RepositoryRoot rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Developer harness cannot resolve the exact source commit.' }
    $tree = (& git -C $RepositoryRoot rev-parse 'HEAD^{tree}' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $tree -cnotmatch '^[0-9a-f]{40}$') { throw 'Developer harness cannot resolve the exact source tree.' }
    $dirty = @(& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Developer harness requires clean exact-commit source.' }
    # The independently recomputed Git tree binds every tracked source,
    # project, lockfile, NuGet setting, build script and dependency version.
    $toolchain = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/container/toolchain.json') -Raw | ConvertFrom-Json
    $global = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Raw | ConvertFrom-Json
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -cne $toolchain.dotnet.sdkVersion -or $sdk -cne $global.sdk.version) {
        throw 'Developer harness requires the exact catalog-pinned SDK.'
    }
    [xml]$versions = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/Versions.props') -Raw
    $versionNode = $versions.SelectSingleNode('/Project/PropertyGroup/SharpProofPackageVersion')
    $version = if ($null -eq $versionNode) { '' } else { [string]$versionNode.InnerText }
    $prefixNode = $versions.SelectSingleNode('/Project/PropertyGroup/SharpProofVersionPrefix')
    if ($null -ne $prefixNode) { $version = $version.Replace('$(SharpProofVersionPrefix)', [string]$prefixNode.InnerText) }
    if ([string]::IsNullOrWhiteSpace($version) -or $version.Contains('$')) { throw 'Developer harness requires the exact source package version.' }
    $inputs = foreach ($path in @('global.json', 'eng/container/toolchain.json', 'eng/Versions.props', 'Directory.Build.props', 'Directory.Build.targets', 'NuGet.config')) {
        [ordered]@{ path = $path; sha256 = Get-DeveloperHarnessHash -Path (Join-Path $RepositoryRoot $path) }
    }
    $runId = [string]$env:GITHUB_RUN_ID
    $runAttempt = [string]$env:GITHUB_RUN_ATTEMPT
    if (($env:GITHUB_ACTIONS -ceq 'true' -or $runId.Length -ne 0 -or $runAttempt.Length -ne 0) -and ($runId -cnotmatch '^[1-9][0-9]*$' -or $runAttempt -cnotmatch '^[1-9][0-9]*$')) {
        throw 'Developer harness requires a complete workflow run and attempt identity.'
    }
    if (-not $Producer) {
        $requestedAttempt = [string]$env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT
        if ($requestedAttempt.Length -ne 0) {
            [long]$currentNumber = 0
            [long]$producerNumber = 0
            if ($requestedAttempt -cnotmatch '^[1-9][0-9]*$' -or -not [long]::TryParse($requestedAttempt, [ref]$producerNumber) -or -not [long]::TryParse($runAttempt, [ref]$currentNumber) -or $producerNumber -gt $currentNumber) {
                throw 'Developer harness producer attempt must be canonical, positive, and no later than the current workflow attempt.'
            }
            $runAttempt = $requestedAttempt
        }
    }
    $projects = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($project in (Get-SharpProofDeveloperQualificationBuildProjects -RepositoryRoot $RepositoryRoot -IncludeConsumerHarness)) { $projects.Add([string]$project) }
    return [ordered]@{
        repositoryCommit = $commit
        sourceTree       = $tree
        configuration    = 'Release'
        platform         = 'linux/amd64'
        sdkVersion       = $sdk
        packageVersion   = $version
        runId            = $runId
        runAttempt       = $runAttempt
        buildProjects    = $projects.ToImmutable()
        restore          = 'locked-mode'
        properties       = [ordered]@{
            ContinuousIntegrationBuild  = 'true'
            GeneratePackageOnBuild      = 'false'
            RepositoryCommit            = $commit
            SourceRevisionId            = $commit
            SharpProofDeveloperPackages = 'true'
            SharpProofPackTool          = 'true'
            RunAnalyzersDuringBuild     = 'true'
        }
        inputs           = @($inputs)
    }
}

function Get-DeveloperHarnessPackages([string[]]$PackageSources, [string]$RepositoryCommit, [string]$ExpectedVersion) {
    $files = @(
        foreach ($source in $PackageSources) {
            $root = (Resolve-Path -LiteralPath $source).Path
            foreach ($file in @(Get-DeveloperHarnessFiles -Root $root | Where-Object Extension -In @('.nupkg', '.snupkg'))) {
                if ($file.DirectoryName -cne $root) { throw 'Developer harness package sources must be flat.' }
                $file
            }
        }
    )
    $expected = @('SharpProof.nupkg', 'SharpProof.Attributes.nupkg', 'SharpProof.Attributes.snupkg', 'SharpProof.Verifier.nupkg', 'SharpProof.Verifier.snupkg', 'SharpProof.Tool.nupkg', 'SharpProof.CodeFixes.nupkg')
    if ($files.Count -ne $expected.Count) { throw 'Developer harness requires exactly the seven producer package archives.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $version = ''
    foreach ($file in $files | Sort-Object Name -CaseSensitive) {
        $identity = Get-SharpProofPackageIdentity -Path $file.FullName -RequireSingleIdentity -RequireRepository
        $key = $identity.Id + $file.Extension
        if ($expected -cnotcontains $key -or -not $seen.Add($key) -or $identity.RepositoryCommit -cne $RepositoryCommit -or $identity.Version -cne $ExpectedVersion -or ($version.Length -ne 0 -and $identity.Version -cne $version)) {
            throw 'Developer harness package identities do not match the producer source and package set.'
        }
        $version = $identity.Version
        if ($file.Name -cne ($identity.Id + '.' + $version + $file.Extension)) { throw 'Developer harness package archive name does not match its identity.' }
        [ordered]@{
            path             = $file.Name
            id               = $identity.Id
            version          = $version
            repositoryCommit = $identity.RepositoryCommit
            bytes            = $file.Length
            sha256           = Get-DeveloperHarnessHash -Path $file.FullName
        }
    }
}

function Assert-DeveloperHarnessPayload([object[]]$Files) {
    $paths = @($Files | ForEach-Object { [string]$_.path })
    foreach ($required in @('SharpProof.DeveloperExperience.Test.dll', 'SharpProof.DeveloperExperience.Test.deps.json', 'SharpProof.DeveloperExperience.Test.runtimeconfig.json', 'nunit.framework.dll', 'NUnit3.TestAdapter.dll', 'testhost.dll')) {
        if ($paths -cnotcontains $required) { throw "Developer harness is missing runtime dependency '$required'." }
    }
}

function New-SharpProofDeveloperHarnessReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$PackageSource,
        [Parameter(Mandatory = $true)][string]$ToolPackageSource,
        [Parameter(Mandatory = $true)][string]$PreparedCodeFixesSource,
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][DateTime]$BuildStartedUtc
    )
    if ($BuildStartedUtc.Kind -ne [DateTimeKind]::Utc -or $BuildStartedUtc -gt [DateTime]::UtcNow) { throw 'Developer harness requires a past UTC normal-build start.' }
    $context = Get-DeveloperHarnessContext -RepositoryRoot $RepositoryRoot -Producer
    $files = @(Get-DeveloperHarnessInventory -Root $PayloadDirectory)
    Assert-DeveloperHarnessPayload -Files $files
    foreach ($name in @('SharpProof.DeveloperExperience.Test.dll', 'SharpProof.DeveloperExperience.Test.deps.json', 'SharpProof.DeveloperExperience.Test.runtimeconfig.json')) {
        $file = Get-Item -LiteralPath (Join-Path $PayloadDirectory $name)
        if ($file.Length -eq 0 -or $file.LastWriteTimeUtc -lt $BuildStartedUtc) { throw 'Developer harness output predates the fresh normal producer build.' }
    }
    return [ordered]@{
        schemaVersion   = 1
        context         = $context
        buildStartedUtc = $BuildStartedUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
        assembly        = 'payload/SharpProof.DeveloperExperience.Test.dll'
        testSelection   = 'all-discovered-no-filter'
        packages        = @(Get-DeveloperHarnessPackages -PackageSources @($PackageSource, $ToolPackageSource, $PreparedCodeFixesSource) -RepositoryCommit $context.repositoryCommit -ExpectedVersion $context.packageVersion)
        files           = $files
    }
}

function Test-SharpProofDeveloperHarnessReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$HarnessRoot,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$PackageSource,
        [string]$ToolPackageSource = '',
        [string]$PreparedCodeFixesSource = ''
    )
    $root = (Resolve-Path -LiteralPath $HarnessRoot).Path
    $allFiles = @(Get-DeveloperHarnessFiles -Root $root)
    $receiptPath = Join-Path $root 'receipt.json'
    $json = [IO.File]::ReadAllText($receiptPath)
    $document = [Text.Json.JsonDocument]::Parse($json)
    try {
        Assert-UniqueJsonProperties -Value $document.RootElement -Context 'Developer harness receipt'
        $startToken = $document.RootElement.GetProperty('buildStartedUtc')
        if ($startToken.ValueKind -ne [Text.Json.JsonValueKind]::String) { throw 'Developer harness normal-build time is invalid.' }
        $startedText = $startToken.GetString()
    }
    finally { $document.Dispose() }
    $receipt = ConvertFrom-Json -InputObject $json -AsHashtable
    Assert-SharpProofExactJsonProperties -Actual @($receipt.Keys) -Expected @('schemaVersion', 'context', 'buildStartedUtc', 'assembly', 'testSelection', 'packages', 'files') -Description 'Developer harness receipt'
    if (($receipt.schemaVersion -isnot [int] -and $receipt.schemaVersion -isnot [long]) -or $receipt.schemaVersion -ne 1 -or $receipt.assembly -cne 'payload/SharpProof.DeveloperExperience.Test.dll' -or $receipt.testSelection -cne 'all-discovered-no-filter') {
        throw 'Developer harness schema, assembly, or complete test selection is invalid.'
    }
    [DateTime]$started = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($startedText, 'O', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$started) -or $started.Kind -ne [DateTimeKind]::Utc -or $started -gt [DateTime]::UtcNow) {
        throw 'Developer harness normal-build time is invalid.'
    }
    $expected = Get-DeveloperHarnessContext -RepositoryRoot $RepositoryRoot
    Assert-SharpProofExactJsonProperties -Actual @($receipt.context.Keys) -Expected @($expected.Keys) -Description 'Developer harness context'
    foreach ($key in @('repositoryCommit', 'sourceTree', 'configuration', 'platform', 'sdkVersion', 'packageVersion', 'runId', 'runAttempt', 'restore')) {
        if ($receipt.context[$key] -cne $expected[$key]) { throw "Developer harness context mismatch: $key." }
    }
    if (($receipt.context.buildProjects -join '|') -cne ($expected.buildProjects -join '|')) { throw 'Developer harness producer build roots changed.' }
    Assert-SharpProofExactJsonProperties -Actual @($receipt.context.properties.Keys) -Expected @($expected.properties.Keys) -Description 'Developer harness build properties'
    foreach ($key in $expected.properties.Keys) {
        if ($receipt.context.properties[$key] -cne $expected.properties[$key]) { throw "Developer harness normal build property mismatch: $key." }
    }
    Assert-DeveloperHarnessRecords -Actual @($receipt.context.inputs) -Expected @($expected.inputs) -Description 'source inputs'
    if ([string]::IsNullOrWhiteSpace($ToolPackageSource) -ne [string]::IsNullOrWhiteSpace($PreparedCodeFixesSource)) { throw 'Developer harness separate feed inputs must include both Tool and CodeFixes.' }
    $sources = @($PackageSource)
    if (-not [string]::IsNullOrWhiteSpace($ToolPackageSource)) { $sources += @($ToolPackageSource, $PreparedCodeFixesSource) }
    $packages = @(Get-DeveloperHarnessPackages -PackageSources $sources -RepositoryCommit $expected.repositoryCommit -ExpectedVersion $expected.packageVersion)
    Assert-DeveloperHarnessRecords -Actual @($receipt.packages) -Expected $packages -Description 'packages'
    $payload = Join-Path $root 'payload'
    $files = @(Get-DeveloperHarnessInventory -Root $payload)
    Assert-DeveloperHarnessPayload -Files $files
    foreach ($name in @('SharpProof.DeveloperExperience.Test.dll', 'SharpProof.DeveloperExperience.Test.deps.json', 'SharpProof.DeveloperExperience.Test.runtimeconfig.json')) {
        $file = Get-Item -LiteralPath (Join-Path $payload $name)
        if ($file.Length -eq 0 -or $file.LastWriteTimeUtc -lt $started) { throw 'Developer harness output predates the fresh normal producer build.' }
    }
    Assert-DeveloperHarnessRecords -Actual @($receipt.files) -Expected $files -Description 'payload'
    if ($allFiles.Count -ne $files.Count + 1 -or @($allFiles | Where-Object { $_.FullName -cne $receiptPath -and -not $_.FullName.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) }).Count -ne 0) {
        throw 'Developer harness has unexpected files outside its payload.'
    }
    return [ordered]@{
        AssemblyPath     = Join-Path $root $receipt.assembly
        PayloadDirectory = $payload
        ReceiptSha256    = Get-DeveloperHarnessHash -Path $receiptPath
        RepositoryCommit = $expected.repositoryCommit
        Files            = $files
    }
}

function Assert-SharpProofDeveloperHarnessResults {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ResultsDirectory)
    $cases = @(Read-SharpProofCanonicalCases -Root $ResultsDirectory)
    $discovery = @(Read-SharpProofCanonicalDiscovery -Root $ResultsDirectory -ExcludedCategories @())
    if (@($cases | Where-Object { $_.assembly -cne 'SharpProof.DeveloperExperience.Test.dll' -or $_.outcome -cne 'Passed' }).Count -ne 0 -or @($discovery | Where-Object { $_.assembly -cne 'SharpProof.DeveloperExperience.Test.dll' }).Count -ne 0) {
        throw 'Developer harness must execute every discovered developer case successfully.'
    }
    $expectedNames = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $observedNames = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $expectedNames.AddRange([string[]]@(Get-SharpProofTrxDiscoveryNames -Discovery $discovery))
    $observedNames.AddRange([string[]]@($cases | ForEach-Object { $_.assembly + '|' + $_.fullyQualifiedName }))
    $expectedNames.Sort([StringComparer]::Ordinal)
    $observedNames.Sort([StringComparer]::Ordinal)
    if ($expectedNames.Count -ne $observedNames.Count) { throw 'Developer harness discovery and execution case counts differ.' }
    for ($index = 0; $index -lt $expectedNames.Count; $index++) {
        if ($expectedNames[$index] -cne $observedNames[$index]) { throw 'Developer harness discovery and execution case identities differ.' }
    }
    return [ordered]@{ discovered = $discovery.Count; cases = $cases.Count; passed = $cases.Count; notExecuted = 0 }
}

function Invoke-SharpProofDeveloperHarnessTests {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$HarnessRoot,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$PackageSource,
        [Parameter(Mandatory = $true)][string]$ResultsDirectory
    )
    $validated = Test-SharpProofDeveloperHarnessReceipt -HarnessRoot $HarnessRoot -RepositoryRoot $RepositoryRoot -PackageSource $PackageSource
    $results = Resolve-SharpProofContainedPath -Root $RepositoryRoot -Path $ResultsDirectory -ParameterName 'ResultsDirectory'
    if (Test-Path -LiteralPath $results) { throw 'Developer harness requires a new evidence directory.' }
    $runtimeRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-developer-harness-' + [Guid]::NewGuid().ToString('N'))
    $runtime = Join-Path $runtimeRoot 'net10.0'
    try {
        # The downloaded payload remains immutable. NUnit creates its own Dump
        # directory, so discovery and execution use an authenticated private copy.
        [IO.Directory]::CreateDirectory($runtime) | Out-Null
        foreach ($file in $validated.Files) {
            $destination = Join-Path $runtime $file.path
            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            [IO.File]::Copy((Join-Path $validated.PayloadDirectory $file.path), $destination, $false)
        }
        $copied = @(Get-DeveloperHarnessInventory -Root $runtime)
        Assert-DeveloperHarnessRecords -Actual $validated.Files -Expected $copied -Description 'private runtime copy'
        $taskRoot = Join-Path $results 'all-tests'
        $dumpRoot = Join-Path $taskRoot 'discovery'
        [IO.Directory]::CreateDirectory($dumpRoot) | Out-Null
        $settings = Join-Path $taskRoot 'discovery.runsettings'
        [IO.File]::WriteAllText($settings, '<RunSettings><NUnit><DumpXmlTestDiscovery>true</DumpXmlTestDiscovery></NUnit></RunSettings>', [Text.UTF8Encoding]::new($false))
        $assembly = Join-Path $runtime 'SharpProof.DeveloperExperience.Test.dll'
        Invoke-SharpProofRequiredDotnet -Arguments @('vstest', $assembly, ('/TestAdapterPath:' + $runtime), '/ListFullyQualifiedTests', ('/Settings:' + $settings), ('/ListTestsTargetPath:' + (Join-Path $taskRoot 'discovered-tests.txt'))) | Out-Host
        $dumpName = 'D_SharpProof.DeveloperExperience.Test.dll.dump'
        $dumpPath = Join-Path $runtime ('Dump/' + $dumpName)
        if (-not (Test-Path -LiteralPath $dumpPath -PathType Leaf)) { throw 'Developer harness discovery did not produce its actual NUnit case dump.' }
        Copy-Item -LiteralPath $dumpPath -Destination (Join-Path $dumpRoot 'SharpProof.DeveloperExperience.Test.dll.discovery.dump')
        # Deliberately no test filter or category exclusion: preserve dotnet
        # test's complete DeveloperExperience test universe.
        Invoke-SharpProofRequiredDotnet -Arguments @('vstest', $assembly, ('/TestAdapterPath:' + $runtime), ('/Settings:' + $settings), ('/ResultsDirectory:' + $taskRoot), '/Logger:trx;LogFileName=developer-tests.trx') | Out-Host
        $result = Assert-SharpProofDeveloperHarnessResults -ResultsDirectory $results
        $after = @(Get-DeveloperHarnessInventory -Root $runtime)
        $runtimeFiles = @($after | Where-Object { $_.path -cne ('Dump/' + $dumpName) })
        Assert-DeveloperHarnessRecords -Actual $validated.Files -Expected $runtimeFiles -Description 'executed runtime'
        $revalidated = Test-SharpProofDeveloperHarnessReceipt -HarnessRoot $HarnessRoot -RepositoryRoot $RepositoryRoot -PackageSource $PackageSource
        if ($revalidated.ReceiptSha256 -cne $validated.ReceiptSha256) { throw 'Developer harness receipt changed during execution.' }
        $qualification = [ordered]@{ schemaVersion = 1; commit = $validated.RepositoryCommit; receiptSha256 = $validated.ReceiptSha256; selection = 'all-discovered-no-filter'; discovered = $result.discovered; cases = $result.cases; passed = $result.passed; notExecuted = $result.notExecuted }
        [IO.File]::WriteAllText((Join-Path $results 'qualification.json'), ($qualification | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
        Write-Host "Developer harness passed all $($result.cases) discovered cases from the exact producer build."
    }
    finally {
        if ([IO.Directory]::Exists($runtimeRoot)) { [IO.Directory]::Delete($runtimeRoot, $true) }
    }
}

Export-ModuleMember -Function New-SharpProofDeveloperHarnessReceipt, Test-SharpProofDeveloperHarnessReceipt, Assert-SharpProofDeveloperHarnessResults, Invoke-SharpProofDeveloperHarnessTests
