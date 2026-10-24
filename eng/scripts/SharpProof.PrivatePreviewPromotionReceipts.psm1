Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofPrivatePreviewPromotionReceiptContract {
    # This is an inventory for reviewers, never an acceptance-receipt generator.
    return [ordered]@{
        schemaVersion  = 1
        editors        = @('visual-studio', 'visual-studio-code', 'rider')
        scenarios      = @('project-load', 'portable-warning-present', 'portable-warning-cleared',
            'result-type-diagnostic', 'result-type-preview-apply', 'result-fix-all',
            'companion-diagnostic-action', 'companion-refactoring', 'companion-shape-review',
            'companion-reviewed-clauses', 'missing-member-repair', 'negative-ambiguous-overloads',
            'negative-partial-overloads', 'negative-nested-inaccessible', 'negative-nullable-constraints',
            'negative-source-generated', 'optional-package-removal', 'reopen-candidate-reload',
            'unintended-diff-review', 'strict-claims-review', 'strict-assumptions-vacuity-review',
            'regression-fails-before', 'regression-passes-after', 'fresh-strict-proof-passes', 'package-installation', 'undo-source-restoration')
        accessChecks   = @('visibility', 'direct-users', 'teams', 'inheritance', 'actions-repositories', 'anonymous-access', 'non-owner-access')
        accessSubjects = @('repository:alexyorke/SharpProof-preview', 'nuget:SharpProof', 'nuget:SharpProof.Attributes',
            'nuget:SharpProof.CodeFixes', 'nuget:SharpProof.Templates', 'nuget:SharpProof.Tool', 'nuget:SharpProof.Verifier',
            'container:ghcr.io/alexyorke/sharpproof-preview', 'symbols:alexyorke/SharpProof-preview', 'evidence:alexyorke/SharpProof-preview')
    }
}

function Assert-ReceiptFields($Value, [string[]]$Required, [string[]]$Optional = @()) {
    if ($Value -isnot [Collections.IDictionary]) { throw 'Receipt object is missing or has the wrong JSON type.' }
    foreach ($name in $Required) {
        if (-not $Value.Contains($name) -or $null -eq $Value[$name]) { throw "Required receipt field is missing: $name" }
    }
    foreach ($key in $Value.Keys) {
        if ($key -cnotin $Required -and $key -cnotin $Optional) { throw "Unexpected receipt field: $key" }
    }
}

function Assert-ReceiptText($Value) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value) -or $Value -cne $Value.Trim() -or
        $Value.Length -gt 1024 -or $Value -match '[\x00-\x1f\x7f]' -or
        $Value -match '(?i)\b(pending|unavailable|unknown|unexecuted|placeholder|todo|tbd)\b') {
        throw 'Receipt text must contain an actual bounded observation, not a placeholder.'
    }
}

function Assert-ReceiptHash($Value) {
    if ($Value -isnot [string] -or $Value -cnotmatch '\A[a-f0-9]{64}\z') { throw 'Receipt requires an exact lowercase SHA256.' }
}

function Assert-ReceiptTrue($Value) {
    if ($Value -isnot [bool] -or -not $Value) { throw 'A required receipt observation was not actually performed and passed.' }
}

function Assert-ReceiptTimestamp($Value) {
    if ($Value -is [datetime]) {
        if ($Value.Kind -ne [DateTimeKind]::Utc) { throw 'Receipt observation time must be UTC.' }
        $instant = [DateTimeOffset]$Value
    }
    else {
        if ($Value -isnot [string] -or $Value -cnotmatch '\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?Z\z') { throw 'Receipt observation time must be an exact UTC timestamp.' }
        $instant = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$instant)) { throw 'Receipt observation time is invalid.' }
    }
    if ($instant -gt [DateTimeOffset]::UtcNow.AddMinutes(5) -or $instant.Year -lt 2026) { throw 'Receipt observation time is outside its valid range.' }
}

function Assert-ReceiptJsonKeys([Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'Duplicate or case-ambiguous receipt JSON field.' }
            Assert-ReceiptJsonKeys $property.Value
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-ReceiptJsonKeys $item }
    }
}

