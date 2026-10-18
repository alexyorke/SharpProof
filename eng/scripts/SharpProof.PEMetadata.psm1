Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofMetadataModuleVersionId {
    param(
        [Parameter(Mandatory = $true)]
        [System.Reflection.Metadata.MetadataReader]$Reader
    )

    $module = $Reader.GetModuleDefinition()
    return $Reader.GetGuid($module.Mvid).ToString('D')
}

function Get-SharpProofModuleVersionId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolved = [IO.Path]::GetFullPath($Path)
    $stream = [IO.File]::OpenRead($resolved)
    try {
        $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            if (-not $peReader.HasMetadata) {
                throw "The portable executable has no metadata: $resolved"
            }
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader(
                $peReader)
            return Get-SharpProofMetadataModuleVersionId -Reader $metadata
        }
        finally {
            $peReader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

Export-ModuleMember -Function Get-SharpProofModuleVersionId,
    Get-SharpProofMetadataModuleVersionId
