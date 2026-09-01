function Get-SharpProofReleaseAuthorityClosure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $tracked = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $trackedOutput = & git -c "safe.directory=$root" -C $root ls-files -z
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enumerate tracked release-authority inputs.'
    }
    foreach ($trackedPath in (($trackedOutput -join '').Split(
                [char]0, [StringSplitOptions]::RemoveEmptyEntries))) {
        [void]$tracked.Add($trackedPath.Replace('\', '/'))
    }
    $roots = @(
        '.github/workflows/package-consumers.yml',
        'eng/container/entrypoint.sh',
        'scripts/Get-SharpProofReleaseAuthorityClosure.ps1',
        'scripts/SharpProof.PublicationPlanIdentity.psm1',
        'scripts/Test-SharpProofPublicationPlan.ps1',
        'scripts/Test-SharpProofPublicationPlanIdentityFixtures.ps1',
        'scripts/Invoke-SharpProofContainer.ps1',
        'scripts/Invoke-SharpProofReleaseContainer.ps1',
        'SharpProof.Verifier/SharpProof.Verifier.nuspec')
    $pending = [Collections.Generic.Queue[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($path in $roots) { $pending.Enqueue($path) }

    while ($pending.Count -ne 0) {
        $path = $pending.Dequeue().Replace('\', '/')
        if (-not $seen.Add($path)) { continue }
        $absolute = [IO.Path]::GetFullPath((Join-Path $root $path))
        if (-not $absolute.StartsWith(
                $root + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal) -or
            -not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
            throw "Release-authority path is missing or escapes the repository: '$path'."
        }

        $references = [Collections.Generic.List[string]]::new()
        if ($path.EndsWith('.ps1', [StringComparison]::Ordinal) -or
            $path.EndsWith('.psm1', [StringComparison]::Ordinal)) {
            $tokens = $null
            $errors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                $absolute, [ref]$tokens, [ref]$errors)
            if ($errors.Count -ne 0) {
                throw "Release-authority script cannot be parsed: '$path'."
            }
            foreach ($literal in $ast.FindAll({
                        param($node)
                        $node -is [Management.Automation.Language.StringConstantExpressionAst]
                    }, $true)) {
                $value = ([string]$literal.Value).Replace('\', '/')
                if ($value -match '(?i)^(?:scripts|eng|\.github|SharpProof(?:\.[A-Za-z0-9_.-]+)?)/.+\.(?:ps1|json|ya?ml|props|targets|nuspec)$' -or
                    $value -match '(?i)^scripts/.+\.cs$') {
                    $references.Add($value)
                }
                elseif ($value -match '(?i)^[A-Za-z0-9_.-]+\.psm?1$' -or
                    $value -ceq 'SharpProof.SymbolPackageValidator.cs') {
                    $references.Add((Split-Path $path -Parent).Replace('\', '/') + '/' + $value)
                }
            }
        }
        elseif ($path.EndsWith('.sh', [StringComparison]::Ordinal) -or
            $path.EndsWith('.yml', [StringComparison]::Ordinal) -or
            $path.EndsWith('.yaml', [StringComparison]::Ordinal)) {
            $text = [IO.File]::ReadAllText($absolute)
            foreach ($match in [regex]::Matches(
                    $text,
                    '(?m)(?:^|[\s''"])(?<path>(?:scripts|eng|\.github|SharpProof(?:\.[A-Za-z0-9_.-]+)?)/[A-Za-z0-9_./-]+\.(?:ps1|json|ya?ml|props|targets|nuspec))(?=$|[\s''";])')) {
                $references.Add($match.Groups['path'].Value)
            }
            foreach ($match in [regex]::Matches(
                    $text,
                    '(?m)^\s*(?:-\s*)?uses:\s*\./(?<path>\.github/actions/[A-Za-z0-9_./-]+)\s*(?:#.*)?$')) {
                $action = $match.Groups['path'].Value.TrimEnd('/') + '/action.yml'
                $references.Add($action)
            }
        }
        foreach ($reference in $references) {
            $canonical = $reference.Replace('\', '/')
            if ($canonical.StartsWith('./', [StringComparison]::Ordinal)) {
                $canonical = $canonical.Substring(2)
            }
            $canonical = ([IO.Path]::GetRelativePath(
                $root, [IO.Path]::GetFullPath((Join-Path $root $canonical)))).Replace('\', '/')
            $isSourceCandidate = $canonical -notmatch '(?:^|/)(?:bin|obj|artifacts)/' -and
                (Test-Path -LiteralPath (Join-Path $root $canonical) -PathType Leaf)
            if ($canonical -cne 'eng/acceptance/contract.json' -and
                ($tracked.Contains($canonical) -or $isSourceCandidate) -and
                -not $seen.Contains($canonical)) {
                $pending.Enqueue($canonical)
            }
        }
    }

    $result = [string[]]@($seen)
    [Array]::Sort($result, [StringComparer]::Ordinal)
    return $result
}