function Resolve-ReceiptFile([string]$Root, [string]$Relative, [long]$MaximumBytes = 67108864) {
    if ($Relative -cnotmatch '\A[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\z' -or @($Relative.Split('/') | Where-Object { $_ -cin @('.', '..') }).Count -ne 0) { throw 'Receipt evidence path is unsafe.' }
    $path = $Root
    foreach ($segment in $Relative.Split('/')) {
        $path = Join-Path $path $segment
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Receipt evidence must not contain filesystem links.' }
    }
    if ($item.PSIsContainer -or $item.Length -le 0 -or $item.Length -gt $MaximumBytes) { throw 'Receipt evidence must be a nonempty bounded regular file.' }
    return $path
}

function Read-ReceiptJson([string]$Path, [ref]$Sha256) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt 4MB) { throw 'Receipt JSON exceeds its bound or is empty.' }
        $bytes = [byte[]]::new([int]$stream.Length)
        $stream.ReadExactly($bytes, 0, $bytes.Length)
        if ($stream.ReadByte() -ne -1) { throw 'Receipt JSON changed while reading.' }
    }
    finally { $stream.Dispose() }
    $document = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($bytes), [Text.Json.JsonDocumentOptions]@{ MaxDepth = 32 })
    try { Assert-ReceiptJsonKeys $document.RootElement }
    finally { $document.Dispose() }
    if ($null -ne $Sha256) { $Sha256.Value = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    return , ($text | ConvertFrom-Json -AsHashtable -Depth 32 -NoEnumerate)
}

function Assert-ReceiptEvidence($Items, [string]$Root) {
    if ($Items -isnot [array] -or $Items.Count -lt 1 -or $Items.Count -gt 32) { throw 'Each performed observation requires bounded private evidence attachments.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $Items) {
        Assert-ReceiptFields $item @('path', 'sha256')
        Assert-ReceiptHash $item.sha256
        if ($item.path -isnot [string] -or -not $item.path.StartsWith('evidence/', [StringComparison]::Ordinal) -or -not $seen.Add($item.path)) { throw 'Evidence attachment path must be unique and under evidence/.' }
        $path = Resolve-ReceiptFile $Root $item.path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $item.sha256) { throw 'Receipt evidence attachment hash differs from the retained bytes.' }
    }
}

function Assert-ReceiptObservation($Value, [string]$Root, [string[]]$AdditionalFields = @()) {
    Assert-ReceiptFields $Value (@('id', 'status', 'performed', 'method', 'observedAtUtc', 'evidence') + $AdditionalFields) @('reason', 'observedActionAvailable', 'principal', 'principalVerified', 'httpStatus')
    Assert-ReceiptText $Value.id
    Assert-ReceiptText $Value.method
    Assert-ReceiptTrue $Value.performed
    Assert-ReceiptTimestamp $Value.observedAtUtc
    Assert-ReceiptEvidence $Value.evidence $Root
}

function Assert-ReceiptPackages($Packages, $Expected) {
    if ($Packages -isnot [array] -or $Packages.Count -ne 6) { throw 'Receipt must bind all six exact candidate packages.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in $Packages) {
        Assert-ReceiptFields $package @('id', 'version', 'sha256')
        if ($package.id -isnot [string] -or -not $seen.Add($package.id) -or -not $Expected.Contains($package.id)) { throw 'Receipt package identity is unexpected or duplicated.' }
        Assert-ReceiptHash $package.sha256
        if ($package.version -cne $Expected[$package.id].version -or $package.sha256 -cne $Expected[$package.id].sha256) { throw 'Receipt package version or archive hash differs from the frozen candidate.' }
    }
}

