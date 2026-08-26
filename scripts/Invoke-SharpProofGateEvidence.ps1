[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('corpus', 'performance')]
    [string]$Gate,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
. (Join-Path $PSScriptRoot 'Assert-SharpProofStandaloneGateResult.ps1')
$parallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$resolvedOutput = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $OutputPath -ParameterName 'OutputPath'
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory |
    Out-Null
$rawOutput = [IO.Path]::ChangeExtension(
    $resolvedOutput,
    '.gate.json')
$standardError = [IO.Path]::ChangeExtension(
    $resolvedOutput,
    '.stderr.txt')
foreach ($stalePath in @($resolvedOutput, $rawOutput, $standardError)) {
    if (Test-Path -LiteralPath $stalePath -PathType Leaf) {
        Remove-Item -LiteralPath $stalePath -Force
    }
}
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to bind gate evidence to the exact source commit.'
}
$workingTreeStatus = & git -C $repositoryRoot status --porcelain=v1 `
    --untracked-files=all
if ($LASTEXITCODE -ne 0 -or @($workingTreeStatus).Count -ne 0) {
    throw 'Standalone gate evidence requires clean exact-commit source.'
}
$wrapper = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$project = Join-Path $repositoryRoot 'SharpProof.Gates\SharpProof.Gates.csproj'
$buildArguments = @(
    'build', $project, '-c', 'Release', '--no-restore',
    '-t:Rebuild', "/m:$parallelism",
    "-p:SharpProofSourceCommit=$sourceCommit")
& $wrapper -TimeoutSeconds $TimeoutSeconds @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "The standalone gate build failed with code $LASTEXITCODE."
}
$executable = Join-Path $repositoryRoot `
    'SharpProof.Gates\bin\Release\net9.0\SharpProof.Gates.dll'
$pdb = [IO.Path]::ChangeExtension($executable, '.pdb')
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $pdb -PathType Leaf)) {
    throw 'The freshly built standalone gate identity is incomplete.'
}
$executableSha256 = (Get-FileHash -LiteralPath $executable `
    -Algorithm SHA256).Hash.ToLowerInvariant()
$pdbSha256 = (Get-FileHash -LiteralPath $pdb `
    -Algorithm SHA256).Hash.ToLowerInvariant()
$contractSha256 = (Get-FileHash -LiteralPath (
        Join-Path $repositoryRoot 'eng\acceptance\contract.json') `
    -Algorithm SHA256).Hash.ToLowerInvariant()
$stream = [IO.File]::OpenRead($executable)
try {
    $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader(
            $peReader)
        $mvid = $metadata.GetGuid(
            $metadata.GetModuleDefinition().Mvid).ToString('D')
    }
    finally {
        $peReader.Dispose()
    }
}
finally {
    $stream.Dispose()
}
$dotnetArguments = @(
    $executable,
    $Gate
)
$quotedArguments = @(
    $dotnetArguments |
        ForEach-Object {
            "'" + ([string]$_).Replace("'", "''") + "'"
        }
) -join ','
$escapedWrapper = $wrapper.Replace("'", "''")
$command = (
    "& '$escapedWrapper' -TimeoutSeconds " +
    [string]$TimeoutSeconds +
    " @($quotedArguments); exit " + '$LASTEXITCODE')
$encodedCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($command))
$exitCode = -1
$failure = $null
$gateResult = $null
try {
    $process = Start-Process `
        -FilePath 'pwsh' `
        -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-EncodedCommand',
            $encodedCommand
        ) `
        -WorkingDirectory $repositoryRoot `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $rawOutput `
        -RedirectStandardError $standardError
    $exitCode = $process.ExitCode
    if (-not (Test-Path -LiteralPath $rawOutput -PathType Leaf)) {
        $failure = 'The gate did not produce a JSON result.'
    }
    else {
        try {
            $gateResult = Assert-SharpProofStandaloneGateResult `
                -Path $rawOutput `
                -ExpectedGate $Gate `
                -ExpectedCommit $sourceCommit `
                -ExpectedExecutableSha256 $executableSha256 `
                -ExpectedMvid $mvid `
                -ExpectedPdbSha256 $pdbSha256 `
                -ExpectedContractSha256 $contractSha256
        }
        catch {
            $failure = 'The gate result failed validation: ' +
                $_.Exception.Message
        }
    }
    if ($exitCode -ne 0 -and $null -eq $failure) {
        $failure = "The gate exited with code $exitCode."
    }
}
catch {
    $failure = 'The gate could not be started or observed: ' +
        $_.Exception.Message
}

$passed = $exitCode -eq 0 -and
    $null -ne $gateResult -and
    $null -eq $failure
$envelope = [pscustomobject][ordered]@{
    schemaVersion = 2
    gate = $Gate
    passed = $passed
    commit = $sourceCommit
    acceptanceContractSha256 = $contractSha256
    executable = [pscustomobject][ordered]@{
        sha256 = $executableSha256
        mvid = $mvid
        pdbSha256 = $pdbSha256
    }
    exitCode = $exitCode
    failure = $failure
    result = if ($null -eq $gateResult) { $null } else { $gateResult.Result }
    rawResultSha256 = if (Test-Path -LiteralPath $rawOutput -PathType Leaf) {
        (Get-FileHash -LiteralPath $rawOutput `
            -Algorithm SHA256).Hash.ToLowerInvariant()
    } else { $null }
    rawOutput = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $rawOutput).Replace('\', '/')
    standardError = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $standardError).Replace('\', '/')
}
$json = ($envelope | ConvertTo-Json -Depth 20) -replace "`r`n", "`n"
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + "`n",
    [Text.UTF8Encoding]::new($false))
$json

if (-not $passed) {
    throw "SharpProof $Gate gate failed. Evidence: $resolvedOutput"
}
