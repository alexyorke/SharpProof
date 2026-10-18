Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SharpProofPackageIds = @(
    'SharpProof',
    'SharpProof.Attributes',
    'SharpProof.Verifier'
) | Sort-Object

$SharpProofPackagePushOrder = @(
    'SharpProof.Attributes',
    'SharpProof',
    'SharpProof.Verifier'
)

function Get-SharpProofNuspecMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries | Where-Object {
            $_.FullName.EndsWith(
                '.nuspec', [StringComparison]::OrdinalIgnoreCase)
        })
        if ($entries.Count -ne 1) {
            throw "Package '$Path' must contain exactly one nuspec."
        }
        $reader = [IO.StreamReader]::new($entries[0].Open())
        try {
            [xml]$document = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $namespaces = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaces.AddNamespace(
        'n', [string]$document.DocumentElement.NamespaceURI)
    $metadata = $document.SelectSingleNode(
        '/n:package/n:metadata', $namespaces)
    if ($null -eq $metadata) {
        throw "Package '$Path' has no nuspec metadata."
    }
    return $metadata
}

function Get-SharpProofPackageIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter()]
        [switch]$RequireRepository,

        [Parameter()]
        [switch]$RequireSingleIdentity
    )

    $metadata = Get-SharpProofNuspecMetadata -Path $Path
    $namespaces = [Xml.XmlNamespaceManager]::new(
        $metadata.OwnerDocument.NameTable)
    $namespaces.AddNamespace(
        'n', [string]$metadata.OwnerDocument.DocumentElement.NamespaceURI)
    $ids = @($metadata.SelectNodes('n:id', $namespaces))
    $versions = @($metadata.SelectNodes('n:version', $namespaces))
    if ($RequireSingleIdentity -and
        ($ids.Count -ne 1 -or $versions.Count -ne 1)) {
        throw "Package '$Path' has incomplete identity metadata."
    }
    $id = if ($ids.Count -eq 0) { $null } else { $ids[0] }
    $version = if ($versions.Count -eq 0) { $null } else { $versions[0] }
    $repository = $metadata.SelectSingleNode('n:repository', $namespaces)
    if ($null -eq $id -or $null -eq $version -or
        ($RequireRepository -and $null -eq $repository)) {
        throw "Package '$Path' has incomplete identity metadata."
    }

    $repositoryUrl = $null
    $repositoryCommit = $null
    if ($null -ne $repository) {
        $repositoryType = $repository.GetAttribute('type')
        $repositoryUrl = $repository.GetAttribute('url')
        $repositoryCommit = $repository.GetAttribute('commit')
        if ($RequireRepository -and
            ($repositoryType -ne 'git' -or
             $repositoryUrl -ne 'https://github.com/alexyorke/SharpProof' -or
             $repositoryCommit -notmatch '^[0-9a-fA-F]{40}$')) {
            throw "Package '$Path' has invalid repository metadata."
        }
    }

    return [pscustomobject][ordered]@{
        Id = $id.InnerText
        Version = $version.InnerText
        Path = $Path
        RepositoryUrl = $repositoryUrl
        RepositoryCommit = if ($null -eq $repositoryCommit) {
            $null
        }
        else {
            $repositoryCommit.ToLowerInvariant()
        }
    }
}

function Get-SharpProofPackageIdentitySet {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Files,

        [Parameter()]
        [switch]$RequireRepository
    )

    $identities = @(
        $Files | ForEach-Object {
            [pscustomobject][ordered]@{
                File = $_
                Identity = Get-SharpProofPackageIdentity `
                    -Path $_.FullName `
                    -RequireRepository:$RequireRepository
            }
        }
    )
    foreach ($extension in @('.nupkg', '.snupkg')) {
        $actualIds = @(
            $identities |
                Where-Object { $_.File.Extension -eq $extension } |
                ForEach-Object { $_.Identity.Id } |
                Sort-Object
        )
        if (($actualIds -join '|') -ne ($SharpProofPackageIds -join '|')) {
            throw (
                "SharpProof package identity set requires one $extension " +
                "for each expected ID; found '$($actualIds -join ', ')'.")
        }
    }

    $versions = @(
        $identities |
            ForEach-Object { $_.Identity.Version } |
            Sort-Object -Unique
    )
    if ($versions.Count -ne 1) {
        throw (
            'SharpProof package and symbol package versions must match; ' +
            "found '$($versions -join ', ')'.")
    }

    return [pscustomobject][ordered]@{
        Identities = $identities
        Version = [string]$versions[0]
        RepositoryCommits = @(
            $identities |
                ForEach-Object { $_.Identity.RepositoryCommit } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Sort-Object -Unique
        )
    }
}

Export-ModuleMember -Function @(
    'Get-SharpProofNuspecMetadata',
    'Get-SharpProofPackageIdentity',
    'Get-SharpProofPackageIdentitySet'
) -Variable @(
    'SharpProofPackageIds',
    'SharpProofPackagePushOrder'
)
