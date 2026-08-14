Set-StrictMode -Version Latest

function Test-SharpProofSpdxPackageChecksum {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Package,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha256,

        [Parameter(Mandatory = $true)]
        [string]$Identity
    )

    if ($ExpectedSha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw "Expected SHA256 identity is invalid: $Identity"
    }
    $checksumProperty = $Package.PSObject.Properties['checksums']
    if ($null -eq $checksumProperty -or
        $null -eq $checksumProperty.Value -or
        $checksumProperty.Value -isnot [array]) {
        throw "SPDX checksum array is invalid: $Identity"
    }
    $rows = @($checksumProperty.Value)
    if ($rows.Count -ne 1 -or $null -eq $rows[0]) {
        throw "SPDX checksum array is not exact: $Identity"
    }
    $row = $rows[0]
    $propertyNames = @($row.PSObject.Properties.Name | Sort-Object)
    if ($propertyNames.Count -ne 2 -or
        $propertyNames[0] -cne 'algorithm' -or
        $propertyNames[1] -cne 'checksumValue' -or
        $row.algorithm -isnot [string] -or
        [string]$row.algorithm -cne 'SHA256' -or
        $row.checksumValue -isnot [string] -or
        [string]$row.checksumValue -cne $ExpectedSha256) {
        throw "SPDX checksum row is invalid: $Identity"
    }
}

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
    $licenseNodes = @($metadata.SelectNodes('n:license', $manager))
    if ($null -eq $idNode -or
        $null -eq $versionNode -or
        $licenseNodes.Count -gt 1) {
        throw "Package '$PackagePath' has incomplete nuspec metadata."
    }
    $licenseExpression = $null
    if ($licenseNodes.Count -eq 1) {
        $license = $licenseNodes[0]
        if ($license.Attributes.Count -ne 1 -or
            $license.GetAttribute('type') -ne 'expression' -or
            [string]::IsNullOrWhiteSpace($license.InnerText)) {
            throw "Package '$PackagePath' must declare a license expression."
        }
        $licenseExpression = $license.InnerText
    }

    $publicMetadata = [ordered]@{}
    foreach ($name in @('authors', 'projectUrl', 'description', 'tags')) {
        $nodes = @($metadata.SelectNodes("n:$name", $manager))
        if ($nodes.Count -gt 1) {
            throw "Package '$PackagePath' has duplicate '$name' metadata."
        }
        if ($nodes.Count -eq 0) {
            $publicMetadata[$name] = $null
            continue
        }
        $node = $nodes[0]
        if ($node.Attributes.Count -ne 0 -or
            $node.ChildNodes.Count -ne 1 -or
            $node.ChildNodes[0].NodeType -ne [Xml.XmlNodeType]::Text -or
            [string]::IsNullOrWhiteSpace($node.InnerText)) {
            throw "Package '$PackagePath' has invalid '$name' metadata form."
        }
        $publicMetadata[$name] = $node.InnerText
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
        LicenseExpression = $licenseExpression
        PublicMetadata = [pscustomobject]$publicMetadata
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
        $actualLicense = [string]$model.Nuspec.LicenseExpression
        if (($model.Extension -eq '.nupkg' -and
                $actualLicense -cne
                    [string]$expected[0].licenseExpression) -or
            ($model.Extension -eq '.snupkg' -and
                -not [string]::IsNullOrEmpty($actualLicense) -and
                $actualLicense -cne
                    [string]$expected[0].licenseExpression)) {
            throw "Package '$($model.Nuspec.Id)' has an invalid license expression."
        }
        foreach ($name in @('authors', 'projectUrl', 'description', 'tags')) {
            $actual = [string]$model.Nuspec.PublicMetadata.$name
            $wanted = [string]$expected[0].publicMetadata.$name
            if (($model.Extension -eq '.nupkg' -and
                    $actual -cne $wanted) -or
                ($model.Extension -eq '.snupkg' -and
                    -not [string]::IsNullOrEmpty($actual) -and
                    $actual -cne $wanted)) {
                throw "Package '$($model.Nuspec.Id)' has invalid '$name' metadata."
            }
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

function Get-SharpProofPackageLicenseGraph {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$PackagePaths,

        [Parameter()]
        [string]$ContractPath = (Join-Path `
            (Join-Path $PSScriptRoot '..') `
            'eng/release/package-dependency-contract.json')
    )

    $null = Get-SharpProofPackageDependencyGraph `
        -PackagePaths $PackagePaths `
        -ContractPath $ContractPath
    $contract = Get-Content -LiteralPath $ContractPath -Raw |
        ConvertFrom-Json
    return @($contract.packages | ForEach-Object {
        [pscustomobject][ordered]@{
            PackageId = [string]$_.id
            LicenseExpression = [string]$_.licenseExpression
        }
    } | Sort-Object PackageId)
}

function Get-SharpProofDependencySpdxId {
    param([Parameter(Mandatory = $true)][string]$Name)

    return 'SPDXRef-Package-' + ($Name -replace '[^A-Za-z0-9.-]', '-')
}

function Get-SharpProofSbomLicenseGraph {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$PackageLicenseGraph,

        [Parameter(Mandatory = $true)]
        [string]$PackageVersion,

        [Parameter(Mandatory = $true)]
        [object[]]$ThirdPartyComponents
    )

    $licenses = [Collections.Generic.List[object]]::new()
    foreach ($license in $PackageLicenseGraph) {
        $licenses.Add([pscustomobject][ordered]@{
            Name = [string]$license.PackageId
            Version = $PackageVersion
            LicenseExpression = [string]$license.LicenseExpression
        })
    }
    foreach ($group in @($ThirdPartyComponents | Group-Object {
                [string]$_.id + "`0" + [string]$_.version
            })) {
        $expressions = @($group.Group |
            ForEach-Object { [string]$_.license } |
            Sort-Object -Unique)
        if ($expressions.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace($expressions[0])) {
            throw "Third-party SBOM license authority is invalid: $($group.Name)"
        }
        $licenses.Add([pscustomobject][ordered]@{
            Name = [string]$group.Group[0].id
            Version = [string]$group.Group[0].version
            LicenseExpression = $expressions[0]
        })
    }
    return @($licenses | Sort-Object Name, Version)
}

