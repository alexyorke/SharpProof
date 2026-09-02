function Resolve-SharpProofPublicationPlanOutput {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($parent) -or
        -not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw 'PlanOutputPath must have an existing parent directory.'
    }
    if (Test-Path -LiteralPath $fullPath) {
        $resolved = (& readlink -f -- $fullPath).Trim()
    }
    else {
        $resolvedParent = (& readlink -f -- $parent).Trim()
        $resolved = Join-Path $resolvedParent ([IO.Path]::GetFileName($fullPath))
    }
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolved)) {
        throw 'PlanOutputPath could not be canonically resolved.'
    }
    return [IO.Path]::GetFullPath($resolved)
}

function Get-SharpProofPublicationPlanFileIdentity {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (& readlink -f -- $Path).Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($resolved) -or
        -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Publication input is not a regular file: '$Path'."
    }
    $deviceInode = (& stat -Lc '%d:%i' -- $resolved).Trim()
    if ($LASTEXITCODE -ne 0 -or $deviceInode -notmatch '^[0-9]+:[0-9]+$') {
        throw "Publication input identity is unavailable: '$Path'."
    }
    $file = Get-Item -LiteralPath $resolved
    return [pscustomobject][ordered]@{
        path = [IO.Path]::GetFullPath($resolved)
        fileIdentity = $deviceInode
        bytes = [int64]$file.Length
    }
}

function New-SharpProofPublicationInputSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$PackageSource,
        [AllowNull()][string]$FixtureDirectory
    )

    $packageRoot = (& readlink -f -- $PackageSource).Trim()
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
        throw 'PackageSource could not be canonically resolved.'
    }
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File) {
        if ($file.Extension -in @('.nupkg', '.snupkg') -or
            $file.Name -in @(
                'SharpProof.release.json','SharpProof.spdx.json')) {
            $files.Add($file)
        }
    }
    $fixtureRoot = $null
    if (-not [string]::IsNullOrWhiteSpace($FixtureDirectory)) {
        $fixtureRoot = (& readlink -f -- $FixtureDirectory).Trim()
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath $fixtureRoot -PathType Container)) {
            throw 'RemotePackageDirectory could not be canonically resolved.'
        }
        foreach ($file in Get-ChildItem -LiteralPath $fixtureRoot -File -Recurse) {
            $files.Add($file)
        }
    }
    $entries = @($files | Sort-Object FullName | ForEach-Object {
        Get-SharpProofPublicationPlanFileIdentity -Path $_.FullName
    })
    if ($entries.Count -eq 0 -or
        @($entries.path | Sort-Object -Unique).Count -ne $entries.Count -or
        @($entries.fileIdentity | Sort-Object -Unique).Count -ne $entries.Count) {
        throw 'Publication inputs are empty, duplicated, or aliased.'
    }
    return [pscustomobject][ordered]@{
        packageSource = [IO.Path]::GetFullPath($packageRoot)
        fixtureDirectory = $fixtureRoot
        entries = $entries
    }
}

function Assert-SharpProofPublicationPlanTopology {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][object]$InputSnapshot
    )

    $reserved = @('SharpProof.release.json','SharpProof.spdx.json')
    if ($reserved -ccontains [IO.Path]::GetFileName($OutputPath)) {
        throw 'PlanOutputPath uses a reserved release-evidence filename.'
    }
    $outputIdentity = if (Test-Path -LiteralPath $OutputPath) {
        Get-SharpProofPublicationPlanFileIdentity -Path $OutputPath
    }
    else { $null }
    foreach ($entry in @($InputSnapshot.entries)) {
        if ([string]$entry.path -ceq $OutputPath -or
            ($null -ne $outputIdentity -and
             [string]$entry.fileIdentity -ceq
                [string]$outputIdentity.fileIdentity)) {
            throw 'PlanOutputPath aliases a certified publication input.'
        }
    }
}

function Test-SharpProofPublicationInputSnapshot {
    param([Parameter(Mandatory = $true)][object]$Snapshot)

    $current = New-SharpProofPublicationInputSnapshot `
        -PackageSource ([string]$Snapshot.packageSource) `
        -FixtureDirectory ([string]$Snapshot.fixtureDirectory)
    $expectedJson = @($Snapshot.entries | Sort-Object path) |
        ConvertTo-Json -Compress
    $currentJson = @($current.entries | Sort-Object path) |
        ConvertTo-Json -Compress
    if ($currentJson -cne $expectedJson) {
        throw 'Certified publication inputs changed while writing the plan.'
    }
}

function Write-SharpProofPublicationPlanAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$Json,
        [Parameter(Mandatory = $true)][object]$InputSnapshot,
        [AllowNull()][scriptblock]$BeforePublish,
        [AllowNull()][scriptblock]$AfterPublish
    )

    $directory = [IO.Path]::GetDirectoryName($OutputPath)
    $nonce = [Guid]::NewGuid().ToString('N')
    $temporary = Join-Path $directory ".sharpproof-plan-$nonce.tmp"
    $backup = Join-Path $directory ".sharpproof-plan-$nonce.bak"
    $hadOutput = Test-Path -LiteralPath $OutputPath -PathType Leaf
    $published = $false
    try {
        if ($hadOutput) {
            [IO.File]::Copy($OutputPath, $backup, $false)
        }
        [IO.File]::WriteAllText(
            $temporary,
            $Json,
            [Text.UTF8Encoding]::new($false))
        if ($null -ne $BeforePublish) { & $BeforePublish }
        [IO.File]::Move($temporary, $OutputPath, $true)
        $published = $true
        if ($null -ne $AfterPublish) { & $AfterPublish }
        Test-SharpProofPublicationInputSnapshot -Snapshot $InputSnapshot
    }
    catch {
        if ($published) {
            if ($hadOutput -and (Test-Path -LiteralPath $backup -PathType Leaf)) {
                [IO.File]::Move($backup, $OutputPath, $true)
            }
            elseif (Test-Path -LiteralPath $OutputPath -PathType Leaf) {
                Remove-Item -LiteralPath $OutputPath -Force
            }
        }
        throw
    }
    finally {
        foreach ($owned in @($temporary, $backup)) {
            if (Test-Path -LiteralPath $owned -PathType Leaf) {
                Remove-Item -LiteralPath $owned -Force
            }
        }
    }
}
