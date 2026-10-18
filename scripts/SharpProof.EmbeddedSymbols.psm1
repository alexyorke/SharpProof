Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofEmbeddedPdb {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$AssemblyPath)

    $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $AssemblyPath).Path)
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $entries = @($pe.ReadDebugDirectory())
            $embedded = @($entries | Where-Object Type -EQ EmbeddedPortablePdb)
            if ($embedded.Count -eq 0) { return $null }
            if ($embedded.Count -ne 1 -or @($entries | Where-Object Type -EQ CodeView).Count -ne 1) {
                throw 'An embedded-symbol assembly must contain exactly one embedded PDB and one CodeView record.'
            }
            $entry = $embedded[0]
            if ($entry.DataSize -lt 8 -or $entry.DataPointer -lt 0 -or
                ([long]$entry.DataPointer + $entry.DataSize) -gt $stream.Length) {
                throw 'Embedded PDB debug data is outside the assembly.'
            }
            $stream.Position = $entry.DataPointer
            $header = [byte[]]::new(8)
            $stream.ReadExactly($header, 0, $header.Length)
            $length = [BitConverter]::ToInt32($header, 4)
            if ([Text.Encoding]::ASCII.GetString($header, 0, 4) -cne 'MPDB' -or
                $length -lt 4 -or $length -gt 67108864) {
                throw 'Embedded PDB header or declared size is invalid.'
            }
            $compressed = [byte[]]::new($entry.DataSize - 8)
            $stream.ReadExactly($compressed, 0, $compressed.Length)
            $compressedStream = [IO.MemoryStream]::new($compressed, $false)
            try {
                $deflate = [IO.Compression.DeflateStream]::new($compressedStream, [IO.Compression.CompressionMode]::Decompress)
                try {
                    $bytes = [byte[]]::new($length)
                    $offset = 0
                    while ($offset -lt $length) {
                        $read = $deflate.Read($bytes, $offset, $length - $offset)
                        if ($read -eq 0) { throw 'Embedded PDB is shorter than its declared size.' }
                        $offset += $read
                    }
                    if ($deflate.ReadByte() -ne -1 -or
                        [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -cne 'BSJB') {
                        throw 'Embedded PDB payload or declared size is invalid.'
                    }
                    Write-Output -NoEnumerate -InputObject $bytes
                }
                finally { $deflate.Dispose() }
            }
            finally { $compressedStream.Dispose() }
        }
        finally { $pe.Dispose() }
    }
    finally { $stream.Dispose() }
}

Export-ModuleMember -Function Get-SharpProofEmbeddedPdb
