Set-StrictMode -Version Latest

function Get-SharpProofNuspecDependencyModel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = @($archive.Entries | Where-Object {
            $_.FullName.EndsWith(
                '.nuspec',
                [StringComparison]::OrdinalIgnoreCase)
        })
        if ($entries.Count -ne 1) {
            throw "Package '$PackagePath' must contain exactly one nuspec."
        }
        $reader = [IO.StreamReader]::new($entries[0].Open())
        try {
            [xml]$document = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $manager = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $manager.AddNamespace('n', $document.DocumentElement.NamespaceURI)
    $metadata = $document.SelectSingleNode(
        '/n:package/n:metadata',
        $manager)
    if ($null -eq $metadata) {
        throw "Package '$PackagePath' has no nuspec metadata."
    }
    $idNode = $metadata.SelectSingleNode('n:id', $manager)
    $versionNode = $metadata.SelectSingleNode('n:version', $manager)
    if ($null -eq $idNode -or
        $null -eq $versionNode) {
        throw "Package '$PackagePath' has incomplete nuspec metadata."
    }

    $groups = [Collections.Generic.List[object]]::new()
    $dependenciesNodes = @(
        $metadata.SelectNodes('n:dependencies', $manager)
    )
    if ($dependenciesNodes.Count -gt 1) {
        throw "Package '$PackagePath' has duplicate dependency containers."
    }
    if ($dependenciesNodes.Count -eq 1) {
        $container = $dependenciesNodes[0]
        $ungrouped = @($container.SelectNodes('n:dependency', $manager))
        if ($ungrouped.Count -ne 0) {
            throw "Package '$PackagePath' has an ungrouped dependency."
        }
        foreach ($group in @($container.SelectNodes('n:group', $manager))) {
            if ($group.Attributes.Count -ne 1 -or
                -not $group.HasAttribute('targetFramework')) {
                throw "Package '$PackagePath' has an invalid dependency group."
            }
            $dependencies = [Collections.Generic.List[object]]::new()
            foreach ($dependency in @(
                    $group.SelectNodes('n:dependency', $manager))) {
                if ($dependency.Attributes.Count -ne 2 -or
                    -not $dependency.HasAttribute('id') -or
                    -not $dependency.HasAttribute('version')) {
                    throw "Package '$PackagePath' has an invalid dependency."
                }
                $dependencies.Add([pscustomobject][ordered]@{
                    Id = $dependency.GetAttribute('id')
                    Version = $dependency.GetAttribute('version')
                })
            }
            $groups.Add([pscustomobject][ordered]@{
                TargetFramework = $group.GetAttribute('targetFramework')
                Dependencies = @($dependencies)
            })
        }
    }

    return [pscustomobject][ordered]@{
        Id = $idNode.InnerText
        Version = $versionNode.InnerText
        DependencyGroups = @($groups)
    }
}

