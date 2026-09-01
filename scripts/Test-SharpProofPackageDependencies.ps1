Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

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

function Get-SharpProofNuGetPurl {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ([string]::IsNullOrWhiteSpace($Name) -or
        [string]::IsNullOrWhiteSpace($Version)) {
        throw 'NuGet purl identity must contain a package name and version.'
    }
    return 'pkg:nuget/' + [Uri]::EscapeDataString($Name) + '@' +
        [Uri]::EscapeDataString($Version)
}

function Test-SharpProofSbomPackageUrls {
    param([Parameter(Mandatory = $true)][object[]]$SbomPackages)

    foreach ($package in $SbomPackages) {
        $identity = ([string]$package.name) + '@' +
            ([string]$package.versionInfo)
        $property = $package.PSObject.Properties['externalRefs']
        if ($null -eq $property -or
            $null -eq $property.Value -or
            $property.Value -isnot [array]) {
            throw "SPDX externalRefs must be an array: $identity"
        }
        $rows = @($property.Value)
        if ($rows.Count -ne 1 -or $null -eq $rows[0]) {
            throw "SPDX externalRefs must contain exactly one purl: $identity"
        }
        $row = $rows[0]
        $names = @($row.PSObject.Properties.Name)
        [Array]::Sort($names, [StringComparer]::Ordinal)
        if (($names -join "`n") -cne
                "referenceCategory`nreferenceLocator`nreferenceType" -or
            $row.referenceCategory -isnot [string] -or
            [string]$row.referenceCategory -cne 'PACKAGE-MANAGER' -or
            $row.referenceType -isnot [string] -or
            [string]$row.referenceType -cne 'purl' -or
            $row.referenceLocator -isnot [string] -or
            [string]$row.referenceLocator -cne
                (Get-SharpProofNuGetPurl `
                    -Name ([string]$package.name) `
                    -Version ([string]$package.versionInfo))) {
            throw "SPDX purl is not the exact package identity: $identity"
        }
    }
}

function Get-SharpProofSbomReleaseIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$RepositoryCommit
    )

    if ($RepositoryCommit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'SBOM repository commit must be canonical lowercase SHA-1.'
    }
    $commitTimestamp = (
        & git -C $RepositoryRoot show `
            -s `
            --format=%cI `
            $RepositoryCommit
    ).Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($commitTimestamp)) {
        throw "Could not resolve timestamp for commit $RepositoryCommit."
    }
    $created = [DateTimeOffset]::Parse(
        $commitTimestamp,
        [Globalization.CultureInfo]::InvariantCulture).UtcDateTime.ToString(
            'yyyy-MM-ddTHH:mm:ssZ',
            [Globalization.CultureInfo]::InvariantCulture)

    return [pscustomobject][ordered]@{
        Name = "SharpProof-$Version"
        DocumentNamespace = (
            'https://github.com/alexyorke/SharpProof/sbom/' +
            "$Version/$RepositoryCommit")
        Created = $created
        Creators = @('Tool: SharpProof release evidence')
        Comment = 'Timestamp is derived from the source commit for reproducibility.'
    }
}

function Test-SharpProofSbomReleaseIdentity {
    param(
        [Parameter(Mandatory = $true)]$Sbom,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$RepositoryCommit
    )

    $expected = Get-SharpProofSbomReleaseIdentity `
        -RepositoryRoot $RepositoryRoot `
        -Version $Version `
        -RepositoryCommit $RepositoryCommit
    if ($null -eq $Sbom.PSObject.Properties['name'] -or
        $Sbom.name -isnot [string] -or
        [string]$Sbom.name -cne [string]$expected.Name -or
        $null -eq $Sbom.PSObject.Properties['documentNamespace'] -or
        $Sbom.documentNamespace -isnot [string] -or
        [string]$Sbom.documentNamespace -cne
            [string]$expected.DocumentNamespace) {
        throw 'SPDX SBOM release name or document namespace is not exact.'
    }
    if ($null -eq $Sbom.PSObject.Properties['creationInfo'] -or
        $null -eq $Sbom.creationInfo -or
        $Sbom.creationInfo -is [Array] -or
        $Sbom.creationInfo -isnot [psobject]) {
        throw 'SPDX SBOM creationInfo is not an object.'
    }
    $creationInfo = $Sbom.creationInfo
    $properties = @($creationInfo.PSObject.Properties.Name)
    [Array]::Sort($properties, [StringComparer]::Ordinal)
    if (($properties -join "`n") -cne "comment`ncreated`ncreators") {
        throw 'SPDX SBOM creationInfo does not have the exact schema.'
    }
    if ($creationInfo.creators -isnot [Array]) {
        throw 'SPDX SBOM creators must be an array.'
    }
    $creators = @($creationInfo.creators)
    if ($creators.Count -ne 1 -or
        $creators[0] -isnot [string] -or
        [string]$creators[0] -cne [string]$expected.Creators[0] -or
        $creationInfo.created -isnot [string] -or
        [string]$creationInfo.created -cne [string]$expected.Created -or
        $creationInfo.comment -isnot [string] -or
        [string]$creationInfo.comment -cne [string]$expected.Comment) {
        throw 'SPDX SBOM creation identity is not exact.'
    }
}