function Get-SharpProofThirdPartyComponentGraph {
    param(
        [Parameter()]
        [string]$ContractPath = (Join-Path `
            (Join-Path $PSScriptRoot '..') `
            'eng/release/third-party-components.json')
    )

    $contract = Get-Content -LiteralPath $ContractPath -Raw |
        ConvertFrom-Json
    if ($contract.schemaVersion -ne 1 -or
        $null -eq $contract.PSObject.Properties['packages']) {
        throw 'Unsupported third-party component license authority.'
    }
    return @($contract.packages.PSObject.Properties | ForEach-Object {
        $packageId = $_.Name
        @($_.Value) | ForEach-Object {
            [pscustomobject][ordered]@{
                packageId = $packageId
                id = [string]$_.id
                version = [string]$_.version
                license = [string]$_.license
                entries = @(@($_.entries) |
                    ForEach-Object { [string]$_ } |
                    Sort-Object)
            }
        }
    })
}

function Test-SharpProofThirdPartyComponentProjection {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ActualComponents,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$ExpectedComponents
    )

    $propertyNames = @('entries', 'id', 'license', 'packageId', 'version')
    foreach ($component in $ActualComponents) {
        $actualPropertyNames = @($component.PSObject.Properties.Name |
            Sort-Object)
        if (($actualPropertyNames -join '|') -cne
            ($propertyNames -join '|')) {
            throw 'Third-party component inventory has an invalid schema.'
        }
    }
    function ConvertTo-ComponentRecord([object]$Component) {
        return [pscustomobject][ordered]@{
            packageId = [string]$Component.packageId
            id = [string]$Component.id
            version = [string]$Component.version
            license = [string]$Component.license
            entries = @(@($Component.entries) |
                ForEach-Object { [string]$_ } |
                Sort-Object)
        }
    }
    $actual = @($ActualComponents |
        ForEach-Object { ConvertTo-ComponentRecord $_ } |
        Sort-Object packageId, id, version)
    $expected = @($ExpectedComponents |
        ForEach-Object { ConvertTo-ComponentRecord $_ } |
        Sort-Object packageId, id, version)
    if ($actual.Count -ne $expected.Count -or
        ($actual | ConvertTo-Json -Depth 4 -Compress) -cne
            ($expected | ConvertTo-Json -Depth 4 -Compress)) {
        throw 'Third-party component inventory does not match the authenticated catalog projection.'
    }
}

