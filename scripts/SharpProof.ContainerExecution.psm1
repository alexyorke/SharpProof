Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofDotnetWrapperPath {
    param()

    return Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
}

function Invoke-SharpProofDotnetInvocation {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,

        [AllowEmptyString()]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [ref]$ExitCode
    )

    if ([string]::IsNullOrEmpty($OutputPath)) {
        & (Get-SharpProofDotnetWrapperPath) `
            -TimeoutSeconds $TimeoutSeconds @Arguments
    }
    else {
        & (Get-SharpProofDotnetWrapperPath) `
            -TimeoutSeconds $TimeoutSeconds `
            -OutputPath $OutputPath @Arguments
    }
    $ExitCode.Value = $LASTEXITCODE
}

function Invoke-SharpProofRequiredDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,

        [switch]$Quiet
    )

    $outputPath = $null
    if ($Quiet) {
        $outputPath = Join-Path ([IO.Path]::GetTempPath()) (
            'sharpproof-dotnet-' + [Guid]::NewGuid().ToString('N') + '.log')
    }

    try {
        $exitCode = 0
        Invoke-SharpProofDotnetInvocation `
            -Arguments $Arguments `
            -TimeoutSeconds $TimeoutSeconds `
            -OutputPath $outputPath `
            -ExitCode ([ref]$exitCode)
        if ($exitCode -ne 0) {
            if ($Quiet -and
                (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
                $output = Get-Content -LiteralPath $outputPath -Raw
                if (-not [string]::IsNullOrWhiteSpace($output)) {
                    Write-Host $output.TrimEnd()
                }
            }
            throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode."
        }
    }
    finally {
        if ($null -ne $outputPath) {
            Remove-Item -LiteralPath $outputPath `
                -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-SharpProofCheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SharpProofGitText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage,

        [switch]$MergeErrorOutput,

        [switch]$TrimOutput
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add('-C')
    $startInfo.ArgumentList.Add($RepositoryRoot)
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw $FailureMessage
        }
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $outputTask.GetAwaiter().GetResult()
        $errorOutput = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            $details = (@($output, $errorOutput) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object { $_.Trim() }) -join [Environment]::NewLine
            if ([string]::IsNullOrWhiteSpace($details)) {
                throw $FailureMessage
            }
            throw "$FailureMessage $details"
        }
        $text = if ($MergeErrorOutput) {
            [string]$output + [string]$errorOutput
        }
        else {
            [string]$output
        }
        if ($TrimOutput) {
            return $text.Trim()
        }
        return $text
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-SharpProofTimedPhase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [Collections.IList]$Timings,

        [switch]$RecordOnFailure
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $completed = $false
    try {
        & $Action
        $completed = $true
    }
    finally {
        $timer.Stop()
        if ($completed -or $RecordOnFailure) {
            $Timings.Add([pscustomobject]@{
                name = $Name
                elapsedMilliseconds = [long]$timer.Elapsed.TotalMilliseconds
            })
        }
    }
}

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

function Get-SharpProofParallelismOverride {
    param(
        [AllowEmptyString()][string]$Value,
        [Parameter(Mandatory = $true)][int]$VisibleProcessors,
        [Parameter(Mandatory = $true)][string]$VariableName
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }
    $parsed = 0
    if (-not [int]::TryParse($Value, [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed) -or
        $parsed -lt 1 -or $parsed -gt $VisibleProcessors) {
        throw (
            "$VariableName must be an integer between 1 and the " +
            "container-visible CPU count ($VisibleProcessors).")
    }
    return $parsed
}

function Get-SharpProofCpuBudget {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string[]]$OverrideVariables,
        [string]$DivisorProperty,
        [string]$PercentProperty,
        [string]$InvalidMessage,
        [switch]$AllVisible
    )

    $visibleProcessors = [Environment]::ProcessorCount
    if ($visibleProcessors -lt 1) {
        throw 'The container did not expose a positive processor count.'
    }
    foreach ($variable in $OverrideVariables) {
        $value = [Environment]::GetEnvironmentVariable(
            $variable, [EnvironmentVariableTarget]::Process)
        $override = Get-SharpProofParallelismOverride `
            $value $visibleProcessors $variable
        if ($null -ne $override) {
            return $override
        }
    }
    if ($AllVisible) {
        return $visibleProcessors
    }

    $contract = Get-Content -LiteralPath (Join-Path `
        $RepositoryRoot 'eng/acceptance/contract.json') -Raw |
        ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace($DivisorProperty)) {
        $divisor = [int]$contract.automation.$DivisorProperty
        if ($divisor -lt 1) {
            throw $InvalidMessage
        }
        return [Math]::Max(
            1, [Math]::Floor($visibleProcessors / $divisor))
    }

    $percent = [int]$contract.automation.$PercentProperty
    if ($percent -lt 1 -or $percent -gt 100) {
        throw $InvalidMessage
    }
    return [Math]::Max(
        1, [Math]::Floor($visibleProcessors * $percent / 100.0))
}

