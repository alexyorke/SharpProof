[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 300,

    [Parameter()]
    [string[]]$Project = @(
        'SharpProof.Attributes\SharpProof.Attributes.csproj',
        'SharpProof.Ir\SharpProof.Ir.csproj',
        'SharpProof.Specs\SharpProof.Specs.csproj',
        'SharpProof.Dataflow\SharpProof.Dataflow.csproj',
        'SharpProof.Frontend\SharpProof.Frontend.csproj',
        'SharpProof.Contracts\SharpProof.Contracts.csproj',
        'SharpProof.Effects\SharpProof.Effects.csproj',
        'SharpProof.Verify\SharpProof.Verify.csproj',
        'SharpProof.Smt\SharpProof.Smt.csproj',
        'SharpProof.Analyzer\SharpProof.Analyzer.csproj'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
$analyzerProject = Join-Path $repositoryRoot 'SharpProof.Analyzer\SharpProof.Analyzer.csproj'

Push-Location $repositoryRoot
try
{
    & $dotnetWrapper `
        -TimeoutSeconds $TimeoutSeconds `
        build $analyzerProject `
        --configuration $Configuration
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    foreach ($relativeProject in $Project)
    {
        $projectPath = (Resolve-Path (Join-Path $repositoryRoot $relativeProject)).Path
        Write-Host "Dogfooding $relativeProject"
        & $dotnetWrapper `
            -TimeoutSeconds $TimeoutSeconds `
            build $projectPath `
            --configuration $Configuration `
            --no-restore `
            --no-dependencies `
            /p:SharpProofDogfood=true `
            /p:GeneratePackageOnBuild=false `
            /p:TreatWarningsAsErrors=false `
            /p:SharpProofProfile=advisory `
            /p:SharpProofFeatures=all
        if ($LASTEXITCODE -ne 0)
        {
            exit $LASTEXITCODE
        }
    }
}
finally
{
    Pop-Location
}
