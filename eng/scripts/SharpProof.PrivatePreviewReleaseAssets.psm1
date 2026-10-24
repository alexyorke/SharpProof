Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SharpProofPrivatePreviewReleaseAssetSizes {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Paths)

    # GitHub requires each release asset to be strictly under 2 GiB. Inspect
    # actual retained files without rewriting qualified archive or signed bytes.
    foreach ($path in $Paths) {
        $item = Get-Item -LiteralPath $path -Force
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Private release assets must be regular files without links.'
        }
        if ($item.Length -ge 2147483648L) {
            throw "Private release asset must be strictly under 2 GiB: $($item.Name)"
        }
    }
}

Export-ModuleMember -Function Assert-SharpProofPrivatePreviewReleaseAssetSizes
