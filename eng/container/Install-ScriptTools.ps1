[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CatalogPath,
    [string]$DestinationRoot = '/usr/local'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
$tools = $catalog.scriptTools
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-script-tools-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporaryRoot)
$binaryRoot = Join-Path $DestinationRoot 'bin'
[void][IO.Directory]::CreateDirectory($binaryRoot)
try {
    foreach ($name in @('psscriptAnalyzer', 'shellcheck', 'shfmt')) {
        $tool = $tools.$name
        $download = Join-Path $temporaryRoot $name
        Invoke-WebRequest -Uri $tool.url -OutFile $download
        if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ine $tool.sha256) {
            throw "Downloaded $name does not match its pinned SHA256."
        }
        switch ($name) {
            'psscriptAnalyzer' {
                $moduleRoot = Join-Path $DestinationRoot "share/powershell/Modules/PSScriptAnalyzer/$($tool.version)"
                [IO.Compression.ZipFile]::ExtractToDirectory($download, $moduleRoot)
                Import-Module (Join-Path $moduleRoot 'PSScriptAnalyzer.psd1') -RequiredVersion $tool.version -Force
            }
            'shellcheck' {
                & tar -xJf $download -C $temporaryRoot
                if ($LASTEXITCODE -ne 0) { throw 'Cannot extract ShellCheck.' }
                Copy-Item -LiteralPath (Join-Path $temporaryRoot "shellcheck-v$($tool.version)/shellcheck") -Destination $binaryRoot
            }
            'shfmt' {
                Copy-Item -LiteralPath $download -Destination (Join-Path $binaryRoot 'shfmt')
            }
        }
    }
    & chmod 755 (Join-Path $binaryRoot 'shellcheck') (Join-Path $binaryRoot 'shfmt')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot set script-tool executable permissions.' }
    & (Join-Path $binaryRoot 'shellcheck') --version
    if ($LASTEXITCODE -ne 0) { throw 'ShellCheck installation failed.' }
    & (Join-Path $binaryRoot 'shfmt') --version
    if ($LASTEXITCODE -ne 0) { throw 'shfmt installation failed.' }
}
finally {
    # Only the explicitly created download directory is disposable.
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
    if (-not $resolvedTemporary.StartsWith(
            (Join-Path ([IO.Path]::GetTempPath()) 'sharpproof-script-tools-'), [StringComparison]::Ordinal)) {
        throw 'Invalid script-tool temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
