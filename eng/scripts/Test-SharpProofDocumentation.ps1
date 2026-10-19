[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)

function Get-Anchors([string]$Content) {
    $anchors = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    $counts = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, int]([StringComparer]::OrdinalIgnoreCase)
    $withoutCode = [regex]::Replace($Content, '(?ms)^\s*(```|~~~).*?^\s*\1[^\r\n]*$', '')
    foreach ($match in [regex]::Matches($withoutCode, '(?m)^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$')) {
        $heading = $match.Groups[1].Value.ToLowerInvariant()
        $withoutMarkup = [regex]::Replace($heading, '<[^>]+>', '')
        $slug = [regex]::Replace($withoutMarkup, '[^\p{L}\p{N}_\-\s]', '') -replace '\s', '-'
        $anchor = if ($counts.ContainsKey($slug)) { $counts[$slug]++; $slug + '-' + $counts[$slug] }
        else { $counts[$slug] = 0; $slug }
        [void]$anchors.Add($anchor)
    }
    foreach ($match in [regex]::Matches($withoutCode, '(?i)\b(?:id|name)=["'']([^"'']+)["'']')) {
        [void]$anchors.Add($match.Groups[1].Value)
    }
    return , $anchors.ToImmutable()
}

# Restrict discovery to tracked source; ignored artifacts can contain other worktrees.
$trackedBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
& git -C $RepositoryRoot ls-files --cached --others --exclude-standard -- '*.md' ':!:eng/common/**' |
    Sort-Object -Unique |
    ForEach-Object { $trackedBuilder.Add([string]$_) }
if ($LASTEXITCODE -ne 0) { throw 'Documentation discovery requires a Git source checkout.' }
$tracked = $trackedBuilder.ToImmutable()
$fileBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
foreach ($relative in $tracked) { $fileBuilder.Add((Join-Path $RepositoryRoot $relative)) }
$generated = Join-Path $RepositoryRoot 'docs/generated/source-reference.md'
if ((Test-Path -LiteralPath $generated) -and $generated -notin $fileBuilder) { $fileBuilder.Add($generated) }
$files = $fileBuilder.ToImmutable()
$checked = 0
foreach ($file in $files) {
    $content = [IO.File]::ReadAllText($file)
    $content = [regex]::Replace($content, '(?ms)^\s*(```|~~~).*?^\s*\1[^\r\n]*$', '')
    $targetBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($match in [regex]::Matches($content, '\[[^\]\r\n]*\]\(<?([^\s>]+)>?(?:\s+"[^"]*")?\)')) { $targetBuilder.Add($match.Groups[1].Value) }
    foreach ($match in [regex]::Matches($content, '(?m)^ {0,3}\[[^\]]+\]:\s*<?([^\s>]+)>?')) { $targetBuilder.Add($match.Groups[1].Value) }
    $targets = $targetBuilder.ToImmutable()
    foreach ($target in $targets) {
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $target.StartsWith('//')) { continue }
        $parts = $target.Split('#', 2)
        $pathPart = [Uri]::UnescapeDataString($parts[0].Split('?')[0])
        $resolved = if (-not $pathPart) { $file }
        elseif ($pathPart.StartsWith('/')) { Join-Path $RepositoryRoot $pathPart.TrimStart('/') }
        else { Join-Path (Split-Path $file -Parent) $pathPart }
        if (-not (Test-Path -LiteralPath $resolved)) { throw "Broken Markdown link in ${file}: $target" }
        if ($parts.Count -eq 2 -and $parts[1] -and [IO.Path]::GetExtension($resolved) -eq '.md') {
            $anchors = Get-Anchors ([IO.File]::ReadAllText($resolved))
            if (-not $anchors.Contains([Uri]::UnescapeDataString($parts[1]))) { throw "Broken Markdown anchor in ${file}: $target" }
        }
        $checked++
    }
}
Write-Host "Documentation local links passed: $checked links."
