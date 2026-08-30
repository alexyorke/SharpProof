Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-SharpProofStaticGraphArgument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    if ($Arguments.Count -lt 2 -or
        $Arguments[0] -notin @('build', 'test') -or
        [IO.Path]::GetExtension($Arguments[1]) -notin @('.sln', '.slnf') -or
        $Arguments -contains '-graphBuild' -or
        $Arguments -contains '/graphBuild') {
        return $Arguments
    }

    return @($Arguments) + '-graphBuild'
}

function Get-SharpProofTestProjectParallelism {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $override = [Environment]::GetEnvironmentVariable(
        'SHARPPROOF_TEST_PROJECT_PARALLELISM',
        [EnvironmentVariableTarget]::Process)
    $visibleProcessors = [Environment]::ProcessorCount
    if ($visibleProcessors -lt 1) {
        throw 'The container did not expose a positive processor count.'
    }

    if (-not [string]::IsNullOrWhiteSpace($override)) {
        $value = 0
        if (-not [int]::TryParse(
                $override,
                [Globalization.NumberStyles]::None,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$value) -or
            $value -lt 1 -or
            $value -gt $visibleProcessors) {
            throw (
                'SHARPPROOF_TEST_PROJECT_PARALLELISM must be an integer ' +
                "between 1 and the container-visible CPU count " +
                "($visibleProcessors).")
        }
        return $value
    }

    $contract = Get-Content -LiteralPath (Join-Path `
        $RepositoryRoot 'eng/acceptance/contract.json') -Raw |
        ConvertFrom-Json
    $divisor = [int]$contract.automation.testProjectCpuDivisor
    if ($divisor -lt 1) {
        throw 'The test-project CPU divisor must be positive.'
    }

    return [Math]::Max(1, [Math]::Floor($visibleProcessors / $divisor))
}

function Get-SharpProofBuildParallelism {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $sharedOverride = [Environment]::GetEnvironmentVariable(
        'SHARPPROOF_TEST_PROJECT_PARALLELISM',
        [EnvironmentVariableTarget]::Process)
    if (-not [string]::IsNullOrWhiteSpace($sharedOverride)) {
        return Get-SharpProofTestProjectParallelism `
            -RepositoryRoot $RepositoryRoot
    }

    $visibleProcessors = [Environment]::ProcessorCount
    if ($visibleProcessors -lt 1) {
        throw 'The container did not expose a positive processor count.'
    }
    $contract = Get-Content -LiteralPath (Join-Path `
        $RepositoryRoot 'eng/acceptance/contract.json') -Raw |
        ConvertFrom-Json
    $percent = [int]$contract.automation.buildCpuPercent
    if ($percent -lt 1 -or $percent -gt 100) {
        throw 'The build CPU percentage must be between 1 and 100.'
    }

    return [Math]::Max(
        1,
        [Math]::Floor($visibleProcessors * $percent / 100.0))
}

function Get-SharpProofTestAssemblyPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration
    )

    $normalizedProjectPath = $ProjectPath.Replace('\', '/')
    $project = if ([IO.Path]::IsPathRooted($normalizedProjectPath)) {
        [IO.Path]::GetFullPath($normalizedProjectPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path (Get-Location) $normalizedProjectPath))
    }
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Test project was not found: '$ProjectPath'."
    }

    [xml]$document = Get-Content -LiteralPath $project -Raw
    $frameworks = @(
        $document.SelectNodes("//*[local-name()='TargetFramework']") |
            ForEach-Object { [string]$_.InnerText } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($frameworks.Count -ne 1) {
        throw (
            'Direct vstest requires exactly one TargetFramework in ' +
            "'$ProjectPath'; use dotnet test for multi-target projects.")
    }
    $assemblyName = @(
        $document.SelectNodes("//*[local-name()='AssemblyName']") |
            ForEach-Object { [string]$_.InnerText } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -First 1)
    if ($assemblyName.Count -eq 0) {
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)
    }
    $assembly = Join-Path (Split-Path -Parent $project) (
        'bin/' + $Configuration + '/' + $frameworks[0] + '/' +
        $assemblyName + '.dll')
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw (
            "Built test assembly was not found at '$assembly'; run a " +
            'matching build before using -NoBuild.')
    }
    return [IO.Path]::GetFullPath($assembly)
}

function Invoke-SharpProofParallelDotnetBuilds {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Builds,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 1024)]
        [int]$Parallelism,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 86400)]
        [int]$TimeoutSeconds
    )

    if ($Builds.Count -eq 0) {
        return
    }
    if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
        throw 'Parallel builds require the canonical Linux container.'
    }

    $lanesPerBuild = [Math]::Max(
        1,
        [Math]::Floor($Parallelism / $Builds.Count))
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $running = [Collections.Generic.List[object]]::new()
    try {
        foreach ($build in $Builds) {
            $name = [string]$build.Name
            $arguments = @($build.Arguments)
            if ([string]::IsNullOrWhiteSpace($name) -or
                $arguments.Count -lt 2 -or
                [string]$arguments[0] -cne 'build') {
                throw 'Parallel build entries require a name and build arguments.'
            }
            $arguments += @(
                "/m:$lanesPerBuild",
                '/nodeReuse:false',
                '-p:UseSharedCompilation=false')
            $effectiveArguments = @(
                Add-SharpProofStaticGraphArgument -Arguments $arguments)
            $startInfo = [Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = 'dotnet'
            $startInfo.WorkingDirectory = $RepositoryRoot
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.Environment['UseSharedCompilation'] = 'false'
            $startInfo.Environment['MSBUILDDISABLENODEREUSE'] = '1'
            foreach ($argument in $effectiveArguments) {
                [void]$startInfo.ArgumentList.Add($argument)
            }
            $process = [Diagnostics.Process]::new()
            $process.StartInfo = $startInfo
            if (-not $process.Start()) {
                $process.Dispose()
                throw "Could not start build $name."
            }
            $running.Add([pscustomobject]@{
                Name = $name
                Arguments = $effectiveArguments
                Process = $process
                StandardOutput = $process.StandardOutput.ReadToEndAsync()
                StandardError = $process.StandardError.ReadToEndAsync()
            })
        }

        foreach ($active in $running) {
            $remaining = $deadline - [DateTime]::UtcNow
            if ($remaining -le [TimeSpan]::Zero -or
                -not $active.Process.WaitForExit(
                    [int][Math]::Ceiling($remaining.TotalMilliseconds))) {
                throw "Parallel builds exceeded $TimeoutSeconds seconds."
            }
        }

        $failures = [Collections.Generic.List[string]]::new()
        foreach ($active in $running) {
            $stdout = $active.StandardOutput.GetAwaiter().GetResult()
            $stderr = $active.StandardError.GetAwaiter().GetResult()
            Write-Host "--- Build $($active.Name) ---"
            if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                Write-Host $stdout.TrimEnd()
            }
            if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                Write-Host $stderr.TrimEnd()
            }
            if ($active.Process.ExitCode -ne 0) {
                $failures.Add(
                    "$($active.Name) exited $($active.Process.ExitCode): " +
                    ($active.Arguments -join ' '))
            }
        }
        if ($failures.Count -ne 0) {
            throw "Parallel builds failed:`n$($failures -join "`n")"
        }
    }
    finally {
        foreach ($active in $running) {
            if (-not $active.Process.HasExited) {
                $active.Process.Kill($true)
                $active.Process.WaitForExit()
            }
            $active.Process.Dispose()
        }
    }
}

function New-SharpProofIsolatedTestOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
        throw 'Isolated test outputs require the canonical Linux container.'
    }
    $source = (Resolve-Path `
        -LiteralPath $SourceDirectory `
        -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Test output source is not a directory: $source"
    }
    $destination = [IO.Path]::GetFullPath($DestinationDirectory)
    if (Test-Path -LiteralPath $destination) {
        throw "Isolated test output already exists: $destination"
    }
    [IO.Directory]::CreateDirectory(
        [IO.Path]::GetDirectoryName($destination)) | Out-Null

    & /bin/cp --archive --link -- $source $destination
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Could not hard-link the isolated test output at ' +
            "$destination.")
    }

    # Static managed coverage replaces instrumented assemblies on disk. Keep
    # immutable dependencies hard-linked, but give every collector process its
    # own managed binaries and PDBs so instrumentation and restoration cannot
    # race another shard.
    foreach ($file in Get-ChildItem `
            -LiteralPath $destination `
            -Recurse `
            -File `
            -ErrorAction Stop | Where-Object {
                $_.Extension -in @('.dll', '.pdb')
            }) {
        $temporary =
            $file.FullName + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        [IO.File]::Copy($file.FullName, $temporary, $false)
        Move-Item `
            -LiteralPath $temporary `
            -Destination $file.FullName `
            -Force
    }

    return $destination
}

Export-ModuleMember -Function @(
    'Add-SharpProofStaticGraphArgument',
    'Get-SharpProofBuildParallelism',
    'Get-SharpProofTestProjectParallelism',
    'Get-SharpProofTestAssemblyPath',
    'Invoke-SharpProofParallelDotnetBuilds',
    'New-SharpProofIsolatedTestOutput')