function Assert-ReceiptBinding($Receipt, [string]$Kind, $Manifest, [string]$ManifestHash, $Packages,
    [long]$ArtifactId, [string]$ArtifactDigest, [long]$RunId, [string]$PayloadName) {
    Assert-ReceiptFields $Receipt @('schemaVersion', 'kind', 'status', 'sourceRepository', 'releaseRepository', 'sourceCommit', 'workflowCommit',
        'candidateManifestSha256', 'candidateArtifactId', 'candidateArtifactDigest', 'candidateRunId', 'consumerImage', 'packageVersion', 'packageFeed', 'reviewedBy', 'reviewedAtUtc', 'packages', $PayloadName)
    if ($Receipt.schemaVersion -isnot [long] -and $Receipt.schemaVersion -isnot [int]) { throw 'Receipt schema version must be an integer.' }
    if ($Receipt.schemaVersion -ne 1 -or $Receipt.kind -cne $Kind -or $Receipt.status -cne 'passed' -or $Receipt.reviewedBy -cne 'alexyorke' -or
        $Receipt.sourceCommit -isnot [string] -or $Receipt.workflowCommit -isnot [string] -or
        $Receipt.candidateManifestSha256 -isnot [string] -or $Receipt.candidateArtifactDigest -isnot [string] -or
        $Receipt.sourceRepository -cne 'alexyorke/SharpProof' -or $Receipt.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
        $Receipt.sourceCommit -cne $Manifest.sourceCommit -or $Receipt.workflowCommit -cne $Manifest.workflowCommit -or
        $Receipt.candidateManifestSha256 -cne $ManifestHash -or $Receipt.candidateArtifactDigest -cne $ArtifactDigest -or
        $Receipt.consumerImage -cne $Manifest.consumerImage -or $Receipt.packageVersion -cne $Manifest.packageVersion -or
        $Receipt.packageFeed -cne 'https://nuget.pkg.github.com/alexyorke/index.json') { throw 'Receipt status or exact source, workflow, candidate, image, or feed binding differs.' }
    foreach ($field in @('candidateArtifactId', 'candidateRunId')) {
        if ($Receipt[$field] -isnot [long] -and $Receipt[$field] -isnot [int]) { throw 'Receipt private artifact and run identities must be integers.' }
    }
    if ($Receipt.candidateArtifactId -ne $ArtifactId -or $Receipt.candidateRunId -ne $RunId) { throw 'Receipt private artifact or run identity differs.' }
    Assert-ReceiptTimestamp $Receipt.reviewedAtUtc
    Assert-ReceiptPackages $Receipt.packages $Packages
}