function Get-SharpProofNuspecDependencyModel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath
    )

    $metadata = Get-SharpProofNuspecMetadata -Path $PackagePath
    $manager = [Xml.XmlNamespaceManager]::new(
        $metadata.OwnerDocument.NameTable)
    $manager.AddNamespace(
        'n', $metadata.OwnerDocument.DocumentElement.NamespaceURI)
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
    Assert-SharpProofCanonicalMatch `
        -Actual $actual -Expected $expected -Depth 4 `
        -Message 'Third-party component inventory does not match the authenticated catalog projection.'
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

    Test-SharpProofSbomPackageUrls -SbomPackages $SbomPackages

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
    Assert-SharpProofCanonicalMatch `
        -Actual $actualPackages -Expected $expectedPackages -Depth 2 `
        -Message 'SPDX SBOM package identities are not the exact canonical graph.'

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
    Assert-SharpProofCanonicalMatch `
        -Actual $actualRelationships -Expected $expectedRelationships `
        -Depth 2 `
        -Message 'SPDX relationships are not the exact canonical topology.'
}

function Test-SharpProofSbomArtifactScope {
    param(
        [Parameter(Mandatory = $true)][object[]]$Artifacts,
        [Parameter(Mandatory = $true)][object[]]$SbomPackages,
        [Parameter(Mandatory = $true)][object[]]$DocumentDescribes,
        [Parameter(Mandatory = $true)][string[]]$FirstPartyPackageIds,
        [Parameter(Mandatory = $true)][string]$PackageVersion
    )

    $expectedIds = @($FirstPartyPackageIds | Sort-Object -Unique)
    if ($expectedIds.Count -ne $FirstPartyPackageIds.Count) {
        throw 'The SBOM first-party package authority contains duplicates.'
    }
    $mainArtifacts = @($Artifacts | Where-Object {
        [string]$_.kind -ceq 'package'
    })
    $symbolArtifacts = @($Artifacts | Where-Object {
        [string]$_.kind -ceq 'symbols'
    })
    foreach ($set in @(
            @{ Name = 'main'; Rows = $mainArtifacts; Extension = '.nupkg' },
            @{ Name = 'symbol'; Rows = $symbolArtifacts; Extension = '.snupkg' })) {
        $ids = @($set.Rows | ForEach-Object { [string]$_.packageId } |
            Sort-Object)
        if ($set.Rows.Count -ne $expectedIds.Count -or
            ($ids -join "`0") -cne ($expectedIds -join "`0")) {
            throw "The release manifest does not contain the exact $($set.Name) package set."
        }
        foreach ($row in $set.Rows) {
            $expectedName = ([string]$row.packageId) + '.' +
                $PackageVersion + [string]$set.Extension
            if ([string]$row.fileName -cne $expectedName -or
                [string]$row.sha256 -notmatch '^[0-9a-f]{64}$') {
                throw "The release manifest has an invalid $($set.Name) package identity."
            }
        }
    }

    foreach ($id in $expectedIds) {
        $main = @($mainArtifacts | Where-Object {
            [string]$_.packageId -ceq $id
        })
        $symbol = @($symbolArtifacts | Where-Object {
            [string]$_.packageId -ceq $id
        })
        $sbomRows = @($SbomPackages | Where-Object {
            [string]$_.name -ceq $id -and
            [string]$_.versionInfo -ceq $PackageVersion
        })
        $spdxId = Get-SharpProofDependencySpdxId -Name $id
        if ($main.Count -ne 1 -or $symbol.Count -ne 1 -or
            $sbomRows.Count -ne 1 -or
            @($DocumentDescribes | Where-Object {
                [string]$_ -ceq $spdxId
            }).Count -ne 1) {
            throw "The SBOM artifact scope is invalid for '$id'."
        }
        Test-SharpProofSpdxPackageChecksum `
            -Package $sbomRows[0] `
            -ExpectedSha256 ([string]$main[0].sha256) `
            -Identity "$id main package"
    }
}

function Test-SharpProofSbomAttestationWorkflow {
    param([Parameter(Mandatory = $true)][string]$Workflow)

    $lines = @($Workflow.Replace("`r`n", "`n").Replace("`r", "`n").Split("`n"))
    $starts = @(for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index].Trim() -ceq '- name: Attest package SBOM') {
            $index
        }
    })
    if ($starts.Count -ne 1) {
        throw 'The release workflow must contain one SBOM attestation step.'
    }
    $end = $lines.Count
    for ($index = $starts[0] + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^      - name:') {
            $end = $index
            break
        }
    }
    $block = @($lines[$starts[0]..($end - 1)])
    $subjects = @($block | Where-Object {
        $_.TrimStart().StartsWith('subject-path:', [StringComparison]::Ordinal)
    })
    $sbomPaths = @($block | Where-Object {
        $_.TrimStart().StartsWith('sbom-path:', [StringComparison]::Ordinal)
    })
    if ($subjects.Count -ne 1 -or
        $subjects[0].Trim() -cne 'subject-path: nupkgs/*.nupkg' -or
        $sbomPaths.Count -ne 1 -or
        $sbomPaths[0].Trim() -cne
            'sbom-path: nupkgs/SharpProof.spdx.json') {
        throw 'The SBOM attestation must cover only exact main NuGet packages.'
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
