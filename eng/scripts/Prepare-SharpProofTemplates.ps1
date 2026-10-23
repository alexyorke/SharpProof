[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$ConsumerImage,
    [Parameter(Mandatory)][string]$NuspecPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ReleaseConfiguration.psm1') -Force
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceRoot = Join-Path $repositoryRoot 'src/SharpProof.Templates/content'
$destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedNuspec = [IO.Path]::GetFullPath($NuspecPath)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/obj')) + [IO.Path]::DirectorySeparatorChar
function Assert-ArtifactPath([string]$Path) {
    if ($Path.TrimEnd([IO.Path]::DirectorySeparatorChar) -ceq $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        -not $Path.StartsWith($artifactRoot, [StringComparison]::Ordinal)) {
        throw 'Template preparation outputs must stay beneath artifacts/obj.'
    }
    for ($ancestor = $Path; $ancestor.Length -gt $repositoryRoot.Length; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Template preparation outputs must not traverse symbolic links.'
        }
    }
}
Assert-ArtifactPath $destinationRoot
Assert-ArtifactPath $resolvedNuspec
if ($resolvedNuspec -ceq $destinationRoot -or $resolvedNuspec.StartsWith($destinationRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'The generated nuspec must stay outside the prepared template content.'
}
if ($ConsumerImage -cnotmatch '^ghcr\.io/alexyorke/sharpproof(?:-preview)?@sha256:[a-f0-9]{64}$') {
    throw 'SharpProofConsumerImage must identify the qualified consumer image by immutable GHCR digest.'
}
$sdk = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$nuspec = [Xml.XmlDocument]::new()
$nuspec.LoadXml('<package><metadata><id>SharpProof.Templates</id><version/><authors>Alex Yorke</authors><description>SharpProof portable and canonical-container starter projects.</description><license type="expression">MIT</license><readme>README.md</readme><packageTypes><packageType name="Template"/></packageTypes><repository type="git" url="https://github.com/alexyorke/SharpProof" commit=""/></metadata><files/></package>')
$nuspec.package.metadata.version = $PackageVersion
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[a-f0-9]{40}$') { throw 'Template packaging requires a Git source identity.' }
$nuspec.package.metadata.repository.SetAttribute('commit', $commit)
$nuspec.package.metadata.repository.SetAttribute('url', (Get-SharpProofExpectedRepositoryUrl))
function Add-PackageFile([string]$Source, [string]$Target) {
    $entry = $nuspec.CreateElement('file')
    $entry.SetAttribute('src', $Source)
    $entry.SetAttribute('target', $Target)
    [void]$nuspec.SelectSingleNode('/package/files').AppendChild($entry)
}
Add-PackageFile (Join-Path $repositoryRoot 'README.md') ''
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
    $target = Join-Path $destinationRoot $relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    $text = [IO.File]::ReadAllText($file.FullName).Replace('__SHARPPROOF_VERSION__', $PackageVersion).
    Replace('__SHARPPROOF_SDK__', [string]$sdk).Replace('__SHARPPROOF_CONSUMER_IMAGE__', $ConsumerImage).
    Replace('__SHARPPROOF_SOURCE_REVISION__', $commit)
    [IO.File]::WriteAllText($target, $text, [Text.UTF8Encoding]::new($false))
    $relativeDirectory = [IO.Path]::GetDirectoryName($relative).Replace('\', '/')
    Add-PackageFile $target ('content/' + $relativeDirectory)
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedNuspec))
$nuspec.Save($resolvedNuspec)