function Assert-ReceiptEditorSessions($Sessions, [string]$Root, $Contract) {
    if ($Sessions -isnot [array] -or $Sessions.Count -ne 3) { throw 'All three interactive editor sessions are required.' }
    $editors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $fixtureHash = $null
    $globalHash = $null
    foreach ($session in $Sessions) {
        Assert-ReceiptFields $session @('editor', 'status', 'interactive', 'os', 'windowsEdition', 'windowsBuild', 'processArchitecture',
            'volumeFileSystem', 'volumeKind', 'channel', 'editorVersion', 'compilerVersion', 'extensions', 'selectedSdkVersion', 'installedSdkVersions',
            'runtimeVersions', 'freshWorkspace', 'candidateRestoreVerified', 'analyzersEnabled', 'analyzerLoadClean', 'fixture', 'globalJson',
            'candidateAuthentication', 'scenarios')
        if ($session.editor -cnotin $Contract.editors -or -not $editors.Add($session.editor) -or $session.status -cne 'passed' -or
            $session.os -cne 'windows' -or $session.processArchitecture -cne 'x64' -or $session.channel -cne 'stable' -or
            $session.volumeFileSystem -cne 'NTFS' -or $session.volumeKind -cne 'fixed-local') { throw 'A required stable Windows x64 interactive editor host is missing or incompatible.' }
        foreach ($field in @('interactive', 'freshWorkspace', 'candidateRestoreVerified', 'analyzersEnabled', 'analyzerLoadClean')) { Assert-ReceiptTrue $session[$field] }
        foreach ($field in @('windowsEdition', 'windowsBuild', 'editorVersion', 'compilerVersion', 'selectedSdkVersion')) { Assert-ReceiptText $session[$field] }
        foreach ($field in @('windowsBuild', 'editorVersion', 'compilerVersion', 'selectedSdkVersion')) {
            if ($session[$field] -cnotmatch '\A[0-9]+(?:\.[0-9]+){1,4}(?:[-+][0-9A-Za-z.-]+)?\z') { throw 'Editor, compiler, Windows, and SDK versions must be exact versions.' }
        }
        foreach ($field in @('installedSdkVersions', 'runtimeVersions')) {
            if ($session[$field] -isnot [array] -or $session[$field].Count -lt 1) { throw 'Installed SDK and runtime versions must be recorded.' }
            foreach ($version in $session[$field]) {
                if ($version -isnot [string] -or $version -cnotmatch '\A[0-9]+(?:\.[0-9]+){2}(?:[-+][0-9A-Za-z.-]+)?\z') { throw 'Installed SDK/runtime inventory contains an inexact version.' }
            }
        }
        if ($session.extensions -isnot [array] -or $session.extensions.Count -lt 1) { throw 'Exact C# extension or built-in plugin versions are required.' }
        $extensions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($extension in $session.extensions) {
            Assert-ReceiptFields $extension @('id', 'version', 'role')
            Assert-ReceiptText $extension.id
            if (-not $extensions.Add($extension.id) -or $extension.role -cne 'csharp' -or $extension.version -isnot [string] -or $extension.version -cnotmatch '\A[0-9]+(?:\.[0-9]+){1,4}(?:[-+][0-9A-Za-z.-]+)?\z') { throw 'C# extension/plugin identity or exact version is invalid.' }
        }
        if ($session.editor -ceq 'visual-studio-code' -and -not $extensions.Contains('ms-dotnettools.csharp')) { throw 'VS Code requires the Microsoft C# extension.' }
        Assert-ReceiptEvidence @($session.fixture) $Root
        Assert-ReceiptEvidence @($session.globalJson) $Root
        if ($null -eq $fixtureHash) { $fixtureHash = $session.fixture.sha256; $globalHash = $session.globalJson.sha256 }
        if ($session.fixture.sha256 -cne $fixtureHash -or $session.globalJson.sha256 -cne $globalHash) { throw 'Editor sessions must start from clean copies of the same pinned fixture.' }
        $globalBytesHash = $null
        $global = Read-ReceiptJson (Resolve-ReceiptFile $Root $session.globalJson.path 4MB) ([ref]$globalBytesHash)
        if ($globalBytesHash -cne $session.globalJson.sha256) { throw 'Pinned fixture JSON changed from the recorded attachment bytes.' }
        Assert-ReceiptFields $global @('sdk')
        Assert-ReceiptFields $global.sdk @('version', 'rollForward')
        if ($global.sdk.version -isnot [string] -or $global.sdk.version -cnotmatch '\A10\.[0-9]+\.[0-9]+\z' -or
            $global.sdk.rollForward -cne 'disable' -or $session.selectedSdkVersion -cne $global.sdk.version -or
            $session.selectedSdkVersion -cnotin $session.installedSdkVersions) { throw 'Each editor must use the exact .NET 10 SDK pinned by the fixture.' }
        Assert-ReceiptObservation $session.candidateAuthentication $Root
        if ($session.candidateAuthentication.id -cne 'candidate-authentication' -or $session.candidateAuthentication.status -cne 'passed' -or
            $session.candidateAuthentication.method -cne 'private-actions-artifact') { throw 'The actual candidate authentication observation is missing.' }
        if ($session.scenarios -isnot [array] -or $session.scenarios.Count -ne $Contract.scenarios.Count) { throw 'Every required interactive editor scenario must be recorded.' }
        $scenarios = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($scenario in $session.scenarios) {
            Assert-ReceiptObservation $scenario $Root
            if ($scenario.id -cnotin $Contract.scenarios -or -not $scenarios.Add($scenario.id)) { throw 'Editor scenario identity is unexpected or duplicated.' }
            if ($scenario.id -ceq 'companion-refactoring' -and $scenario.status -ceq 'not-applicable') {
                if (-not $scenario.Contains('observedActionAvailable') -or $scenario.observedActionAvailable -isnot [bool] -or $scenario.observedActionAvailable -or
                    -not $scenario.Contains('reason') -or $scenario.method -cne 'interactive-ui') { throw 'Refactoring applicability must be actually inspected in the editor UI.' }
                Assert-ReceiptText $scenario.reason
            }
            elseif ($scenario.status -cne 'passed' -or $scenario.method -cnotin @('interactive-ui', 'canonical-container', 'native-consumer-windows-x64')) { throw 'Every required editor scenario must actually pass; pending, blocked, or unavailable is not acceptance.' }
            if ($scenario.id -cin @('strict-claims-review', 'strict-assumptions-vacuity-review', 'regression-fails-before', 'regression-passes-after', 'fresh-strict-proof-passes') -and
                $scenario.method -cnotin @('canonical-container', 'native-consumer-windows-x64')) { throw 'Strict proof and regression scenarios require actual installed-consumer build/test evidence.' }
            if ($scenario.id -cin @('project-load', 'portable-warning-present', 'portable-warning-cleared', 'result-type-diagnostic', 'result-type-preview-apply',
                    'result-fix-all', 'companion-diagnostic-action', 'companion-refactoring', 'companion-shape-review', 'companion-reviewed-clauses', 'missing-member-repair',
                    'negative-ambiguous-overloads', 'negative-partial-overloads', 'negative-nested-inaccessible', 'negative-nullable-constraints', 'negative-source-generated',
                    'optional-package-removal', 'reopen-candidate-reload', 'unintended-diff-review', 'package-installation', 'undo-source-restoration') -and $scenario.method -cne 'interactive-ui') { throw 'Interactive editor scenarios cannot be substituted by implementation or command-line tests.' }
        }
    }
}

