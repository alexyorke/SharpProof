param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolved = [IO.Path]::GetFullPath($Path)
$stream = [IO.File]::OpenRead($resolved)
try {
    $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if (-not $peReader.HasMetadata) {
            throw "The portable executable has no metadata: $resolved"
        }

        $metadata =
            [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader(
                $peReader)
        $metadata.GetGuid(
            $metadata.GetModuleDefinition().Mvid).ToString('D')
    }
    finally {
        $peReader.Dispose()
    }
}
finally {
    $stream.Dispose()
}
