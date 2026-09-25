[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('LoopSnapshot', 'PreprocessorSymbols')]
    [string]$Scenario
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Scenario -eq 'PreprocessorSymbols') {
    . (Join-Path $PSScriptRoot 'CSharpSourceMetrics.ps1')
    $options = New-SharpProofCSharpParseOptions `
        -LanguageVersion 'latest' `
        -PreprocessorSymbols @('DEBUG', 'debug')

    $source = @'
#if DEBUG
public class UpperSymbolBranch { }
#endif
#if debug
public class LowerSymbolBranch { }
#endif
'@
    $metrics = Measure-CSharpSourceText `
        -Source $source `
        -ParseOptions $options
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        $source,
        $options)
    $rootNode = $tree.GetRoot()
    $effectiveSymbols = $tree.Options.PreprocessorSymbolNames
    $parseErrors = @($tree.GetDiagnostics() | Where-Object {
        $_.Severity.ToString() -eq 'Error'
    })
    if ($effectiveSymbols.Length -ne 2 -or
        -not $effectiveSymbols.Contains('DEBUG') -or
        -not $effectiveSymbols.Contains('debug') -or
        $rootNode.Members.Count -ne 2 -or
        $metrics.members -ne 2 -or
        $parseErrors.Count -ne 0) {
        throw (
            'Expected distinct DEBUG/debug branches to parse cleanly; found ' +
            "$($metrics.members) measured members, $($rootNode.Members.Count) root members, " +
            "$($effectiveSymbols.Length) symbols, and $($parseErrors.Count) errors.")
    }
    Write-Output 'DEBUG and debug both remain active.'
    exit 0
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-LoopCase-' + [Guid]::NewGuid().ToString('N'))
$repositoryRoot = Join-Path $temporaryRoot 'repository'
$scriptsDirectory = Join-Path $repositoryRoot 'scripts'
$stubDirectory = Join-Path $temporaryRoot 'stub'
$capturePath = Join-Path $temporaryRoot 'captured-manifest'
$savedPath = $env:PATH
$savedRepository = $env:B56_LOOP_REPO_ROOT
$savedCapture = $env:B56_LOOP_CAPTURE_PATH

try {
    if (-not $IsLinux) {
        throw 'The loop snapshot fixture requires a case-sensitive Linux filesystem.'
    }

    [IO.Directory]::CreateDirectory($scriptsDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($stubDirectory) | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot 'Invoke-SharpProofLoop.ps1') `
        -Destination (Join-Path $scriptsDirectory 'Invoke-SharpProofLoop.ps1')
    [IO.File]::WriteAllText(
        (Join-Path $repositoryRoot 'tracked.txt'),
        'tracked baseline' + "`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $repositoryRoot '.gitignore'),
        '/artifacts/' + "`n",
        [Text.UTF8Encoding]::new($false))

    & git -C $repositoryRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize loop fixture Git repository.' }
    & git -C $repositoryRoot config user.email test@example.invalid
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure loop fixture Git identity.' }
    & git -C $repositoryRoot config user.name 'SharpProof Test'
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure loop fixture Git identity.' }
    & git -C $repositoryRoot add -- `
        scripts/Invoke-SharpProofLoop.ps1 `
        tracked.txt `
        .gitignore
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage loop fixture baseline.' }
    & git -C $repositoryRoot commit --quiet -m baseline
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit loop fixture baseline.' }

    [IO.File]::WriteAllText(
        (Join-Path $repositoryRoot 'Foo.cs'),
        'public class UpperPath { }' + "`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $repositoryRoot 'foo.cs'),
        'public class LowerPath { }' + "`n",
        [Text.UTF8Encoding]::new($false))

    $dockerStub = @'
#!/bin/sh
set -eu
repo_root="${B56_LOOP_REPO_ROOT:?}"
capture_path="${B56_LOOP_CAPTURE_PATH:?}"
snapshot_name=""
for argument in "$@"; do
    case "$argument" in
        SHARPPROOF_LOOP_SNAPSHOT_ROOT=*) snapshot_name="${argument##*/}" ;;
    esac
done
if [ -z "$snapshot_name" ]; then
    echo "loop snapshot environment argument missing" >&2
    exit 30
fi
snapshot="$repo_root/artifacts/$snapshot_name"
manifest="$snapshot/source-files"
if [ ! -f "$manifest" ] ||
   [ ! -f "$snapshot/files/Foo.cs" ] ||
   [ ! -f "$snapshot/files/foo.cs" ]; then
    echo "case-distinct files were not both copied into the snapshot" >&2
    exit 31
fi
cp "$manifest" "$capture_path"
'@
    $dockerPath = Join-Path $stubDirectory 'docker'
    [IO.File]::WriteAllText(
        $dockerPath,
        $dockerStub.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
    & chmod +x $dockerPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not make fixture docker stub executable.' }

    $env:PATH = $stubDirectory + [IO.Path]::PathSeparator + $savedPath
    $env:B56_LOOP_REPO_ROOT = $repositoryRoot
    $env:B56_LOOP_CAPTURE_PATH = $capturePath
    $loopScript = Join-Path $scriptsDirectory 'Invoke-SharpProofLoop.ps1'
    $pwshPath = (
        Get-Command pwsh -CommandType Application |
            Select-Object -First 1).Source
    & $pwshPath -NoLogo -NoProfile -File $loopScript fixture
    if ($LASTEXITCODE -ne 0) {
        throw "Invoke-SharpProofLoop failed with exit code $LASTEXITCODE."
    }

    $paths = [Text.Encoding]::UTF8.GetString(
        [IO.File]::ReadAllBytes($capturePath)).Split(
            [char[]]@([char]0),
            [StringSplitOptions]::RemoveEmptyEntries)
    if ($paths.Count -ne 2 -or
        -not ($paths -ccontains 'Foo.cs') -or
        -not ($paths -ccontains 'foo.cs')) {
        throw "Expected both case-distinct paths in snapshot manifest, got: $($paths -join ', ')."
    }
    Write-Output 'Foo.cs and foo.cs both survive snapshot selection and manifest capture.'
}
finally {
    $env:PATH = $savedPath
    if ($null -eq $savedRepository) {
        Remove-Item Env:B56_LOOP_REPO_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:B56_LOOP_REPO_ROOT = $savedRepository
    }
    if ($null -eq $savedCapture) {
        Remove-Item Env:B56_LOOP_CAPTURE_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:B56_LOOP_CAPTURE_PATH = $savedCapture
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