function Assert-ReceiptAccessSubjects($Subjects, [string]$Root, $Contract) {
    if ($Subjects -isnot [array] -or $Subjects.Count -ne $Contract.accessSubjects.Count) { throw 'Owner-only access must be checked for every repository, package, image, symbol, and evidence subject.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($subject in $Subjects) {
        Assert-ReceiptFields $subject @('id', 'linkedRepository', 'checks')
        if ($subject.id -cnotin $Contract.accessSubjects -or -not $seen.Add($subject.id) -or $subject.linkedRepository -cne 'alexyorke/SharpProof-preview') { throw 'Access subject or private repository linkage differs from the candidate.' }
        if ($subject.checks -isnot [array] -or $subject.checks.Count -ne $Contract.accessChecks.Count) { throw 'Every granular access check must actually be performed.' }
        $checks = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($check in $subject.checks) {
            Assert-ReceiptObservation $check $Root @('value')
            if ($check.id -cnotin $Contract.accessChecks -or -not $checks.Add($check.id) -or $check.status -cne 'passed') { throw 'Granular access check is missing, duplicated, pending, or unsuccessful.' }
            switch ($check.id) {
                'visibility' {
                    if ($check.value -cne 'private' -or $check.method -cnotin @('metadata-api', 'settings-ui', 'granular-permissions-api')) { throw 'Subject visibility must actually be private.' }
                }
                'direct-users' {
                    if ($check.value -isnot [array] -or $check.value.Count -ne 1 -or $check.value[0] -cne 'alexyorke') { throw 'Direct human access must contain only the owner.' }
                }
                'teams' {
                    if ($check.value -isnot [array] -or $check.value.Count -ne 0) { throw 'Owner-only access cannot contain a team grant.' }
                }
                'inheritance' {
                    if ($check.value -cnotin @('disabled', 'private-owner-only-verified')) { throw 'Private owner-only inheritance must be explicitly verified or disabled.' }
                }
                'actions-repositories' {
                    if ($check.value -isnot [array] -or $check.value.Count -gt 1 -or
                        ($check.value.Count -eq 1 -and $check.value[0] -cne 'alexyorke/SharpProof-preview')) { throw 'Access cannot grant an extraneous Actions repository.' }
                }
                'anonymous-access' {
                    if ($check.value -cne 'denied' -or $check.method -cne 'anonymous-access-test' -or
                        -not $check.Contains('principal') -or $check.principal -cne 'anonymous' -or
                        -not $check.Contains('httpStatus') -or ($check.httpStatus -isnot [int] -and $check.httpStatus -isnot [long]) -or $check.httpStatus -notin @(401, 403, 404)) { throw 'Actual unauthenticated access denial must be observed.' }
                }
                'non-owner-access' {
                    if ($check.value -cne 'denied' -or $check.method -cne 'non-owner-access-test' -or
                        -not $check.Contains('principal') -or $check.principal -isnot [string] -or
                        $check.principal -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9-]{0,38}\z' -or $check.principal -ieq 'alexyorke' -or
                        -not $check.Contains('principalVerified') -or -not $check.Contains('httpStatus') -or
                        ($check.httpStatus -isnot [int] -and $check.httpStatus -isnot [long]) -or $check.httpStatus -notin @(403, 404)) { throw 'Actual authenticated non-owner access denial must be observed.' }
                    Assert-ReceiptTrue $check.principalVerified
                }
            }
            if ($check.id -cin @('direct-users', 'teams', 'inheritance', 'actions-repositories') -and
                $check.method -cnotin @('settings-ui', 'granular-permissions-api')) { throw 'Granular access and inheritance cannot be inferred from package or repository metadata alone.' }
        }
    }
}