function Test-SharpProofSbomComponentGraph {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$SbomPackages,

        [Parameter(Mandatory = $true)]
        [object[]]$Relationships,

        [Parameter(Mandatory = $true)]
        [object[]]$Components
    )

    $componentIdentities = @($Components |
        ForEach-Object { [string]$_.id + "`0" + [string]$_.version } |
        Sort-Object -Unique)
    $actualComponents = @($SbomPackages | Where-Object {
        $key = [string]$_.name + "`0" + [string]$_.versionInfo
        $componentIdentities -ccontains $key
    })
    if ($actualComponents.Count -ne $componentIdentities.Count) {
        throw 'SPDX SBOM third-party component package graph is not exact.'
    }
    $contains = @($Relationships | Where-Object {
        [string]$_.relationshipType -ceq 'CONTAINS'
    })
    if ($contains.Count -ne $Components.Count) {
        throw 'SPDX SBOM third-party containment graph is not exact.'
    }
    foreach ($component in $Components) {
        $owner = Get-SharpProofDependencySpdxId `
            -Name ([string]$component.packageId)
        $componentId = Get-SharpProofDependencySpdxId `
            -Name (([string]$component.id) + '-' + ([string]$component.version))
        if (@($contains | Where-Object {
                [string]$_.spdxElementId -ceq $owner -and
                [string]$_.relatedSpdxElement -ceq $componentId
            }).Count -ne 1) {
            throw "SPDX SBOM containment is invalid: $($component.packageId)/$($component.id)"
        }
    }
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

function Test-SharpProofSbomTopology {
    param(
        [Parameter(Mandatory = $true)][object[]]$SbomPackages,
        [Parameter(Mandatory = $true)][object[]]$DocumentDescribes,
        [Parameter(Mandatory = $true)][object[]]$Relationships,
        [Parameter(Mandatory = $true)][string[]]$FirstPartyPackageIds,
        [Parameter(Mandatory = $true)][string]$PackageVersion,
        [Parameter(Mandatory = $true)][object[]]$Components,
        [Parameter(Mandatory = $true)][object[]]$DependencyGraph
    )

    function PackageIdentity([string]$Name, [string]$Version) {
        return [pscustomobject][ordered]@{
            name = $Name
            version = $Version
            spdxId = Get-SharpProofDependencySpdxId `
                -Name $(if ($Version -ceq $PackageVersion -and
                    $FirstPartyPackageIds -ccontains $Name) {
                        $Name
                    }
                    else {
                        "$Name-$Version"
                    })
        }
    }
    $expectedPackages = [Collections.Generic.List[object]]::new()
    foreach ($id in @($FirstPartyPackageIds | Sort-Object -Unique)) {
        $expectedPackages.Add((PackageIdentity $id $PackageVersion))
    }
    foreach ($group in @($Components | Group-Object {
                [string]$_.id + "`0" + [string]$_.version
            })) {
        $expectedPackages.Add((PackageIdentity `
            ([string]$group.Group[0].id) `
            ([string]$group.Group[0].version)))
    }
    $expectedPackages = @($expectedPackages | Sort-Object name, version)
    if ($expectedPackages.Count -ne
            @($expectedPackages.spdxId | Sort-Object -Unique).Count) {
        throw 'SPDX SBOM canonical package identities collide.'
    }
    $actualPackages = @($SbomPackages | ForEach-Object {
        [pscustomobject][ordered]@{
            name = [string]$_.name
            version = [string]$_.versionInfo
            spdxId = [string]$_.SPDXID
        }
    } | Sort-Object name, version)
    if ($actualPackages.Count -ne $expectedPackages.Count -or
        ($actualPackages | ConvertTo-Json -Compress) -cne
            ($expectedPackages | ConvertTo-Json -Compress)) {
        throw 'SPDX SBOM package identities are not the exact canonical graph.'
    }

    $expectedDescribes = @($FirstPartyPackageIds |
        ForEach-Object { Get-SharpProofDependencySpdxId -Name $_ } |
        Sort-Object)
    $actualDescribes = @($DocumentDescribes |
        ForEach-Object { [string]$_ } | Sort-Object)
    if ($actualDescribes.Count -ne $expectedDescribes.Count -or
        ($actualDescribes -join '|') -cne ($expectedDescribes -join '|')) {
        throw 'SPDX documentDescribes is not the exact first-party package graph.'
    }

    $expectedRelationships = [Collections.Generic.List[object]]::new()
    foreach ($id in $FirstPartyPackageIds) {
        $expectedRelationships.Add([pscustomobject][ordered]@{
            from = 'SPDXRef-DOCUMENT'
            type = 'DESCRIBES'
            to = Get-SharpProofDependencySpdxId -Name $id
        })
    }
    foreach ($component in $Components) {
        $expectedRelationships.Add([pscustomobject][ordered]@{
            from = Get-SharpProofDependencySpdxId `
                -Name ([string]$component.packageId)
            type = 'CONTAINS'
            to = Get-SharpProofDependencySpdxId `
                -Name (([string]$component.id) + '-' +
                    ([string]$component.version))
        })
    }
    foreach ($dependency in $DependencyGraph) {
        $expectedRelationships.Add([pscustomobject][ordered]@{
            from = Get-SharpProofDependencySpdxId `
                -Name ([string]$dependency.FromId)
            type = 'DEPENDS_ON'
            to = Get-SharpProofDependencySpdxId `
                -Name ([string]$dependency.ToId)
        })
    }
    $expectedRelationships = @($expectedRelationships |
        Sort-Object from, type, to)
    $expectedRelationshipKeys = @($expectedRelationships |
        ForEach-Object { $_.from + "`0" + $_.type + "`0" + $_.to } |
        Sort-Object -Unique)
    if ($expectedRelationshipKeys.Count -ne $expectedRelationships.Count) {
        throw 'Authenticated SPDX relationship inputs contain duplicates.'
    }
    $actualRelationships = @($Relationships | ForEach-Object {
        if ((@($_.PSObject.Properties.Name | Sort-Object) -join '|') -cne
            'relatedSpdxElement|relationshipType|spdxElementId') {
            throw 'SPDX relationship rows have an invalid schema.'
        }
        [pscustomobject][ordered]@{
            from = [string]$_.spdxElementId
            type = [string]$_.relationshipType
            to = [string]$_.relatedSpdxElement
        }
    } | Sort-Object from, type, to)
    if ($actualRelationships.Count -ne $expectedRelationships.Count -or
        ($actualRelationships | ConvertTo-Json -Compress) -cne
            ($expectedRelationships | ConvertTo-Json -Compress)) {
        throw 'SPDX relationships are not the exact canonical topology.'
    }
}

