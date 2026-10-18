[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne
    [Runtime.InteropServices.Architecture]::X64 -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne
    [Runtime.InteropServices.Architecture]::X64) {
    throw 'The Windows native payload check requires a Windows x64 OS and process.'
}

$source = (Resolve-Path -LiteralPath $PackageSource -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'The package source must be a directory.'
}
$packages = @(Get-ChildItem -LiteralPath $source -Filter 'SharpProof.Verifier.*.nupkg' -File)
if ($packages.Count -ne 1) {
    throw "Expected one packed SharpProof.Verifier package; found $($packages.Count)."
}

$catalogPath = Join-Path $PSScriptRoot '../eng/container/toolchain.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$expected = $catalog.z3.windows
$entryPath = 'tools/native/win-x64/libz3.dll'
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