function Assert-SharpProofPrivatePreviewPromotionReceipts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$EvidenceDirectory,
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$CandidateArtifactId,
        [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{64}\z')][string]$CandidateArtifactDigest,
        [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$CandidateRunId,
        [string]$EditorReceiptPath = 'editor-acceptance.json',
        [string]$AccessReceiptPath = 'access-acceptance.json',
        [ValidateSet('Editor', 'Access', 'All')][string]$ReceiptKind = 'All'
    )
    $root = (Get-Item -LiteralPath $EvidenceDirectory -Force)
    $candidate = (Get-Item -LiteralPath $CandidateDirectory -Force)
    foreach ($directory in @($root, $candidate)) {
        if (-not $directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Receipt and candidate roots must be actual private directories without links.' }
    }
    $manifestPath = Resolve-ReceiptFile $candidate.FullName 'release-manifest.json' 4MB
    $manifestHash = $null
    $frozen = Read-ReceiptJson $manifestPath ([ref]$manifestHash)
    foreach ($field in @('sourceCommit', 'workflowCommit')) {
        if ($frozen[$field] -isnot [string] -or $frozen[$field] -cnotmatch '\A[a-f0-9]{40}\z' -or $Manifest.$field -cne $frozen[$field]) { throw 'Authenticated candidate commit identities differ from its actual manifest.' }
    }
    foreach ($field in @('sourceRepository', 'releaseRepository', 'packageVersion', 'consumerImage', 'workflowPath', 'schemaVersion')) {
        if ($Manifest.$field -cne $frozen[$field]) { throw 'Authenticated candidate identity differs from its actual manifest.' }
    }
    if ($frozen.schemaVersion -ne 1 -or $frozen.workflowPath -cne '.github/workflows/private-preview.yml' -or
        $frozen.sourceRepository -cne 'alexyorke/SharpProof' -or $frozen.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
        $frozen.packageVersion -cne '1.0.0-preview.1' -or $frozen.consumerImage -cnotmatch '\Aghcr\.io/alexyorke/sharpproof-preview@sha256:[a-f0-9]{64}\z') { throw 'Unexpected private preview candidate identity.' }
    $expectedIds = @('SharpProof', 'SharpProof.Attributes', 'SharpProof.CodeFixes', 'SharpProof.Templates', 'SharpProof.Tool', 'SharpProof.Verifier')
    $packages = [ordered]@{}
    if (@($frozen.packages).Count -ne 6 -or @($Manifest.packages).Count -ne 6) { throw 'Authenticated candidate must contain exactly six package archives.' }
    foreach ($package in $frozen.packages) {
        if ($package.id -cnotin $expectedIds -or $packages.Contains($package.id) -or $package.version -cne $frozen.packageVersion -or
            $package.fileName -cne "$($package.id).$($frozen.packageVersion).nupkg") { throw 'Candidate package identity is unexpected or duplicated.' }
        Assert-ReceiptHash $package.sha256
        $authenticated = @($Manifest.packages | Where-Object id -CEQ $package.id)
        if ($authenticated.Count -ne 1 -or $authenticated[0].sha256 -cne $package.sha256 -or $authenticated[0].version -cne $package.version) { throw 'Authenticated candidate package hashes differ from the frozen manifest.' }
        $path = Resolve-ReceiptFile $candidate.FullName $package.fileName 1GB
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $package.sha256) { throw 'Frozen candidate archive bytes differ from the manifest.' }
        $packages.Add($package.id, $package)
    }

    $contract = Get-SharpProofPrivatePreviewPromotionReceiptContract
    if ($ReceiptKind -in @('Editor', 'All')) {
        $editor = Read-ReceiptJson (Resolve-ReceiptFile $root.FullName $EditorReceiptPath 4MB)
        Assert-ReceiptBinding $editor 'SharpProof.PrivatePreviewEditorAcceptance' $frozen $manifestHash $packages $CandidateArtifactId $CandidateArtifactDigest $CandidateRunId 'sessions'
        Assert-ReceiptEditorSessions $editor.sessions $root.FullName $contract
    }
    if ($ReceiptKind -in @('Access', 'All')) {
        $access = Read-ReceiptJson (Resolve-ReceiptFile $root.FullName $AccessReceiptPath 4MB)
        Assert-ReceiptBinding $access 'SharpProof.PrivatePreviewAccessAcceptance' $frozen $manifestHash $packages $CandidateArtifactId $CandidateArtifactDigest $CandidateRunId 'subjects'
        Assert-ReceiptAccessSubjects $access.subjects $root.FullName $contract
    }
    # A valid receipt attests to supplied observations and retained bytes. This function
    # never performs an editor session, creates acceptance evidence, or grants access.
}

Export-ModuleMember -Function Get-SharpProofPrivatePreviewPromotionReceiptContract, Assert-SharpProofPrivatePreviewPromotionReceipts
