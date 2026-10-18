Set-StrictMode -Version Latest

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

    $validatorType = 'SharpProofSymbolPackageValidator' -as [type]
    if ($null -eq $validatorType) {
        $validatorType = @(
            Add-Type `
                -Path (Join-Path `
                    $PSScriptRoot `
                    'SharpProof.SymbolPackageValidator.cs') `
                -PassThru |
                Where-Object Name -eq 'SharpProofSymbolPackageValidator'
        )[0]
    }
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