function Test-SharpProofSbomLicenseGraph {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$SbomPackages,

        [Parameter(Mandatory = $true)]
        [object[]]$LicenseGraph
    )

    if ($SbomPackages.Count -ne $LicenseGraph.Count) {
        throw 'SPDX SBOM license package graph is not exact.'
    }
    foreach ($license in $LicenseGraph) {
        $name = if ($null -ne $license.PSObject.Properties['Name']) {
            [string]$license.Name
        }
        else {
            [string]$license.PackageId
        }
        $version = if ($null -ne $license.PSObject.Properties['Version']) {
            [string]$license.Version
        }
        else {
            $null
        }
        $matches = @($SbomPackages | Where-Object {
            [string]$_.name -ceq $name -and
            ($null -eq $version -or [string]$_.versionInfo -ceq $version)
        })
        if ($matches.Count -ne 1 -or
            $null -eq $matches[0].PSObject.Properties['licenseDeclared'] -or
            $null -eq $matches[0].PSObject.Properties['licenseConcluded'] -or
            $null -eq $matches[0].PSObject.Properties['downloadLocation'] -or
            $null -eq $matches[0].PSObject.Properties['filesAnalyzed']) {
            throw "SPDX SBOM license is invalid: $name"
        }
        if (
            [string]$matches[0].licenseDeclared -cne
                [string]$license.LicenseExpression -or
            [string]$matches[0].licenseConcluded -cne
                [string]$license.LicenseExpression -or
            [string]$matches[0].downloadLocation -cne 'NOASSERTION' -or
            $matches[0].filesAnalyzed -isnot [bool] -or
            [bool]$matches[0].filesAnalyzed -or
            @($matches[0].PSObject.Properties | Where-Object {
                    $_.Name -like 'license*' -and
                    $_.Name -cnotin @('licenseDeclared', 'licenseConcluded')
                }).Count -ne 0) {
            throw "SPDX SBOM license is invalid: $name"
        }
    }
}