function Get-SharpProofPackageDependencyGraph {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$PackagePaths,

        [Parameter()]
        [string]$ContractPath = (Join-Path `
            (Join-Path $PSScriptRoot '..') `
            'eng/release/package-dependency-contract.json')
    )

    $contract = Get-Content -LiteralPath $ContractPath -Raw |
        ConvertFrom-Json
    if ([int]$contract.schemaVersion -ne 1) {
        throw 'Unsupported package dependency contract schema.'
    }
    $expectedPackages = @($contract.packages)
    $models = @($PackagePaths | ForEach-Object {
        [pscustomobject][ordered]@{
            Path = $_
            Extension = [IO.Path]::GetExtension($_).ToLowerInvariant()
            Nuspec = Get-SharpProofNuspecDependencyModel -PackagePath $_
        }
    })
    foreach ($extension in @('.nupkg', '.snupkg')) {
        $extensionModels = @($models | Where-Object {
            $_.Extension -eq $extension
        })
        $actualIds = @($extensionModels.Nuspec.Id | Sort-Object)
        $expectedIds = @($expectedPackages.id | Sort-Object)
        if ($extensionModels.Count -ne $expectedPackages.Count -or
            ($actualIds -join '|') -ne ($expectedIds -join '|')) {
            throw "Package dependency authority requires the exact $extension graph."
        }
    }

    $versions = @($models.Nuspec.Version | Sort-Object -Unique)
    if ($versions.Count -ne 1) {
        throw 'Package dependency authority requires one exact version.'
    }
    $version = [string]$versions[0]
    foreach ($model in $models) {
        $expected = @($expectedPackages | Where-Object {
            [string]$_.id -eq [string]$model.Nuspec.Id
        })
        if ($expected.Count -ne 1) {
            throw "Unexpected package dependency owner '$($model.Nuspec.Id)'."
        }
        $expectedGroups = @($expected[0].dependencyGroups)
        $actualGroups = @($model.Nuspec.DependencyGroups)
        if ($actualGroups.Count -ne $expectedGroups.Count) {
            throw "Package '$($model.Nuspec.Id)' has an invalid dependency group count."
        }
        $seenFrameworks = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($group in $actualGroups) {
            if (-not $seenFrameworks.Add([string]$group.TargetFramework)) {
                throw "Package '$($model.Nuspec.Id)' has duplicate dependency groups."
            }
            $expectedGroup = @($expectedGroups | Where-Object {
                [string]$_.targetFramework -eq
                    [string]$group.TargetFramework
            })
            if ($expectedGroup.Count -ne 1) {
                throw "Package '$($model.Nuspec.Id)' has an unsupported dependency framework."
            }
            $actualDependencies = @($group.Dependencies)
            $expectedIds = @($expectedGroup[0].dependencies)
            if ($actualDependencies.Count -ne $expectedIds.Count) {
                throw "Package '$($model.Nuspec.Id)' has an invalid dependency count."
            }
            $seenIds = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            foreach ($dependency in $actualDependencies) {
                if (-not $seenIds.Add([string]$dependency.Id) -or
                    [string]$dependency.Id -notin $expectedIds -or
                    [string]$dependency.Version -ne "[$version]") {
                    throw "Package '$($model.Nuspec.Id)' has an invalid dependency identity or version."
                }
            }
        }
    }

    $edges = [Collections.Generic.List[object]]::new()
    foreach ($model in @($models | Where-Object {
            $_.Extension -eq '.nupkg'
        })) {
        foreach ($group in @($model.Nuspec.DependencyGroups)) {
            foreach ($dependency in @($group.Dependencies)) {
                $edges.Add([pscustomobject][ordered]@{
                    FromId = [string]$model.Nuspec.Id
                    ToId = [string]$dependency.Id
                    Version = $version
                    TargetFramework = [string]$group.TargetFramework
                })
            }
        }
    }
    return @($edges | Sort-Object FromId, ToId, TargetFramework)
}

function Get-SharpProofDependencySpdxId {
    param([Parameter(Mandatory = $true)][string]$Name)

    return 'SPDXRef-Package-' + ($Name -replace '[^A-Za-z0-9.-]', '-')
}

function Test-SharpProofSbomDependencyGraph {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Relationships,

        [Parameter(Mandatory = $true)]
        [object[]]$DependencyGraph
    )

    $actual = @($Relationships | Where-Object {
        [string]$_.relationshipType -eq 'DEPENDS_ON'
    })
    if ($actual.Count -ne $DependencyGraph.Count) {
        throw 'SPDX SBOM package dependency graph is not exact.'
    }
    foreach ($edge in $DependencyGraph) {
        $from = Get-SharpProofDependencySpdxId -Name $edge.FromId
        $to = Get-SharpProofDependencySpdxId -Name $edge.ToId
        if (@($actual | Where-Object {
                [string]$_.spdxElementId -eq $from -and
                [string]$_.relatedSpdxElement -eq $to
            }).Count -ne 1) {
            throw "SPDX SBOM dependency is missing: $from -> $to"
        }
    }
}
