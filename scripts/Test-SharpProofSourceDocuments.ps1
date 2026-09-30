[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-SharpProofSourceDocument.ps1')

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$probeRoot = Join-Path $temporaryRoot ('sharpproof-source-documents-' + [Guid]::NewGuid().ToString('N'))
$sourceDirectory = Join-Path $probeRoot 'Project'
[IO.Directory]::CreateDirectory($sourceDirectory) | Out-Null
[IO.File]::WriteAllText((Join-Path $sourceDirectory 'Source.cs'), 'class Source {}')
[IO.File]::WriteAllText((Join-Path $sourceDirectory 'NotCompiled.cs'), 'class NotCompiled {}')
$checks = 0
try {
    foreach ($candidate in @(
        '/_/Project/Source.cs', '\_\Project\Source.cs',
        (Join-Path $sourceDirectory 'Source.cs'), 'Project/Source.cs')) {
        $resolved = Resolve-SharpProofSourceDocument -RepositoryRoot $probeRoot -DocumentPath $candidate
        if ($resolved -cne 'Project/Source.cs') {
            throw "Source document identity changed for '$candidate': '$resolved'."
        }
        $checks++
    }
    $rooted = Resolve-SharpProofSourceDocument -RepositoryRoot $probeRoot `
        -DocumentPath 'Source.cs' -SourceRoots @('/_/Project')
    if ($rooted -cne 'Project/Source.cs') { throw 'Mapped source roots did not resolve the same document.' }
    $checks++

    foreach ($candidate in @(
        '/_/', '/_/ ', '/_//Project/Source.cs', '/_/Project//Source.cs',
        '/_/Project/./Source.cs', '/_/Project/../Project/Source.cs',
        '/_/../Project/Source.cs', '/_/C:/Project/Source.cs',
        '/__/Project/Source.cs', '/_other/Project/Source.cs',
        '/foreign/Project/Source.cs', '/_/Project/Missing.cs')) {
        $rejected = $false
        try {
            Resolve-SharpProofSourceDocument -RepositoryRoot $probeRoot -DocumentPath $candidate | Out-Null
        }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Invalid source document was accepted: '$candidate'." }
        $checks++
    }

    $virtualGenerated = Resolve-SharpProofSourceDocument -RepositoryRoot $probeRoot `
        -DocumentPath '/_/Project/obj/Generated/Virtual.g.cs' -RequireFile $false
    if ($virtualGenerated -cne 'Project/obj/Generated/Virtual.g.cs') {
        throw 'Generated documents must be canonicalized before the caller excludes obj/bin.'
    }
    $checks++
    $escape = Join-Path $sourceDirectory 'Escape.cs'
    New-Item -ItemType SymbolicLink -Path $escape -Target '/etc/passwd' | Out-Null
    $rejectedEscape = $false
    try {
        Resolve-SharpProofSourceDocument -RepositoryRoot $probeRoot `
            -DocumentPath '/_/Project/Escape.cs' | Out-Null
    }
    catch { $rejectedEscape = $true }
    if (-not $rejectedEscape) { throw 'A source symlink escaped physical repository containment.' }
    $checks++

    # Mapping changes identity syntax only; membership remains the caller's
    # evaluated Compile/portable-PDB universe, never all existing source files.
    $compilePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$compilePaths.Add('Project/Source.cs')
    $outside = Resolve-SharpProofSourceDocument -RepositoryRoot $probeRoot `
        -DocumentPath '/_/Project/NotCompiled.cs'
    if ($compilePaths.Contains($outside)) { throw 'Mapped documents expanded the Compile universe.' }
    $checks++
    Write-Host "Source document regressions passed: $checks checks."
}
finally {
    $resolvedProbe = [IO.Path]::GetFullPath($probeRoot)
    if (-not $resolvedProbe.StartsWith($temporaryRoot.TrimEnd('/') + '/', [StringComparison]::Ordinal) -or
        -not [IO.Path]::GetFileName($resolvedProbe).StartsWith('sharpproof-source-documents-', [StringComparison]::Ordinal)) {
        throw 'Source document probe cleanup escaped its temporary root.'
    }
    Remove-Item -LiteralPath $resolvedProbe -Recurse -Force
}
