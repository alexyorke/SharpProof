Set-StrictMode -Version Latest

function Test-SharpProofFacadePackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$PackageVersion,
        [Parameter(Mandatory = $true)][string]$RepositoryCommit
    )

    $validatorType = Get-SharpProofSymbolPackageValidatorType
    try {
        $validatorType.GetMethod('ValidateFacade').Invoke(
            $null, @($PackagePath, $PackageVersion, $RepositoryCommit))
    }
    catch [Reflection.TargetInvocationException] { throw $_.Exception.InnerException }
}

function Get-SharpProofSymbolPackageValidatorType {
    $validatorType = 'SharpProofSymbolPackageValidator' -as [type]
    if ($null -eq $validatorType) {
        $validatorType = @(
            Add-Type -Path (Join-Path $PSScriptRoot 'SharpProof.SymbolPackageValidator.cs') -PassThru |
                Where-Object Name -eq 'SharpProofSymbolPackageValidator'
        )[0]
    }
    return $validatorType
}

function Test-SharpProofSymbolPackagePair {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$SymbolPackagePath,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [string]$PackageVersion,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryCommit
    )

    $validatorType = Get-SharpProofSymbolPackageValidatorType
    try {
        $validatorType.GetMethod('Validate').Invoke(
            $null,
            @(
                $PackagePath,
                $SymbolPackagePath,
                $PackageId,
                $PackageVersion,
                $RepositoryCommit))
    }
    catch [Reflection.TargetInvocationException] {
        throw $_.Exception.InnerException
    }
}
