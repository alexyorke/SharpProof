[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$PackageVersion,
    [Parameter(Mandatory)][string]$ConsumerImage,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$RepositoryCommit
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
Assert-SharpProofContainer 'Template regression checks require the canonical container.'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceRoot = Join-Path $repositoryRoot 'src/SharpProof.Templates/content'
$sdkVersion = [string](Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$testRoot = Join-Path $repositoryRoot ('artifacts/obj/template-regressions-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$checks = 0
function Assert-Rejected([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action }
    catch {
        if (-not $_.Exception.Message.Contains($Message)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "The negative control was accepted: $Message" }
    $script:checks++
}
function Read-ArchiveText([IO.Compression.ZipArchiveEntry]$Entry) {
    $reader = [IO.StreamReader]::new($Entry.Open())
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}
function Assert-TemplatePackage([string]$Path) {
    $identity = Get-SharpProofPackageIdentity -Path $Path -RequireRepository -RequireSingleIdentity
    if ($identity.Id -cne 'SharpProof.Templates' -or $identity.Version -cne $PackageVersion -or $identity.RepositoryCommit -cne $RepositoryCommit) {
        throw 'Template package identity differs from qualification.'
    }
    $metadata = Get-SharpProofNuspecMetadata -Path $Path
    $types = @($metadata.SelectNodes("*[local-name()='packageTypes']/*[local-name()='packageType']"))
    $readme = $metadata.SelectSingleNode("*[local-name()='readme']")
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    if ($types.Count -ne 1 -or $types[0].GetAttribute('name') -cne 'Template' -or
        $null -eq $readme -or $readme.InnerText -cne 'README.md' -or
        $null -eq $license -or $license.GetAttribute('type') -cne 'expression' -or $license.InnerText -cne 'MIT') {
        throw 'Template package metadata is incomplete.'
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            if (-not $entries.TryAdd($entry.FullName, $entry)) { throw 'Template package contains duplicate paths.' }
        }
        $expectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force) {
            $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
            $packageEntry = 'content/' + $relative
            [void]$expectedPaths.Add($packageEntry)
            if (-not $entries.ContainsKey($packageEntry)) { throw "Template package is missing content: $packageEntry" }
            $expected = [IO.File]::ReadAllText($file.FullName).Replace('__SHARPPROOF_VERSION__', $PackageVersion).
            Replace('__SHARPPROOF_SDK__', $sdkVersion).Replace('__SHARPPROOF_CONSUMER_IMAGE__', $ConsumerImage).
            Replace('__SHARPPROOF_SOURCE_REVISION__', $RepositoryCommit)
            if ((Read-ArchiveText $entries[$packageEntry]) -cne $expected -or $expected -cmatch '__SHARPPROOF_[A-Z_]+__') {
                throw "Template package content differs from qualified inputs: $packageEntry"
            }
        }
        if (-not $entries.ContainsKey('README.md')) { throw 'Template package is missing its declared readme.' }
        foreach ($entry in $archive.Entries) {
            if ($expectedPaths.Contains($entry.FullName) -or $entry.FullName -ceq 'README.md' -or
                $entry.FullName -ceq 'SharpProof.Templates.nuspec' -or $entry.FullName -ceq '[Content_Types].xml' -or
                $entry.FullName -ceq '_rels/.rels' -or $entry.FullName.StartsWith('package/services/metadata/core-properties/', [StringComparison]::Ordinal)) { continue }
            throw "Template package contains unexpected payload: $($entry.FullName)"
        }
    }
    finally { $archive.Dispose() }
}
function Test-MutatedPackage([string]$Name, [string]$EntryName, [scriptblock]$Transform, [string]$ExpectedMessage) {
    $mutant = Join-Path $testRoot "$Name.nupkg"
    Copy-Item -LiteralPath $PackagePath -Destination $mutant
    $archive = [IO.Compression.ZipFile]::Open($mutant, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry($EntryName)
        $original = Read-ArchiveText $entry
        $replacement = & $Transform $original
        if ($replacement -ceq $original) { throw 'The package negative control did not change its intended input.' }
        $entry.Delete()
        $entry = $archive.CreateEntry($EntryName)
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write([string]$replacement) }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
    Assert-Rejected { Assert-TemplatePackage $mutant } $ExpectedMessage
}
try {
    Assert-TemplatePackage $PackagePath
    $checks++
    $prepare = Join-Path $PSScriptRoot 'Prepare-SharpProofTemplates.ps1'
    $validNuspec = Join-Path $testRoot 'template.nuspec'
    $content = Join-Path $testRoot 'content'
    $outside = Join-Path $repositoryRoot ('artifacts/template-output-' + [Guid]::NewGuid().ToString('N'))
    foreach ($destination in @($outside, (Join-Path $repositoryRoot 'artifacts/obj'), ((Join-Path $repositoryRoot 'artifacts/obj') + '/'), ($outside.Replace('template-output-', 'obj-template-output-')), (Join-Path $testRoot '../../template-escape'))) {
        Assert-Rejected { & $prepare -Destination $destination -PackageVersion $PackageVersion -ConsumerImage $ConsumerImage -NuspecPath $validNuspec } 'beneath artifacts/obj'
    }
    Assert-Rejected { & $prepare -Destination $content -PackageVersion $PackageVersion -ConsumerImage $ConsumerImage -NuspecPath (Join-Path $outside 'template.nuspec') } 'beneath artifacts/obj'
    Assert-Rejected { & $prepare -Destination $content -PackageVersion $PackageVersion -ConsumerImage $ConsumerImage -NuspecPath (Join-Path $content 'README.md') } 'outside the prepared template content'
    Assert-Rejected { & $prepare -Destination ($content + '/') -PackageVersion $PackageVersion -ConsumerImage $ConsumerImage -NuspecPath (Join-Path $content 'README.md') } 'outside the prepared template content'
    Assert-Rejected { & $prepare -Destination $content -PackageVersion $PackageVersion -ConsumerImage 'ghcr.io/alexyorke/sharpproof:latest' -NuspecPath $validNuspec } 'immutable GHCR digest'
    if (Test-Path -LiteralPath $content) { throw 'Rejected preparation wrote template content.' }
    if (Test-Path -LiteralPath $outside) { throw 'Rejected preparation wrote outside artifacts/obj.' }
    $linkTarget = Join-Path $testRoot 'link-target'
    [void][IO.Directory]::CreateDirectory($linkTarget)
    $link = Join-Path $testRoot 'linked-content'
    [void](New-Item -ItemType SymbolicLink -Path $link -Target $linkTarget)
    Assert-Rejected { & $prepare -Destination (Join-Path $link 'content') -PackageVersion $PackageVersion -ConsumerImage $ConsumerImage -NuspecPath $validNuspec } 'symbolic links'
    Assert-Rejected { & $prepare -Destination $content -PackageVersion $PackageVersion -ConsumerImage $ConsumerImage -NuspecPath (Join-Path $link 'template.nuspec') } 'symbolic links'
    & $prepare -Destination $content -PackageVersion $PackageVersion -ConsumerImage $ConsumerImage -NuspecPath $validNuspec
    [xml]$prepared = Get-Content -LiteralPath $validNuspec -Raw
    if ($prepared.package.metadata.version -cne $PackageVersion -or $prepared.package.metadata.repository.commit -cne $RepositoryCommit) {
        throw 'Prepared nuspec lost its qualified version or source commit.'
    }
    $checks++
    $nuspecEntry = 'SharpProof.Templates.nuspec'
    Test-MutatedPackage 'wrong-id' $nuspecEntry { param($value) $value.Replace('<id>SharpProof.Templates</id>', '<id>SharpProof.Unqualified</id>') } 'identity differs'
    Test-MutatedPackage 'wrong-version' $nuspecEntry { param($value) $value.Replace("<version>$PackageVersion</version>", '<version>0.0.0-negative</version>') } 'identity differs'
    Test-MutatedPackage 'wrong-commit' $nuspecEntry { param($value) $value.Replace("commit=`"$RepositoryCommit`"", ('commit="' + ('0' * 40) + '"')) } 'identity differs'
    Test-MutatedPackage 'wrong-type' $nuspecEntry { param($value) $value.Replace('name="Template"', 'name="Dependency"') } 'metadata is incomplete'
    Test-MutatedPackage 'wrong-content-version' 'content/portable/SharpProofStarter.csproj.template' { param($value) $value.Replace($PackageVersion, '0.0.0-negative') } 'content differs'
    Test-MutatedPackage 'mutable-image' 'content/container/compose.yaml.template' { param($value) $value.Replace($ConsumerImage, 'ghcr.io/alexyorke/sharpproof:latest') } 'content differs'
    Write-Host "Template checks passed: $checks (qualified payload, preparation limits, and rejecting mutated packages)."
}
finally {
    $artifactPrefix = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/obj')) + [IO.Path]::DirectorySeparatorChar
    if (-not $testRoot.StartsWith($artifactPrefix, [StringComparison]::Ordinal)) { throw 'Refusing to remove template regression output outside artifacts/obj.' }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