$script:SharpProofParallelismPolicies = @{
    'test-project' = @{
        OverrideVariables = @('SHARPPROOF_TEST_PROJECT_PARALLELISM')
        DivisorProperty = 'testProjectCpuDivisor'
        InvalidMessage = 'The test-project CPU divisor must be positive.'
    }
    semantic = @{
        OverrideVariables = @(
            'SHARPPROOF_SEMANTIC_TEST_PARALLELISM',
            'SHARPPROOF_TEST_PROJECT_PARALLELISM')
        AllVisible = $true
    }
    package = @{
        OverrideVariables = @('SHARPPROOF_TEST_PROJECT_PARALLELISM')
        PercentProperty = 'packageTestCpuPercent'
        InvalidMessage =
            'The package-test CPU percentage must be between 1 and 100.'
    }
    build = @{
        OverrideVariables = @('SHARPPROOF_TEST_PROJECT_PARALLELISM')
        PercentProperty = 'buildCpuPercent'
        InvalidMessage =
            'The build CPU percentage must be between 1 and 100.'
    }
}

function Get-SharpProofConfiguredParallelism {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Policy
    )

    $parameters = @{
        RepositoryRoot = $RepositoryRoot
    }
    foreach ($entry in $script:SharpProofParallelismPolicies[$Policy].GetEnumerator()) {
        $parameters[$entry.Key] = $entry.Value
    }
    return Get-SharpProofCpuBudget @parameters
}

function Get-SharpProofTestProjectParallelism {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    return Get-SharpProofConfiguredParallelism `
        $RepositoryRoot 'test-project'
}

function Get-SharpProofSemanticTestParallelism {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    return Get-SharpProofConfiguredParallelism `
        $RepositoryRoot 'semantic'
}

