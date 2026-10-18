[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [object[]]$Documents
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Documents.Count -eq 0) { return }
$dotnetItem = Get-Item -LiteralPath (Get-Command dotnet -CommandType Application |
    Select-Object -First 1).Source
$dotnetTarget = $dotnetItem.ResolveLinkTarget($true)
$dotnetPath = if ($null -eq $dotnetTarget) { $dotnetItem.FullName } else { $dotnetTarget.FullName }
$sdkVersion = ([string](& $dotnetPath --version)).Trim()
$pin = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Raw | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $sdkVersion -cne [string]$pin.sdk.version) {
    throw 'Declaration classification requires the repository-pinned SDK.'
}
$dotnetRoot = [IO.Path]::GetDirectoryName($dotnetPath)
$sdkPath = Join-Path $dotnetRoot "sdk/$sdkVersion"
$runtimeConfigPath = Join-Path $sdkPath 'MSBuild.runtimeconfig.json'
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$framework = $runtimeConfig.runtimeOptions.framework
$referencePath = Join-Path $dotnetRoot (
    'packs/' + $framework.name + '.Ref/' + $framework.version + '/ref/' +
    $runtimeConfig.runtimeOptions.tfm)
$roslynPath = Join-Path $sdkPath 'Roslyn/bincore'
$temporaryPath = Join-Path ([IO.Path]::GetTempPath()) ('SharpProofDeclarations.' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryPath) | Out-Null
try {
    $references = @(Get-ChildItem -LiteralPath $referencePath -Filter '*.dll' -File |
        Sort-Object Name | ForEach-Object { '-reference:' + $_.FullName })
    foreach ($name in @('Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll')) {
        $assemblyPath = Join-Path $roslynPath $name
        $references += '-reference:' + $assemblyPath
        Copy-Item -LiteralPath $assemblyPath -Destination (Join-Path $temporaryPath $name)
    }
    $hostPath = Join-Path $temporaryPath 'SharpProof.DeclarationLines.dll'
    $compilerArguments = @((Join-Path $roslynPath 'csc.dll'), '-nologo', '-noconfig', '-nostdlib+',
        '-target:exe', ('-out:' + $hostPath)) + $references + @(
        (Join-Path $PSScriptRoot 'SharpProof.DeclarationLines.cs'))
    $compilerOutput = @(& $dotnetPath @compilerArguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw ('Declaration classifier compilation failed: ' + ($compilerOutput -join [Environment]::NewLine))
    }
    $requestPath = Join-Path $temporaryPath 'request.json'
    $responsePath = Join-Path $temporaryPath 'response.json'
    [IO.File]::WriteAllText($requestPath, (ConvertTo-Json -InputObject @($Documents) -Depth 8),
        [Text.UTF8Encoding]::new($false))
    $hostOutput = @(& $dotnetPath exec --runtimeconfig $runtimeConfigPath $hostPath $requestPath $responsePath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw ('Declaration classification failed: ' + ($hostOutput -join [Environment]::NewLine))
    }
    Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
}
finally {
    [IO.Directory]::Delete($temporaryPath, $true)
}
