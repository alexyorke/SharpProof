[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$actualHost = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$actualProcess = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if (-not $IsWindows -or $actualHost -cne $Architecture -or $actualProcess -cne $Architecture) {
    throw "The Windows payload check requires matching $Architecture OS and process (host=$actualHost; process=$actualProcess)."
}

function Assert-WindowsPayloadPrerequisites([string]$Library, [string]$PayloadArchitecture) {
    Add-Type -AssemblyName System.Reflection.Metadata
    $stream = [IO.File]::OpenRead($Library)
    $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $expectedMachine = if ($PayloadArchitecture -eq 'arm64') { 0xaa64 } else { 0x8664 }
        if ([int]$reader.PEHeaders.CoffHeader.Machine -ne $expectedMachine -or
            $null -eq $reader.PEHeaders.PEHeader -or [int]$reader.PEHeaders.PEHeader.Magic -ne 0x20b) {
            throw "The packed DLL does not have the expected native $PayloadArchitecture PE32+ machine."
        }
        $modules = [Collections.Generic.List[string]]::new()
        foreach ($descriptor in @(
                @{ Directory = $reader.PEHeaders.PEHeader.ImportTableDirectory; Stride = 20; NameOffset = 12; Delay = $false },
                @{ Directory = $reader.PEHeaders.PEHeader.DelayImportTableDirectory; Stride = 32; NameOffset = 4; Delay = $true }
            )) {
            $directory = $descriptor.Directory
            if ($directory.RelativeVirtualAddress -eq 0) { continue }
            if ($directory.Size -lt $descriptor.Stride -or $directory.Size -gt 1048576) { throw 'Malformed or oversized PE import directory.' }
            [byte[]]$table = @($reader.GetSectionData($directory.RelativeVirtualAddress).GetContent(0, $directory.Size))
            $terminated = $false
            for ($offset = 0; $offset + $descriptor.Stride -le $table.Length; $offset += $descriptor.Stride) {
                $zero = $true
                for ($index = 0; $index -lt $descriptor.Stride; $index++) {
                    if ($table[$offset + $index] -ne 0) { $zero = $false; break }
                }
                if ($zero) { $terminated = $true; break }
                if ($descriptor.Delay -and [BitConverter]::ToUInt32($table, $offset) -ne 1) { throw 'Unsupported non-RVA PE delay-import descriptor.' }
                $nameRva = [BitConverter]::ToInt32($table, $offset + $descriptor.NameOffset)
                if ($nameRva -le 0) { throw 'Malformed PE import module address.' }
                $block = $reader.GetSectionData($nameRva)
                [byte[]]$nameBytes = @($block.GetContent(0, [Math]::Min(256, $block.Length)))
                $end = [Array]::IndexOf($nameBytes, [byte]0)
                if ($end -le 0) { throw 'PE import module name is not bounded and terminated.' }
                $module = [Text.Encoding]::ASCII.GetString($nameBytes, 0, $end)
                if ($module -notmatch '^[a-zA-Z0-9._+\-]+\.dll$') { throw 'PE import module name is malformed.' }
                $modules.Add($module)
            }
            if (-not $terminated) { throw 'PE import directory has no complete terminating descriptor.' }
        }
        $imports = @($modules | Sort-Object -Unique)
        $externalCpp = @($imports | Where-Object { $_ -match '^(vcruntime|msvcp|libc\+\+|libstdc\+\+|libgcc).*\.dll$' })
        if ($externalCpp.Count) { throw "The $PayloadArchitecture payload requires an external C++ runtime: $($externalCpp -join ', ')." }
        $unexpectedImports = @($imports | Where-Object { $_ -notmatch '^(KERNEL32|api-ms-win-crt-[a-zA-Z0-9\-]+)\.dll$' })
        if ($unexpectedImports.Count) { throw "The $PayloadArchitecture payload imports DLLs outside its Windows system-runtime policy: $($unexpectedImports -join ', ')." }
        Write-Host "Packed $PayloadArchitecture PE machine and import prerequisites passed ($($imports.Count) imported modules)."
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

$source = (Resolve-Path -LiteralPath $PackageSource -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'The package source must be a directory.'
}
$packages = @(Get-ChildItem -LiteralPath $source -Filter 'SharpProof.Verifier.*.nupkg' -File)
if ($packages.Count -ne 1) {
    throw "Expected one packed SharpProof.Verifier package; found $($packages.Count)."
}

$catalogPath = Join-Path $PSScriptRoot '../container/toolchain.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$expected = if ($Architecture -eq 'arm64') { $catalog.z3.windowsArm64 } else { $catalog.z3.windows }
$entryPath = "tools/native/win-$Architecture/libz3.dll"
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $entry = $archive.GetEntry($entryPath)
    if ($null -eq $entry -or $entry.Length -ne [int64]$expected.libraryBytes) {
        throw 'The packed Windows Z3 DLL is missing or has the wrong size.'
    }
    $inputStream = $entry.Open()
    try {
        $digest = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($inputStream)).ToLowerInvariant()
    }
    finally { $inputStream.Dispose() }
    if ($digest -cne [string]$expected.librarySha256) {
        throw 'The packed Windows Z3 DLL has the wrong SHA-256 digest.'
    }

    $scratch = Join-Path ([IO.Path]::GetTempPath()) (
        'sharpproof-win-z3-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($scratch) | Out-Null
    $library = Join-Path $scratch 'libz3.dll'
    try {
        $inputStream = $entry.Open()
        try {
            $outputStream = [IO.FileStream]::new(
                $library,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None)
            try { $inputStream.CopyTo($outputStream) }
            finally { $outputStream.Dispose() }
        }
        finally { $inputStream.Dispose() }

        Assert-WindowsPayloadPrerequisites -Library $library -PayloadArchitecture $Architecture
        $handle = [Runtime.InteropServices.NativeLibrary]::Load($library)
        try {
            $export = [Runtime.InteropServices.NativeLibrary]::GetExport(
                $handle, 'Z3_get_version')
            if ($export -eq [IntPtr]::Zero) {
                throw 'The packed Windows Z3 DLL has no Z3_get_version export.'
            }
        }
        finally { [Runtime.InteropServices.NativeLibrary]::Free($handle) }
    }
    finally {
        if (Test-Path -LiteralPath $library) {
            Remove-Item -LiteralPath $library -Force
        }
        Remove-Item -LiteralPath $scratch -Force
    }
}
finally { $archive.Dispose() }

Write-Host 'Packed Windows Z3 DLL hash, load, and export check passed.'
