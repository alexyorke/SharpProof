[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryPrefix,

    [Parameter(Mandatory = $true)]
    [string]$AuthorityCommit,

    [Parameter(Mandatory = $true)]
    [string]$ModuleIdentities
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

[xml]$coverageDocument = Get-Content -LiteralPath $Path -Raw
foreach ($class in $coverageDocument.SelectNodes('//class[@filename]')) {
    $fileName = [string]$class.filename
    if (-not [IO.Path]::IsPathRooted($fileName)) {
        continue
    }
    $fullPath = [IO.Path]::GetFullPath($fileName)
    if (-not $fullPath.StartsWith(
            $RepositoryPrefix,
            [StringComparison]::Ordinal)) {
        continue
    }
    $class.SetAttribute(
        'filename',
        $fullPath.Substring($RepositoryPrefix.Length).Replace('\', '/'))
}
$authorityNodes = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
        $coverageDocument.SelectNodes('/coverage/sharpProofAuthority')))
if ($authorityNodes.Length -ne 0) {
    throw "Coverage report already contains authority metadata: $Path"
}
$authorityNode = $coverageDocument.CreateElement('sharpProofAuthority')
$authorityNode.SetAttribute('schemaVersion', '1')
$authorityNode.SetAttribute('commit', $AuthorityCommit)
$authorityNode.SetAttribute('modules', $ModuleIdentities)
[void]$coverageDocument.DocumentElement.AppendChild($authorityNode)

$writerSettings = [Xml.XmlWriterSettings]::new()
$writerSettings.Encoding = [Text.UTF8Encoding]::new($false)
$writerSettings.Indent = $true
$writerSettings.NewLineChars = "`n"
$writerSettings.NewLineHandling = [Xml.NewLineHandling]::Replace
$writer = [Xml.XmlWriter]::Create($Path, $writerSettings)
try {
    $coverageDocument.Save($writer)
}
finally {
    $writer.Dispose()
}
