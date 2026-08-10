[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$TestFilter = '',

    [string]$PackageSource = '',

    [switch]$NoBuild,

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Package tests require the canonical Linux container.'
}

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$parallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
$testProject = Join-Path `
    $repositoryRoot 'SharpProof.Package.Test/SharpProof.Package.Test.csproj'

function Invoke-RequiredDotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-package-tests-' + [Guid]::NewGuid().ToString('N'))
$feed = if ([string]::IsNullOrWhiteSpace($PackageSource)) {
    Join-Path $root 'feed'
}
else {
    (Resolve-Path -LiteralPath $PackageSource -ErrorAction Stop).Path
}
$results = Join-Path $root 'results'
if ([string]::IsNullOrWhiteSpace($PackageSource)) {
    [IO.Directory]::CreateDirectory($feed) | Out-Null
}
[IO.Directory]::CreateDirectory($results) | Out-Null

try {
    if (-not $NoBuild) {
        Invoke-RequiredDotnet @(
            'restore', 'SharpProof.sln', '--locked-mode')
        Invoke-RequiredDotnet @(
            'build', $testProject, '-c', $Configuration, '--no-restore')
    }

    if ([string]::IsNullOrWhiteSpace($PackageSource)) {
        $packageManifest = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot 'scripts/package-projects.json') -Raw |
            ConvertFrom-Json
        foreach ($project in @($packageManifest.projects)) {
            Invoke-RequiredDotnet @(
                'pack', [string]$project, '-c', 'Release', '--no-restore',
                $(if ($NoBuild) { '--no-build' } else { '--nologo' }),
                '--output', $feed, '/p:GeneratePackageOnBuild=false')
        }
    }

    $workerClass =
        'SharpProof.Package.Test.WorkerMsBuildIntegrationTests'
    $workerList = & dotnet test $testProject `
        -c $Configuration `
        --no-build `
        --no-restore `
        --list-tests `
        --filter "FullyQualifiedName~$workerClass" `
        /nodeReuse:false `
        -p:UseSharedCompilation=false 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not discover Worker MSBuild integration tests.'
    }
    $workerMethods = @(
        [regex]::Matches(
            $workerList,
            '(?m)^\s{4}(?<method>[A-Za-z_][A-Za-z0-9_]*)(?:\(|\s*$)') |
            ForEach-Object { $_.Groups['method'].Value } |
            Sort-Object -Unique)
    if ($workerMethods.Count -lt 40) {
        throw (
            'Worker MSBuild integration discovery returned only ' +
            "$($workerMethods.Count) test methods.")
    }
    $workerBuckets = @(
        for ($index = 0; $index -lt $parallelism; $index++) {
            , [Collections.Generic.List[string]]::new()
        })
    for ($index = 0; $index -lt $workerMethods.Count; $index++) {
        $workerBuckets[$index % $parallelism].Add(
            "FullyQualifiedName~$workerClass.$($workerMethods[$index])")
    }
    $workerFilters = @(
        $workerBuckets |
            Where-Object Count -gt 0 |
            ForEach-Object { $_ -join '|' })

    $filters = @(
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $TestFilter
        }
        else {
            'FullyQualifiedName~SharpProof.Package.Test.BuildTaskTests',
            'FullyQualifiedName~SharpProof.Package.Test.DependencyAuditScriptTests',
            'FullyQualifiedName~SharpProof.Package.Test.FinalCompilationProbeTests',
            'FullyQualifiedName~SharpProof.Package.Test.LauncherArgumentTests',
            'FullyQualifiedName~SharpProof.Package.Test.PackageLayoutSmokeTests',
            'FullyQualifiedName~SharpProof.Package.Test.ReleasePublicationScriptTests'
            $workerFilters
        }
    )

    $pending = [Collections.Generic.Queue[object]]::new()
    for ($index = 0; $index -lt $filters.Count; $index++) {
        $pending.Enqueue([pscustomobject]@{
            Index = $index
            Filter = [string]$filters[$index]
            Name = 'shard-' + ($index + 1).ToString(
                'D2', [Globalization.CultureInfo]::InvariantCulture)
        })
    }
    $running = [Collections.Generic.List[object]]::new()
    $failures = [Collections.Generic.List[string]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($pending.Count -gt 0 -and $running.Count -lt $parallelism) {
            $shard = $pending.Dequeue()
            $startInfo = [Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = 'dotnet'
            $startInfo.WorkingDirectory = $repositoryRoot
            $startInfo.UseShellExecute = $false
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.Environment['SHARPPROOF_PACKAGE_SOURCE'] = $feed
            foreach ($argument in @(
                    'test', $testProject, '-c', $Configuration,
                    '--no-build', '--no-restore', '--filter', $shard.Filter,
                    '--logger', 'console;verbosity=minimal',
                    '--results-directory', (Join-Path $results $shard.Name),
                    '/nodeReuse:false', '-p:UseSharedCompilation=false')) {
                [void]$startInfo.ArgumentList.Add($argument)
            }
            $process = [Diagnostics.Process]::new()
            $process.StartInfo = $startInfo
            if (-not $process.Start()) {
                $process.Dispose()
                throw "Could not start package test $($shard.Name)."
            }
            $running.Add([pscustomobject]@{
                Shard = $shard
                Process = $process
                StandardOutput = $process.StandardOutput.ReadToEndAsync()
                StandardError = $process.StandardError.ReadToEndAsync()
            })
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            foreach ($active in @($running)) {
                if (-not $active.Process.HasExited) {
                    $active.Process.Kill($true)
                }
            }
            throw "Parallel package tests exceeded $TimeoutSeconds seconds."
        }

        $completed = @($running | Where-Object { $_.Process.HasExited })
        if ($completed.Count -eq 0) {
            Start-Sleep -Milliseconds 100
            continue
        }
        foreach ($active in $completed) {
            $active.Process.WaitForExit()
            $stdout = $active.StandardOutput.GetAwaiter().GetResult()
            $stderr = $active.StandardError.GetAwaiter().GetResult()
            Write-Host "--- Package test $($active.Shard.Name) ---"
            if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                Write-Host $stdout.TrimEnd()
            }
            if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                Write-Host $stderr.TrimEnd()
            }
            if ($active.Process.ExitCode -ne 0) {
                $failures.Add(
                    "$($active.Shard.Name) exited $($active.Process.ExitCode): " +
                    $active.Shard.Filter)
            }
            [void]$running.Remove($active)
            $active.Process.Dispose()
        }
    }

    if ($failures.Count -ne 0) {
        throw "Package test shards failed:`n$($failures -join "`n")"
    }
    Write-Host (
        "Package tests passed in $($filters.Count) isolated shard(s) " +
        "with parallelism $parallelism.")
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
}
