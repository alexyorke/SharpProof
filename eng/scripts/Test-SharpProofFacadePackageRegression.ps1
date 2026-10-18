[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
. (Join-Path $PSScriptRoot 'Test-SharpProofSymbolPackages.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-facade-regression-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$commit = 'a' * 40
$validDependency = '<dependencies><group targetFramework=".NETStandard2.0"><dependency id="SharpProof.Verifier" version="[1.2.3]" /></group></dependencies>'

function Write-FixturePackage(
    [string]$Id = 'SharpProof', [string]$Version = '1.2.3', [string]$Extension = 'nupkg',
    [string]$RepositoryCommit = $commit, [string]$Dependency = $validDependency,
    [string[]]$ExtraEntries = @(), [string]$FileName = '', [switch]$DuplicateEntry, [switch]$Dtd) {
    if (-not $FileName) { $FileName = "$Id.1.2.3.$Extension" }
    $path = Join-Path $fixture $FileName
    if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry("$Id.nuspec")
        $writer = [IO.StreamWriter]::new($entry.Open())
        try {
            if ($Dtd) { $writer.Write('<!DOCTYPE package [<!ENTITY test "forbidden">]>') }
            $writer.Write("<package xmlns=`"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd`">`n<metadata>`n<id>$Id</id>`n<version>$Version</version>`n<repository type=`"git`" url=`"https://github.com/alexyorke/SharpProof`" commit=`"$RepositoryCommit`" />`n$Dependency`n</metadata>`n</package>")
        }
        finally { $writer.Dispose() }
        foreach ($name in $ExtraEntries) { [void]$archive.CreateEntry($name) }
        if ($DuplicateEntry) { [void]$archive.CreateEntry('README.md'); [void]$archive.CreateEntry('README.md') }
    }
    finally { $archive.Dispose() }
    return $path
}

function Assert-Rejected([scriptblock]$Action, [string]$Expected) {
    $rejected = $false
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Facade regression accepted invalid input ($Expected)." }
}

function Assert-FixtureFacade([string]$Path) {
    Test-SharpProofFacadePackage -PackagePath $Path -PackageVersion '1.2.3' -RepositoryCommit $commit
}

try {
    $facade = Write-FixturePackage
    Assert-FixtureFacade $facade
    foreach ($id in @('SharpProof.Attributes', 'SharpProof.Verifier')) {
        $null = Write-FixturePackage -Id $id -Dependency ''
        $null = Write-FixturePackage -Id $id -Extension snupkg -Dependency ''
    }
    $null = Get-SharpProofPackageIdentitySet -Files @(Get-ChildItem -LiteralPath $fixture -File) -RequireRepository
    $unexpectedSymbols = Write-FixturePackage -Extension snupkg
    Assert-Rejected { Get-SharpProofPackageIdentitySet -Files @(Get-ChildItem -LiteralPath $fixture -File) | Out-Null } 'identity set requires'
    Remove-Item -LiteralPath $unexpectedSymbols

    foreach ($extension in @('DLL', 'exe', 'PdB')) {
        $facade = Write-FixturePackage -ExtraEntries @("tools/Unexpected.$extension")
        Assert-Rejected { Assert-FixtureFacade $facade } 'must not contain executable'
    }
    $facade = Write-FixturePackage -DuplicateEntry
    Assert-Rejected { Assert-FixtureFacade $facade } 'duplicate ZIP entries'
    $facade = Write-FixturePackage -ExtraEntries @('extra.nuspec')
    Assert-Rejected { Assert-FixtureFacade $facade } 'exactly one nuspec'
    $facade = Write-FixturePackage -FileName 'Unexpected.1.2.3.nupkg'
    Assert-Rejected { Assert-FixtureFacade $facade } 'filename'
    $facade = Write-FixturePackage -Id 'WrongId' -FileName 'SharpProof.1.2.3.nupkg'
    Assert-Rejected { Assert-FixtureFacade $facade } 'nuspec ID or version'
    $facade = Write-FixturePackage -Version '1.2.4'
    Assert-Rejected { Assert-FixtureFacade $facade } 'nuspec ID or version'
    $facade = Write-FixturePackage -RepositoryCommit ('b' * 40)
    Assert-Rejected { Assert-FixtureFacade $facade } 'repository commit'
    $facade = Write-FixturePackage -RepositoryCommit ('A' * 40)
    Assert-Rejected { Assert-FixtureFacade $facade } 'repository commit'
    $facade = Write-FixturePackage
    Assert-Rejected { Test-SharpProofFacadePackage -PackagePath $facade -PackageVersion '1.2.3' -RepositoryCommit ('A' * 40) } 'lowercase hash'
    Assert-Rejected { Test-SharpProofFacadePackage -PackagePath $facade -PackageVersion '1.2.3' -RepositoryCommit ($commit + "`n") } 'lowercase hash'
    foreach ($dependency in @('', $validDependency.Replace('[1.2.3]', '[1.2.4]'),
            $validDependency.Replace('SharpProof.Verifier', 'SharpProof.Attributes'),
            $validDependency.Replace('[1.2.3]', '1.2.3'),
            $validDependency.Replace('</group>', '<dependency id="Other" version="[1.2.3]" /></group>'))) {
        $facade = Write-FixturePackage -Dependency $dependency
        Assert-Rejected { Assert-FixtureFacade $facade } 'exactly one dependency'
    }
    $facade = Write-FixturePackage -Dtd
    Assert-Rejected { Assert-FixtureFacade $facade } 'DTD'
    $facade = Write-FixturePackage
    Assert-FixtureFacade $facade
    Write-Output 'Metadata facade regressions passed: exact identity/dependency/provenance, 3 main + 2 symbol packages, duplicate/archive/payload/DTD rejection.'
}
finally {
    $fullFixture = [IO.Path]::GetFullPath($fixture)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $fullFixture.StartsWith($temporaryParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($fullFixture) -notlike 'sharpproof-facade-regression-*') { throw 'Refusing to remove an unexpected regression fixture.' }
    Remove-Item -LiteralPath $fullFixture -Recurse -Force
}
