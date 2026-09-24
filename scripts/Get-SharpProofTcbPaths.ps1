function Get-SharpProofTcbPaths {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $Contract,

        [Parameter()]
        [switch]$IncludeAcceptanceContract,

        [Parameter()]
        $ProductionInventory
    )

    if ($null -eq $Contract.trustedKernel -or
        $null -eq $Contract.trustedComputingBase) {
        throw 'The acceptance contract must declare the trusted computing base.'
    }

    $paths = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $addPath = {
        param(
            [Parameter(Mandatory = $true)]
            $Value
        )

        $path = [string]$Value
        if ([string]::IsNullOrWhiteSpace($path)) {
            throw 'Trusted-computing-base path is blank.'
        }
        if ($path.Contains('\') -or
            [IO.Path]::IsPathRooted($path) -or
            $path.StartsWith('/', [StringComparison]::Ordinal) -or
            $path.EndsWith('/', [StringComparison]::Ordinal) -or
            $path.Contains('//')) {
            throw "Trusted-computing-base path is not canonical: '$path'."
        }
        $segments = $path.Split('/')
        foreach ($segment in $segments) {
            if ($segment -eq '.' -or $segment -eq '..') {
                throw "Trusted-computing-base path contains a dot segment: '$path'."
            }
        }
        if (-not $seen.Add($path)) {
            throw "Trusted-computing-base path is duplicated: '$path'."
        }

        [void]$paths.Add($path)
    }

    if ($IncludeAcceptanceContract) {
        & $addPath 'eng/acceptance/contract.json'
    }

    foreach ($path in @($Contract.trustedKernel.paths)) {
        & $addPath $path
    }

    foreach ($component in @(
            $Contract.trustedComputingBase.components)) {
        foreach ($path in @($component.paths)) {
            & $addPath $path
        }
    }

    if ($null -ne $ProductionInventory) {
        if ($null -eq $ProductionInventory.projects) {
            throw 'The production inventory authority has no projects.'
        }
        $pipelineProjectPaths = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($projectPath in @(
                $Contract.trustedComputingBase.pipelineCompileProjects)) {
            $canonicalProjectPath = [string]$projectPath
            if ([string]::IsNullOrWhiteSpace($canonicalProjectPath) -or
                $canonicalProjectPath.Contains('\') -or
                [IO.Path]::IsPathRooted($canonicalProjectPath) -or
                $canonicalProjectPath.StartsWith('/', [StringComparison]::Ordinal) -or
                $canonicalProjectPath.EndsWith('/', [StringComparison]::Ordinal) -or
                $canonicalProjectPath.Split('/') -contains '.' -or
                $canonicalProjectPath.Split('/') -contains '..' -or
                -not $canonicalProjectPath.EndsWith(
                    '.csproj',
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Trusted pipeline project path is not canonical: '$canonicalProjectPath'."
            }
            if (-not $pipelineProjectPaths.Add($canonicalProjectPath)) {
                throw "Trusted pipeline project is duplicated: '$canonicalProjectPath'."
            }
        }
        if ($pipelineProjectPaths.Count -eq 0) {
            throw 'The trusted computing base must declare its production pipeline projects.'
        }

        $compilePaths = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $inventoryProjects = [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::Ordinal)
        foreach ($project in @($ProductionInventory.projects)) {
            $inventoryProjectPath = [string]$project.projectPath
            if ([string]::IsNullOrWhiteSpace($inventoryProjectPath) -or
                -not $inventoryProjects.TryAdd(
                    $inventoryProjectPath,
                    $project)) {
                throw "Production inventory project path is blank or duplicated: '$inventoryProjectPath'."
            }
            foreach ($file in @($project.compile)) {
                [void]$compilePaths.Add([string]$file.path)
            }
        }

        foreach ($projectPath in $pipelineProjectPaths) {
            if (-not $inventoryProjects.ContainsKey($projectPath)) {
                throw (
                    "Trusted pipeline project is not in the production " +
                    "inventory: '$projectPath'.")
            }
            if (-not $seen.Contains($projectPath)) {
                throw (
                    "Trusted pipeline project is not classified in the " +
                    "trusted computing base: '$projectPath'.")
            }

            foreach ($file in @($inventoryProjects[$projectPath].compile)) {
                $compilePath = [string]$file.path
                if (-not $seen.Contains($compilePath)) {
                    throw (
                        "Production pipeline Compile item is not classified " +
                        "in the trusted computing base: '$compilePath'.")
                }
            }
        }

        foreach ($path in $paths) {
            if ($path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -and
                -not $compilePaths.Contains($path)) {
                throw (
                    "Trusted-computing-base source is not an evaluated " +
                    "production Compile item: '$path'.")
            }
        }
    }

    return $paths.ToArray()
}
