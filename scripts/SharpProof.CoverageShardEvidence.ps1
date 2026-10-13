Set-StrictMode -Version Latest

function Save-SharpProofCoverageCohort {
    param([string]$Root, [object[]]$Expected, [object[]]$Completed, [string]$ProjectPath = '')

    $expectedNames = @($Expected | ForEach-Object { [string]$_.Name } | Sort-Object)
    $completedNames = @($Completed | ForEach-Object { [string]$_.Test.Name } | Sort-Object)
    if ($expectedNames.Count -eq 0 -or
        @($completedNames | Select-Object -Unique).Count -ne $expectedNames.Count -or
        ($expectedNames -join "`n") -cne ($completedNames -join "`n") -or
        @($Completed | Where-Object { $_.ExitCode -ne 0 }).Count -ne 0) {
        throw 'Coverage cohort did not complete every planned task successfully.'
    }
    foreach ($name in $expectedNames) {
        $taskRoot = Join-Path $Root $name
        $receipts = @(Get-ChildItem -LiteralPath $taskRoot -Recurse -Filter '*.trx' -File)
        if ($receipts.Count -eq 0) { throw "Coverage task '$name' has no TRX receipt." }
        $descriptor = @($Expected | Where-Object { $_.Name -ceq $name })[0]
        $target = if ($descriptor.PSObject.Properties.Name -contains 'Target') { [string]$descriptor.Target } else { $ProjectPath }
        $requiredProjects = if ($target.EndsWith('.slnf', [StringComparison]::Ordinal)) {
            @((Get-Content -LiteralPath $target -Raw | ConvertFrom-Json).solution.projects)
        }
        elseif ($target.EndsWith('.csproj', [StringComparison]::Ordinal)) { @($target) }
        else { @() }
        $requiredAssemblies = @($requiredProjects | ForEach-Object {
            [IO.Path]::GetFileNameWithoutExtension(([string]$_).Replace('\', '/')) + '.dll'
        } | Sort-Object -Unique)
        $observedAssemblies = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($receipt in $receipts) {
            [xml]$trx = Get-Content -LiteralPath $receipt.FullName -Raw
            $counters = $trx.SelectSingleNode('//*[local-name()="Counters"]')
            if ($null -eq $counters -or [int]$counters.total -eq 0 -or
                [int]$counters.failed -ne 0 -or [int]$counters.error -ne 0) {
                throw "Coverage task '$name' has an empty or failed TRX receipt."
            }
            $definitions = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
            foreach ($definition in $trx.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]')) {
                $method = $definition.SelectSingleNode('*[local-name()="TestMethod"]')
                if ($null -eq $method -or [string]::IsNullOrWhiteSpace([string]$definition.id)) {
                    throw "Coverage task '$name' has an invalid test definition."
                }
                $assembly = [IO.Path]::GetFileName(([string]$method.codeBase).Replace('\', '/'))
                $id = [string]$definition.id
                if ($definitions.ContainsKey($id) -and $definitions[$id] -cne $assembly) {
                    throw "Coverage task '$name' has inconsistent test definitions."
                }
                $definitions[$id] = $assembly
            }
            $results = @($trx.SelectNodes('//*[local-name()="UnitTestResult"]'))
            if ($results.Count -ne [int]$counters.total) {
                throw "Coverage task '$name' result count differs from its TRX counters."
            }
            foreach ($result in $results) {
                $id = [string]$result.testId
                if (-not $definitions.ContainsKey($id) -or
                    [string]$result.outcome -cnotin @('Passed', 'NotExecuted')) {
                    throw "Coverage task '$name' has a dangling or unsuccessful test result."
                }
                if ([string]$result.outcome -ceq 'Passed') {
                    [void]$observedAssemblies.Add($definitions[$id])
                }
            }
            $attachments = @($trx.SelectNodes('//*[local-name()="UriAttachment"]/*[local-name()="A"]') |
                Where-Object { [string]$_.href -like '*.cobertura.xml' })
            if ($attachments.Count -eq 0) { throw "Coverage task '$name' has no collector attachment." }
            foreach ($attachment in $attachments) {
                $report = Join-Path $receipt.DirectoryName ([string]$attachment.href)
                $deployment = $trx.SelectSingleNode('//*[local-name()="Deployment"]')
                if ($null -ne $deployment) {
                    $report = Join-Path $receipt.DirectoryName (
                        [string]$deployment.runDeploymentRoot + '/In/' + [string]$attachment.href)
                }
                if (-not (Test-Path -LiteralPath $report -PathType Leaf)) {
                    throw "Coverage task '$name' is missing a collector attachment."
                }
            }
        }
        foreach ($assembly in $requiredAssemblies) {
            if (-not $observedAssemblies.Contains($assembly)) {
                throw "Coverage task '$name' has no test receipt for expected assembly '$assembly'."
            }
        }
    }
    [ordered]@{ schemaVersion = 1; tasks = $expectedNames } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Root 'coverage-cohort.json') -Encoding utf8NoBOM
}

function Save-SharpProofCoverageShardManifest {
    param([string]$Root, [string]$Phase)

    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Name -eq 'coverage-authority.json' -or $_.Name -eq 'coverage-cohort.json' -or
            $_.Name -like '*.trx' -or $_.Name -like '*.cobertura.xml' } |
        Sort-Object FullName | ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })
    if (@($files | Where-Object { $_.path -like '*.cobertura.xml' }).Count -eq 0 -or
        @($files | Where-Object { $_.path -like '*/coverage-cohort.json' }).Count -ne 1) {
        throw "Coverage shard '$Phase' has no complete cohort evidence."
    }
    [ordered]@{ schemaVersion = 1; phase = $Phase; files = $files } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Root 'coverage-shard.json') -Encoding utf8NoBOM
}

function Assert-SharpProofCoverageShardManifest {
    param([string]$Root, [string]$Phase)

    $manifestPath = Join-Path $Root 'coverage-shard.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Coverage shard '$Phase' is missing its completion manifest."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.phase -cne $Phase) {
        throw "Coverage shard '$Phase' has an invalid completion manifest."
    }
    $actual = @(Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Name -eq 'coverage-authority.json' -or $_.Name -eq 'coverage-cohort.json' -or
            $_.Name -like '*.trx' -or $_.Name -like '*.cobertura.xml' } |
        ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/') } | Sort-Object)
    $recorded = @($manifest.files | ForEach-Object { [string]$_.path } | Sort-Object)
    if ($actual.Count -eq 0 -or ($actual -join "`n") -cne ($recorded -join "`n") -or
        @($recorded | Select-Object -Unique).Count -ne $recorded.Count -or
        @($recorded | Where-Object { $_ -like '*.cobertura.xml' }).Count -eq 0 -or
        @($recorded | Where-Object { $_ -like '*/coverage-cohort.json' }).Count -ne 1 -or
        @($recorded | Where-Object { $_ -eq 'coverage-authority.json' }).Count -ne 1 -or
        ($Phase -eq 'semantic' -and $recorded -cnotcontains 'coverage-cohort.json')) {
        throw "Coverage shard '$Phase' evidence file set differs from its completion manifest."
    }
    foreach ($file in $manifest.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $Root $file.path) -Algorithm SHA256).Hash -cne $file.sha256) {
            throw "Coverage shard '$Phase' evidence hash differs: $($file.path)"
        }
    }
}
