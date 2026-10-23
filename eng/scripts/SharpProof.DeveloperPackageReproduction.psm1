Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
. (Join-Path $PSScriptRoot 'SharpProof.PackageBuildPlan.ps1')

function Get-SharpProofDeveloperQualificationBuildProjects {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$RepositoryRoot,

        [switch]$IncludeConsumerHarness
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $projects = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    foreach ($project in (Get-SharpProofPackageBuildProjects -RepositoryRoot $root -IncludeConsumerHarness:$IncludeConsumerHarness)) {
        [void]$projects.Add($project)
    }
    [xml]$solution = [IO.File]::ReadAllText((Join-Path $root 'SharpProof.slnx'))
    $solutionProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $solution.SelectNodes('/Solution//Project')) {
        [void]$solutionProjects.Add($project.GetAttribute('Path').Replace('\', '/'))
    }
    # The test root owns Tool, CodeFixes, and their actual project-reference
    # closure. Templates has no reference edge from that test assembly.
    # Catalog roots also retain the local pack-product fallback.
    foreach ($project in @(
            'src/SharpProof.DeveloperExperience.Tests/SharpProof.DeveloperExperience.Tests.csproj',
            'src/SharpProof.Templates/SharpProof.Templates.csproj')) {
        $absolute = Resolve-SharpProofContainedPath -Root $root -Path $project `
            -ParameterName 'Developer qualification build project'
        if (-not [IO.File]::Exists($absolute) -or -not $solutionProjects.Contains($project)) {
            throw "Developer qualification build project is missing from the source solution: '$project'."
        }
        [void]$projects.Add($project)
    }
    return , [Collections.Immutable.ImmutableArray]::CreateRange[string](
        [string[]]@($projects.ToImmutable() | Sort-Object -CaseSensitive))
}

function Get-SharpProofPreparedCodeFixesPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackageSource,
        [Parameter(Mandatory)][string]$PackageVersion,
        [Parameter(Mandatory)][string]$RepositoryCommit
    )
    if ([string]::IsNullOrWhiteSpace($PackageVersion) -or $RepositoryCommit -cnotmatch '\A[0-9a-f]{40}\z') {
        throw 'Prepared editor qualification requires the expected version and exact source commit.'
    }
    $source = Get-Item -LiteralPath $PackageSource -Force
    if (-not $source.PSIsContainer -or ($source.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The prepared editor package source must be an ordinary directory.'
    }
    $files = @(Get-ChildItem -LiteralPath $source.FullName -Force)
    if ($files.Count -ne 1 -or $files[0].PSIsContainer -or
        ($files[0].Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $files[0].Name -cne "SharpProof.CodeFixes.$PackageVersion.nupkg") {
        throw 'The prepared editor source must contain only the expected SharpProof.CodeFixes archive.'
    }
    $identity = Get-SharpProofPackageIdentity -Path $files[0].FullName -RequireRepository -RequireSingleIdentity
    if ($identity.Id -cne 'SharpProof.CodeFixes' -or $identity.Version -cne $PackageVersion -or
        $identity.RepositoryCommit -cne $RepositoryCommit) {
        throw 'The prepared editor archive differs from the expected version or source commit.'
    }
    return ([ordered]@{
            Path   = $files[0].FullName
            Sha256 = (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }).AsReadOnly()
}

function Get-SharpProofDeveloperPackageBuildPlan {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$OutputDirectory,
        [Parameter(Mandatory)][string]$RepositoryCommit,
        [Parameter(Mandatory)][string]$ConsumerImage
    )
    if ($Name -cnotin @('Tool', 'CodeFixes', 'Templates')) { throw 'Unsupported developer reproduction package.' }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { throw 'Developer reproduction requires a package output directory.' }
    if ($RepositoryCommit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'Developer reproduction requires the exact source commit.' }
    if ($ConsumerImage -cnotmatch '\Aghcr\.io/alexyorke/sharpproof@sha256:[a-f0-9]{64}\z') {
        throw 'Developer reproduction requires an explicit fixture consumer-image digest.'
    }
    $project = "src/SharpProof.$Name/SharpProof.$Name.csproj"
    $properties = [string[]]@('-p:ContinuousIntegrationBuild=true', '-p:GeneratePackageOnBuild=false',
        '-p:SharpProofDeveloperPackages=true', '-p:SharpProofPackTool=true',
        "-p:RepositoryCommit=$RepositoryCommit", "-p:SourceRevisionId=$RepositoryCommit", "-p:SharpProofConsumerImage=$ConsumerImage")
    $plan = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    # The product package graph does not build every optional developer root.
    # Prepare each root in its own exact-commit clone before packing its output.
    foreach ($phase in @('restore', 'build', 'pack')) {
        $arguments = switch ($phase) {
            'restore' { [string[]]@('restore', $project, '--locked-mode', '-p:Configuration=Release') + $properties }
            'build' { [string[]]@('build', $project, '-c', 'Release', '--no-restore') + $properties }
            'pack' { [string[]]@('pack', $project, '-c', 'Release', '--no-build', '--no-restore', '-o', $OutputDirectory) + $properties }
        }
        $plan.Add(([ordered]@{
                    Phase     = $phase
                    Arguments = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]$arguments)
                }).AsReadOnly())
    }
    return , ($plan.ToImmutable())
}

function Get-SharpProofReproductionDiagnosticText {
    param([AllowEmptyString()][string]$Text, [switch]$Tail)
    # Feed URLs may carry user information or query credentials. Keep them out
    # of both retained phase logs and the bounded diagnostic shown by CI.
    $redacted = [regex]::Replace($Text, '(?i)\b(?:https?|ssh)://\S+', '[redacted URL]')
    $redacted = [regex]::Replace($redacted,
        '(?im)^.*\b(?:authorization|password|passwd|access[_-]?token|api[_-]?key|client[_-]?secret|token|sig(?:nature)?)\b\s*(?:=|:).*$',
        '[redacted credential line]')
    if (-not $Tail) { return $redacted }
    $lines = @([regex]::Split($redacted, '\r?\n') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 20)
    if ($lines.Count -eq 0) { return 'No diagnostic output was captured.' }
    $summary = $lines -join "`n"
    if ($summary.Length -gt 4000) { $summary = $summary.Substring($summary.Length - 4000) }
    return $summary
}

function Get-SharpProofDeveloperPackageSnapshot {
    param(
        [Parameter(Mandatory)][string]$PackageSource,
        [Parameter(Mandatory)][string]$PackageVersion,
        [Parameter(Mandatory)][string]$RepositoryCommit
    )
    $ids = [Collections.Immutable.ImmutableArray]::Create[string]('SharpProof.Tool', 'SharpProof.CodeFixes', 'SharpProof.Templates')
    $files = @(Get-ChildItem -LiteralPath $PackageSource -Filter '*.nupkg' -File)
    if ($files.Count -ne $ids.Length) { throw 'Developer reproduction requires exactly the three optional packages.' }
    $payloads = [Collections.Immutable.ImmutableSortedDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
    $archives = [Collections.Immutable.ImmutableSortedDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
    foreach ($id in $ids) {
        $path = Join-Path $PackageSource "$id.$PackageVersion.nupkg"
        $identity = Get-SharpProofPackageIdentity -Path $path -RequireRepository -RequireSingleIdentity
        if ($identity.Id -cne $id -or $identity.Version -cne $PackageVersion -or $identity.RepositoryCommit -cne $RepositoryCommit) {
            throw 'Developer reproduction package identity, version, or source differs from the exact checkout.'
        }
        $archives.Add([IO.Path]::GetFileName($path), (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())
        $archive = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $names = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $archive.Entries) {
                $name = $entry.FullName
                if (-not $name -or $name.StartsWith('/') -or $name.Contains('\') -or $name.Contains(':') -or
                    @($name.Split('/') | Where-Object { $_ -in @('.', '..') }).Count -or -not $names.Add($name)) {
                    throw 'Developer reproduction package has an unsafe or duplicate ZIP member.'
                }
                if ([IO.Path]::GetFileName($name).Equals('.signature.p7s', [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Developer reproduction requires unsigned original packages.'
                }
                # SDK ZIP relationship/core-property identifiers and timestamps are
                # container metadata. All nuspec and product payload bytes count.
                if ($name.EndsWith('/') -or $name -ceq '_rels/.rels' -or $name -ceq '[Content_Types].xml' -or
                    $name.StartsWith('package/services/metadata/core-properties/', [StringComparison]::Ordinal)) { continue }
                $stream = $entry.Open()
                try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                finally { $stream.Dispose() }
                $payloads.Add("$id/$name", $digest)
            }
            $required = switch ($id) {
                'SharpProof.Tool' { 'tools/net10.0/any/SharpProof.Tool.dll' }
                'SharpProof.CodeFixes' { 'analyzers/dotnet/cs/SharpProof.CodeFixes.dll' }
                'SharpProof.Templates' { 'content/container/compose.yaml.template' }
            }
            if (-not $names.Contains($required)) { throw "Developer reproduction is missing the required $id payload." }
        }
        finally { $archive.Dispose() }
    }
    return , ([ordered]@{ Payloads = $payloads.ToImmutable(); Archives = $archives.ToImmutable() }).AsReadOnly()
}

function Compare-SharpProofDeveloperPackageSnapshots {
    param([Parameter(Mandatory)]$First, [Parameter(Mandatory)]$Second)
    $mismatches = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $payloadKeys = @([Linq.Enumerable]::Concat[string]($First.Payloads.Keys, $Second.Payloads.Keys) | Sort-Object -Unique)
    foreach ($key in $payloadKeys) {
        if (-not $First.Payloads.ContainsKey($key) -or -not $Second.Payloads.ContainsKey($key) -or
            $First.Payloads[$key] -cne $Second.Payloads[$key]) { $mismatches.Add($key) }
    }
    $comparisons = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $archivesEqual = $true
    foreach ($key in @([Linq.Enumerable]::Concat[string]($First.Archives.Keys, $Second.Archives.Keys) | Sort-Object -Unique)) {
        $firstHash = if ($First.Archives.ContainsKey($key)) { $First.Archives[$key] } else { $null }
        $secondHash = if ($Second.Archives.ContainsKey($key)) { $Second.Archives[$key] } else { $null }
        $equal = $null -ne $firstHash -and $null -ne $secondHash -and $firstHash -ceq $secondHash
        if (-not $equal) { $archivesEqual = $false }
        $comparisons.Add(([ordered]@{ file = $key; firstSha256 = $firstHash; secondSha256 = $secondHash; equal = $equal }).AsReadOnly())
    }
    return , ([ordered]@{
            PayloadsEqual = $payloadKeys.Count -gt 0 -and $mismatches.Count -eq 0
            PayloadCount = $payloadKeys.Count; PayloadMismatches = $mismatches.ToImmutable()
            PackageBytesEqual = $comparisons.Count -gt 0 -and $archivesEqual; ArchiveComparisons = $comparisons.ToImmutable()
        }).AsReadOnly()
}

Export-ModuleMember -Function Get-SharpProofDeveloperQualificationBuildProjects, Get-SharpProofPreparedCodeFixesPackage, Get-SharpProofDeveloperPackageBuildPlan, Get-SharpProofReproductionDiagnosticText, Get-SharpProofDeveloperPackageSnapshot, Compare-SharpProofDeveloperPackageSnapshots
