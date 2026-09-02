Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

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
