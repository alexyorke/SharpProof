[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$generatorScripts = @(
    'Generate-DiagnosticDescriptors.ps1',
    'Generate-CSharpScalarSemantics.ps1',
    'Generate-ContractApiCatalog.ps1',
    'Generate-AnalyzerDiagnosticCatalog.ps1',
    'Generate-ProjectionCatalog.ps1',
    'Generate-LauncherArguments.ps1',
    'Generate-BoundContractModel.ps1',
    'Generate-EffectContractMappings.ps1',
    'Generate-OperationSupportCatalog.ps1',
    'Generate-IrModel.ps1',
    'Generate-ApiSpecCatalog.ps1',
    'Generate-ProtocolModel.ps1',
    'Generate-CompilerArtifactModel.ps1',
    'Generate-DeclarativeModels.ps1'
)

foreach ($generatorScript in $generatorScripts) {
    & (Join-Path $repositoryRoot ('scripts/' + $generatorScript)) -Verify
}

Write-Host "Verified $($generatorScripts.Count) generated-output scripts."
