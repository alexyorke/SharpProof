[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 0,

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 300,

    [Parameter()]
    [ValidateRange(1, 60000)]
    [int]$SmtQueryTimeoutMs = 100,

    [Parameter()]
    [ValidateRange(1, 600000)]
    [int]$SmtMethodBudgetMs = 500,

    [Parameter()]
    # The largest engine project is opt-in until its self-analysis scalability issue is resolved:
    # -Project 'SharpProof.Symbolic\SharpProof.Symbolic.csproj'
    [string[]]$Project = @(
        'SharpProof.Attributes\SharpProof.Attributes.csproj',
        'SharpProof.ProofCore\SharpProof.ProofCore.csproj',
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
        -MemoryLimitMb $MemoryLimitMb `
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
            -MemoryLimitMb $MemoryLimitMb `
            -TimeoutSeconds $TimeoutSeconds `
            build $projectPath `
            --configuration $Configuration `
            --no-restore `
            --no-dependencies `
            /p:SharpProofDogfood=true `
            /p:GeneratePackageOnBuild=false `
            /p:TreatWarningsAsErrors=false `
            /p:sharpproof_smt_mode=bounded `
            /p:sharpproof_smt_timeout_ms=$SmtQueryTimeoutMs `
            /p:sharpproof_smt_method_budget_ms=$SmtMethodBudgetMs
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
