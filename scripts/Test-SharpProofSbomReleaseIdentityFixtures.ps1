[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'canonical',
        'stale-commit',
        'stale-timestamp',
        'equivalent-offset-timestamp',
        'equivalent-fractional-timestamp',
        'malformed-namespace',
        'wrong-name',
        'wrong-version',
        'creator-scalar',
        'creator-null',
        'creator-object',
        'creator-extra',
        'creation-extra',
        'creation-case')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Test-SharpProofPackageDependencies.ps1')
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')

$version = Get-SharpProofReleaseVersion -RepositoryRoot $repositoryRoot
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') {
    throw 'Fixture could not resolve a canonical commit.'
}
$identity = Get-SharpProofSbomReleaseIdentity `
    -RepositoryRoot $repositoryRoot `
    -Version $version `
    -RepositoryCommit $commit
$document = [pscustomobject][ordered]@{
    name = [string]$identity.Name
    documentNamespace = [string]$identity.DocumentNamespace
    creationInfo = [pscustomobject][ordered]@{
        created = [string]$identity.Created
        creators = [object[]]@($identity.Creators)
        comment = [string]$identity.Comment
    }
}

switch ($Mutation) {
    'stale-commit' {
        $document.documentNamespace =
            "https://github.com/alexyorke/SharpProof/sbom/$version/" +
            ('0' * 40)
    }
    'stale-timestamp' {
        $document.creationInfo.created = '2000-01-01T00:00:00Z'
    }
    'equivalent-offset-timestamp' {
        $instant = [DateTimeOffset]::Parse(
            [string]$identity.Created,
            [Globalization.CultureInfo]::InvariantCulture)
        $document.creationInfo.created = $instant.ToOffset(
            [TimeSpan]::FromHours(1)).ToString(
                'yyyy-MM-ddTHH:mm:sszzz',
                [Globalization.CultureInfo]::InvariantCulture)
    }
    'equivalent-fractional-timestamp' {
        $document.creationInfo.created =
            ([string]$identity.Created).Replace('Z', '.000Z')
    }
    'malformed-namespace' {
        $document.documentNamespace = "SharpProof/sbom/$version/$commit"
    }
    'wrong-name' { $document.name = 'SharpProof' }
    'wrong-version' {
        $document.name = 'SharpProof-9.9.9'
        $document.documentNamespace =
            "https://github.com/alexyorke/SharpProof/sbom/9.9.9/$commit"
    }
    'creator-scalar' {
        $document.creationInfo.creators = [string]$identity.Creators[0]
    }
    'creator-null' { $document.creationInfo.creators = $null }
    'creator-object' {
        $document.creationInfo.creators = [pscustomobject]@{
            tool = 'SharpProof release evidence'
        }
    }
    'creator-extra' {
        $document.creationInfo.creators = [object[]]@(
            [string]$identity.Creators[0],
            'Person: unreviewed'
        )
    }
    'creation-extra' {
        Add-Member `
            -InputObject $document.creationInfo `
            -NotePropertyName generatedBy `
            -NotePropertyValue 'fixture'
    }
    'creation-case' {
        $document.creationInfo.PSObject.Properties.Remove('comment')
        Add-Member `
            -InputObject $document.creationInfo `
            -NotePropertyName Comment `
            -NotePropertyValue ([string]$identity.Comment)
    }
}

$roundTrip = $document |
    ConvertTo-Json -Depth 5 |
    ConvertFrom-Json -DateKind String
Test-SharpProofSbomReleaseIdentity `
    -Sbom $roundTrip `
    -RepositoryRoot $repositoryRoot `
    -Version $version `
    -RepositoryCommit $commit

Write-Host "SBOM release identity fixture passed: $Mutation"
