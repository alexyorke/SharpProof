[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)

function Get-Anchors([string]$Content) {
    $anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $counts = @{}
    $withoutCode = [regex]::Replace($Content, '(?ms)^\s*(```|~~~).*?^\s*\1[^\r\n]*$', '')
    foreach ($match in [regex]::Matches($withoutCode, '(?m)^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$')) {
        $slug = $match.Groups[1].Value.ToLowerInvariant()
        $slug = [regex]::Replace($slug, '<[^>]+>', '')
        $slug = [regex]::Replace($slug, '[^\p{L}\p{N}_\-\s]', '') -replace '\s', '-'
        if ($counts.ContainsKey($slug)) { $counts[$slug]++; $slug += '-' + $counts[$slug] }
        else { $counts[$slug] = 0 }
        [void]$anchors.Add($slug)
    }
    foreach ($match in [regex]::Matches($withoutCode, '(?i)\b(?:id|name)=["'']([^"'']+)["'']')) {
        [void]$anchors.Add($match.Groups[1].Value)
    }
    return ,$anchors
}

# Restrict discovery to tracked source; ignored artifacts can contain other worktrees.
$tracked = @(& git -C $RepositoryRoot ls-files --cached --others --exclude-standard -- '*.md' ':!:eng/common/**' | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw 'Documentation discovery requires a Git source checkout.' }
$files = @($tracked | ForEach-Object { Join-Path $RepositoryRoot $_ })
$generated = Join-Path $RepositoryRoot 'docs/generated/source-reference.md'
if ((Test-Path -LiteralPath $generated) -and $generated -notin $files) { $files += $generated }
$checked = 0
foreach ($file in $files) {
    $content = [IO.File]::ReadAllText($file)
    $content = [regex]::Replace($content, '(?ms)^\s*(```|~~~).*?^\s*\1[^\r\n]*$', '')
    $targets = @([regex]::Matches($content, '\[[^\]\r\n]*\]\(<?([^\s>]+)>?(?:\s+"[^"]*")?\)') | ForEach-Object { $_.Groups[1].Value })
    $targets += @([regex]::Matches($content, '(?m)^ {0,3}\[[^\]]+\]:\s*<?([^\s>]+)>?') | ForEach-Object { $_.Groups[1].Value })
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