function Get-SharpProofPackageTestParallelism {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    return Get-SharpProofConfiguredParallelism `
        $RepositoryRoot 'package'
}

function Get-SharpProofBuildParallelism {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    return Get-SharpProofConfiguredParallelism `
        $RepositoryRoot 'build'
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

function Stop-SharpProofCompilerServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SharedCompilationId
    )

    $dotnetCommand = Get-Command `
        dotnet `
        -CommandType Application `
        -ErrorAction Stop | Select-Object -First 1
    $dotnetItem = Get-Item -LiteralPath $dotnetCommand.Source
    $dotnetTarget = $dotnetItem.ResolveLinkTarget($true)
    $dotnetPath = if ($null -eq $dotnetTarget) {
        $dotnetItem.FullName
    }
    else {
        $dotnetTarget.FullName
    }
    $sdkVersionOutput = @(& $dotnetPath --version)
    if ($LASTEXITCODE -ne 0 -or $sdkVersionOutput.Count -ne 1) {
        throw 'Could not resolve the active .NET SDK compiler server.'
    }
    $sdkVersion = ([string]$sdkVersionOutput[0]).Trim()
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw 'The active .NET SDK version was empty.'
    }
    $compilerServer = Join-Path `
        ([IO.Path]::GetDirectoryName($dotnetPath)) `
        "sdk/$sdkVersion/Roslyn/bincore/VBCSCompiler.dll"
    if (-not (Test-Path -LiteralPath $compilerServer -PathType Leaf)) {
        throw "The active Roslyn compiler server was not found: $compilerServer"
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnetPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            'exec',
            $compilerServer,
            "-pipename:$SharedCompilationId",
            '-shutdown')) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Could not stop compiler server $SharedCompilationId."
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Compiler server $SharedCompilationId did not stop promptly."
        }
        $stdout = $standardOutput.GetAwaiter().GetResult()
        $stderr = $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw (
                "Compiler server $SharedCompilationId shutdown failed with " +
                "exit code $($process.ExitCode): $stdout$stderr")
        }
    }
    finally {
        $process.Dispose()
    }
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
        [int]$TimeoutSeconds,

        [switch]$Quiet
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
    $compilerServerScope = [Guid]::NewGuid().ToString('N')
    try {
        foreach ($build in $Builds) {
            $name = [string]$build.Name
            $arguments = @($build.Arguments)
            if ([string]::IsNullOrWhiteSpace($name) -or
                $arguments.Count -lt 2 -or
                [string]$arguments[0] -cne 'build') {
                throw 'Parallel build entries require a name and build arguments.'
            }
            $sharedCompilationId =
                "sharpproof-parallel-$compilerServerScope-$name"
            $arguments += @(
                "/m:$lanesPerBuild",
                '/nodeReuse:false',
                '-p:UseSharedCompilation=true',
                "-p:SharedCompilationId=$sharedCompilationId")
            $effectiveArguments = @(
                Add-SharpProofStaticGraphArgument -Arguments $arguments)
            $startInfo = New-SharpProofParallelProcessStartInfo `
                -FileName 'dotnet' `
                -WorkingDirectory $RepositoryRoot `
                -Arguments $effectiveArguments `
                -Environment @{
                    UseSharedCompilation = 'true'
                    SharedCompilationId = $sharedCompilationId
                    'MSBUILDDISABLENODEREUSE' = '1'
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
                SharedCompilationId = $sharedCompilationId
                Process = $process
                StartedUtc = $process.StartTime.ToUniversalTime()
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
            $exitCode = $active.Process.ExitCode
            if (-not $Quiet -or $exitCode -ne 0) {
                Write-Host "--- Build $($active.Name) ---"
                if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                    Write-Host $stdout.TrimEnd()
                }
                if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                    Write-Host $stderr.TrimEnd()
                }
            }
            else {
                $elapsedSeconds = (
                    $active.Process.ExitTime.ToUniversalTime() -
                    $active.StartedUtc).TotalSeconds
                Write-Host (
                    "Build {0}: passed ({1:0.0}s)" -f
                    $active.Name, $elapsedSeconds)
            }
            if ($exitCode -ne 0) {
                $failures.Add(
                    "$($active.Name) exited ${exitCode}: " +
                    ($active.Arguments -join ' '))
            }
        }
        if ($failures.Count -ne 0) {
            throw "Parallel builds failed:`n$($failures -join "`n")"
        }
        foreach ($active in $running) {
            Stop-SharpProofCompilerServer `
                -SharedCompilationId $active.SharedCompilationId
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

function New-SharpProofParallelProcessStartInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [System.Collections.IDictionary]$Environment
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if ($null -ne $Environment) {
        foreach ($entry in $Environment.GetEnumerator()) {
            $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value
        }
    }
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    return $startInfo
}

function New-SharpProofCoverageContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [AllowEmptyString()]
        [string]$CoverageSettings = '',

        [AllowEmptyString()]
        [string]$CoverageResultsDirectory = '',

        [switch]$CreateResultsDirectory
    )

    $enabled =
        -not [string]::IsNullOrWhiteSpace($CoverageSettings) -or
        -not [string]::IsNullOrWhiteSpace($CoverageResultsDirectory)
    if ($enabled -and
        ([string]::IsNullOrWhiteSpace($CoverageSettings) -or
         [string]::IsNullOrWhiteSpace($CoverageResultsDirectory))) {
        throw (
            'CoverageSettings and CoverageResultsDirectory must be supplied ' +
            'together.')
    }
    $settings = if ($enabled) {
        (Resolve-Path -LiteralPath $CoverageSettings -ErrorAction Stop).Path
    }
    else {
        ''
    }
    $results = if ($enabled) {
        [IO.Path]::GetFullPath($CoverageResultsDirectory)
    }
    else {
        ''
    }
    if ($CreateResultsDirectory -and $enabled) {
        [IO.Directory]::CreateDirectory($results) | Out-Null
    }
    return [pscustomobject]@{
        Enabled = $enabled
        Settings = $settings
        Results = $results
        IsolatedOutputRoot = if ($enabled) {
            Join-Path $RepositoryRoot (
                '.sharpproof-coverage-output-' +
                [Guid]::NewGuid().ToString('N'))
        }
        else {
            ''
        }
    }
}

function Remove-SharpProofCoverageOutput {
    [CmdletBinding()]
    param(
        [AllowEmptyString()]
        [string]$Directory
    )

    if (-not [string]::IsNullOrWhiteSpace($Directory) -and
        [IO.Directory]::Exists($Directory)) {
        [IO.Directory]::Delete($Directory, $true)
    }
}

function Add-SharpProofCoverageArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [bool]$Enabled,

        [AllowEmptyString()]
        [string]$Settings = ''
    )

    if (-not $Enabled) {
        return $Arguments
    }
    return @($Arguments) + @(
        '--settings', $Settings,
        '--collect', 'Code Coverage;Format=Cobertura')
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
    'Get-SharpProofPackageTestParallelism',
    'Get-SharpProofSemanticTestParallelism',
    'Get-SharpProofTestProjectParallelism',
    'Get-SharpProofTestAssemblyPath',
    'Get-SharpProofDotnetWrapperPath',
    'Invoke-SharpProofCheckedCommand',
    'Invoke-SharpProofGitText',
    'Invoke-SharpProofTimedPhase',
    'Invoke-SharpProofParallelDotnetBuilds',
    'New-SharpProofParallelProcessStartInfo',
    'New-SharpProofCoverageContext',
    'Remove-SharpProofCoverageOutput',
    'Add-SharpProofCoverageArguments',
    'Invoke-SharpProofRequiredDotnet',
    'New-SharpProofIsolatedTestOutput',
    'Stop-SharpProofCompilerServer')
