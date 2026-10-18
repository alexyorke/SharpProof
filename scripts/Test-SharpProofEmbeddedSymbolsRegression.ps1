[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-SharpProofSymbolPackages.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.EmbeddedSymbols.psm1') -Force
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$root = Join-Path $parent ('sharpproof-embedded-regression-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)

function New-EmbeddedFixture([string]$Name, [string]$DebugType = 'embedded', [bool]$EmbedSources = $true) {
    $directory = Join-Path $root $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::Copy((Join-Path $repository 'global.json'), (Join-Path $directory 'global.json'))
    [IO.File]::WriteAllText((Join-Path $directory 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>', $encoding)
    [IO.File]::WriteAllText((Join-Path $directory 'Fixture.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><DebugType>$DebugType</DebugType><EmbedAllSources>$($EmbedSources.ToString().ToLowerInvariant())</EmbedAllSources><Deterministic>true</Deterministic><GenerateAssemblyInfo>false</GenerateAssemblyInfo><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
"@, $encoding)
    # A substantial comment exercises compressed embedded-source records.
    [IO.File]::WriteAllText((Join-Path $directory 'Fixture.cs'),
        ('// ' + ($Name * 500) + "`npublic static class Fixture { public static int Value() => $($Name.Length); }`n"), $encoding)
    Push-Location $directory
    try {
        $output = (& dotnet build Fixture.csproj -c Release --nologo '-p:UseSharedCompilation=false' 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) { throw "Embedded-symbol fixture build failed: $output" }
    }
    finally { Pop-Location }
    $assembly = Join-Path $directory 'bin/Release/net10.0/Fixture.dll'
    $pdb = [IO.Path]::ChangeExtension($assembly, 'pdb')
    if ($DebugType -eq 'embedded') { [IO.File]::WriteAllBytes($pdb, (Get-SharpProofEmbeddedPdb -AssemblyPath $assembly)) }
    return [pscustomobject]@{ Assembly = $assembly; Pdb = $pdb }
}

function Assert-Rejected([scriptblock]$Action, [string]$Expected = '') {
    $rejected = $false
    try { & $Action | Out-Null }
    catch {
        if ($Expected -and $_.Exception.Message -notlike "*$Expected*") { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Embedded-symbol regression accepted corrupt or mismatched input ($Expected)." }
}

try {
    $valid = New-EmbeddedFixture 'valid'
    Test-SharpProofEmbeddedPdbPair -AssemblyPath $valid.Assembly -PdbPath $valid.Pdb -RequireEmbedded $true
    $other = New-EmbeddedFixture 'different'
    Assert-Rejected { Test-SharpProofEmbeddedPdbPair -AssemblyPath $valid.Assembly -PdbPath $other.Pdb -RequireEmbedded $true } 'exact external portable PDB bytes'
    Assert-Rejected { Test-SharpProofEmbeddedPdbPair -AssemblyPath $valid.Assembly -PdbPath $valid.Pdb -RequireEmbedded $false } 'Attributes runtime assembly'
    $portable = New-EmbeddedFixture 'portable' -DebugType portable
    Test-SharpProofEmbeddedPdbPair -AssemblyPath $portable.Assembly -PdbPath $portable.Pdb -RequireEmbedded $false
    Assert-Rejected { Test-SharpProofEmbeddedPdbPair -AssemblyPath $portable.Assembly -PdbPath $portable.Pdb -RequireEmbedded $true } 'missing its embedded'
    $withoutSources = New-EmbeddedFixture 'noSources' -EmbedSources $false
    Assert-Rejected { Test-SharpProofEmbeddedPdbPair -AssemblyPath $withoutSources.Assembly -PdbPath $withoutSources.Pdb -RequireEmbedded $true } 'no embedded source'

    [byte[]]$corruptSourcePdb = [IO.File]::ReadAllBytes($valid.Pdb)
    $pdbStream = [IO.MemoryStream]::new($corruptSourcePdb, $false)
    $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
    try {
        $metadata = $provider.GetMetadataReader()
        $firstDocument = $metadata.GetDocument(@($metadata.Documents)[0])
        [byte[]]$sourceHash = $metadata.GetBlobBytes($firstDocument.Hash)
    }
    finally { $provider.Dispose(); $pdbStream.Dispose() }
    $hashOffset = -1
    for ($candidate = 0; $candidate -le $corruptSourcePdb.Length - $sourceHash.Length; $candidate++) {
        $sourceHashMatches = $true
        for ($index = 0; $index -lt $sourceHash.Length; $index++) {
            if ($corruptSourcePdb[$candidate + $index] -ne $sourceHash[$index]) { $sourceHashMatches = $false; break }
        }
        if ($sourceHashMatches) { $hashOffset = $candidate; break }
    }
    if ($hashOffset -lt 0) { throw 'The source-checksum negative-control fixture could not locate its document checksum.' }
    $corruptSourcePdb[$hashOffset] = $corruptSourcePdb[$hashOffset] -bxor 1
    Assert-Rejected { Test-SharpProofEmbeddedSources -PdbBytes $corruptSourcePdb } 'PDB document SHA256'

    $stream = [IO.File]::OpenRead($valid.Assembly)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $entries = @($pe.ReadDebugDirectory())
        $embeddedIndex = 0
        while ($entries[$embeddedIndex].Type -ne 'EmbeddedPortablePdb') { $embeddedIndex++ }
        $embedded = $entries[$embeddedIndex]
        $debugRva = $pe.PEHeaders.PEHeader.DebugTableDirectory.RelativeVirtualAddress
        $section = @($pe.PEHeaders.SectionHeaders | Where-Object {
                $debugRva -ge $_.VirtualAddress -and $debugRva -lt $_.VirtualAddress + [Math]::Max($_.VirtualSize, $_.SizeOfRawData)
            })[0]
        $debugOffset = $section.PointerToRawData + $debugRva - $section.VirtualAddress
    }
    finally { $pe.Dispose(); $stream.Dispose() }
    [byte[]]$original = [IO.File]::ReadAllBytes($valid.Assembly)
    $mutated = Join-Path $root 'mutated.dll'
    foreach ($mutation in @('signature', 'short-size', 'long-size', 'compressed', 'bounds', 'duplicate', 'no-codeview')) {
        [byte[]]$bytes = $original.Clone()
        switch ($mutation) {
            'signature' { $bytes[$embedded.DataPointer] = $bytes[$embedded.DataPointer] -bxor 255 }
            'short-size' { [BitConverter]::GetBytes([BitConverter]::ToInt32($bytes, $embedded.DataPointer + 4) - 1).CopyTo($bytes, $embedded.DataPointer + 4) }
            'long-size' { [BitConverter]::GetBytes([BitConverter]::ToInt32($bytes, $embedded.DataPointer + 4) + 1).CopyTo($bytes, $embedded.DataPointer + 4) }
            'compressed' { $bytes[$embedded.DataPointer + 8] = 255 }
            'bounds' { [BitConverter]::GetBytes([int]$bytes.Length).CopyTo($bytes, $debugOffset + $embeddedIndex * 28 + 24) }
            'duplicate' {
                $spare = 0
                while ($entries[$spare].Type -in @('EmbeddedPortablePdb', 'CodeView')) { $spare++ }
                [BitConverter]::GetBytes([int]17).CopyTo($bytes, $debugOffset + $spare * 28 + 12)
            }
            'no-codeview' {
                $codeView = 0
                while ($entries[$codeView].Type -ne 'CodeView') { $codeView++ }
                [BitConverter]::GetBytes([int]0).CopyTo($bytes, $debugOffset + $codeView * 28 + 12)
            }
        }
        [IO.File]::WriteAllBytes($mutated, $bytes)
        Assert-Rejected { Get-SharpProofEmbeddedPdb -AssemblyPath $mutated }
    }
    Write-Output 'Embedded-symbol regressions passed: exact portable/embedded pairing, source checksums, missing symbols/sources, and malformed embedded-debug records.'
}
finally {
    $fullRoot = [IO.Path]::GetFullPath($root)
    if (-not $fullRoot.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($fullRoot) -notlike 'sharpproof-embedded-regression-*') { throw 'Refusing to remove an unexpected embedded-symbol fixture.' }
    Remove-Item -LiteralPath $fullRoot -Recurse -Force
}
