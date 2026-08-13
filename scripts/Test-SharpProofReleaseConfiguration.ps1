[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$contractPath = Join-Path $repositoryRoot 'eng\release\environment-contract.json'
$workflowPath = Join-Path $repositoryRoot '.github\workflows\package-consumers.yml'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
$workflow = Get-Content -LiteralPath $workflowPath -Raw

if ([int]$contract.schemaVersion -ne 1 -or
    [string]::IsNullOrWhiteSpace([string]$contract.repository)) {
    throw 'The release-environment contract is invalid.'
}
if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI is required to inspect release configuration.'
}

function Invoke-GitHubJson {
    param([Parameter(Mandatory = $true)][string]$Endpoint)

    $output = & gh api $Endpoint 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub API request failed for '$Endpoint': $output"
    }
    return ($output -join "`n") | ConvertFrom-Json
}

function Require-SetMembers {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Actual,
        [Parameter(Mandatory = $true)][object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $missing = @($Expected | Where-Object { [string]$_ -notin $Actual })
    if ($missing.Count -ne 0) {
        throw "$Owner is missing: $($missing -join ', ')."
    }
}

function Require-ExactSet {
    param(
        [AllowEmptyCollection()][string[]]$Actual = @(),
        [AllowEmptyCollection()][object[]]$Expected = @(),
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $expectedStrings = @($Expected | ForEach-Object { [string]$_ })
    $actualUnique = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $expectedUnique = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    if (@($Actual | Where-Object { -not $actualUnique.Add($_) }).Count -ne 0 -or
        @($expectedStrings | Where-Object {
                -not $expectedUnique.Add($_)
            }).Count -ne 0 -or
        $actualUnique.Count -ne $expectedUnique.Count -or
        @($actualUnique | Where-Object {
                -not $expectedUnique.Contains($_)
            }).Count -ne 0) {
        throw "$Owner must equal the exact contract set."
    }
}

$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'The release-configuration commit could not be resolved.'
}

$repository = [string]$contract.repository
$rulesets = @(Invoke-GitHubJson "repos/$repository/rulesets")
$activeTagRulesets = @($rulesets |
    Where-Object { $_.target -ceq 'tag' -and $_.enforcement -ceq 'active' })
if ($activeTagRulesets.Count -ne 1) {
    throw 'Exactly one active tag ruleset must own the release tag policy.'
}
$tagRuleset = Invoke-GitHubJson (
    "repos/$repository/rulesets/$($activeTagRulesets[0].id)")
$rulesetIncludes = @($tagRuleset.conditions.ref_name.include |
    ForEach-Object { [string]$_ })
$rulesetExcludes = @($tagRuleset.conditions.ref_name.exclude |
    ForEach-Object { [string]$_ })
Require-ExactSet `
    -Actual $rulesetIncludes `
    -Expected @($contract.tagRuleset.includes) `
    -Owner 'The release-tag ruleset include policy'
$expectedRulesetExcludes = @($contract.tagRuleset.excludes)
if ($expectedRulesetExcludes.Count -eq 0) {
    if ($rulesetExcludes.Count -ne 0) {
        throw 'The release-tag ruleset exclude policy must equal the exact contract set.'
    }
}
else {
    Require-ExactSet `
        -Actual $rulesetExcludes `
        -Expected $expectedRulesetExcludes `
        -Owner 'The release-tag ruleset exclude policy'
}
if (@($rulesetIncludes | Where-Object {
            $rulesetExcludes -ccontains $_
        }).Count -ne 0) {
    throw 'The release-tag ruleset cannot both include and exclude a ref.'
}
Require-SetMembers `
    -Actual @($tagRuleset.rules.type | ForEach-Object { [string]$_ }) `
    -Expected @($contract.tagRuleset.requiredRules) `
    -Owner 'The release-tag ruleset'

$environmentEvidence = @()
foreach ($required in $contract.environments) {
    $name = [string]$required.name
    $escapedName = [Uri]::EscapeDataString($name)
    $environment = Invoke-GitHubJson "repos/$repository/environments/$escapedName"
    if ($null -eq $environment.deployment_branch_policy -or
        [bool]$environment.deployment_branch_policy.protected_branches -or
        -not [bool]$environment.deployment_branch_policy.custom_branch_policies) {
        throw "Environment '$name' must use explicit tag policies."
    }
    $policies = Invoke-GitHubJson (
        "repos/$repository/environments/$escapedName/deployment-branch-policies")
    $actualRefs = @($policies.branch_policies | ForEach-Object {
            ([string]$_.type) + ':' + ([string]$_.name)
        })
    $expectedRefs = @($required.tags | ForEach-Object {
            'tag:' + ([string]$_)
        })
    Require-ExactSet `
        -Actual $actualRefs `
        -Expected $expectedRefs `
        -Owner "Environment '$name' deployment ref policies"

    $variables = Invoke-GitHubJson "repos/$repository/environments/$escapedName/variables"
    $actualVariables = @($variables.variables | ForEach-Object { [string]$_.name })
    Require-SetMembers `
        -Actual $actualVariables `
        -Expected @($required.variables) `
        -Owner "Environment '$name' variables"

    $secrets = Invoke-GitHubJson "repos/$repository/environments/$escapedName/secrets"
    $actualSecrets = @($secrets.secrets | ForEach-Object { [string]$_.name })
    $requiredSecrets = @($required.secrets)
    if ($requiredSecrets.Count -ne 0) {
        Require-SetMembers `
            -Actual $actualSecrets `
            -Expected $requiredSecrets `
            -Owner "Environment '$name' secrets"
    }

    foreach ($token in @($name) + @($required.tags) +
        @($required.variables) + @($required.secrets)) {
        if (-not $workflow.Contains([string]$token, [StringComparison]::Ordinal)) {
            throw "The release workflow does not reference '$token'."
        }
    }

    $environmentEvidence += [pscustomobject]@{
        name = $name
        tags = @($required.tags)
        variables = @($required.variables)
        secrets = @($required.secrets)
    }
}

if (-not $workflow.Contains('NuGet/login@', [StringComparison]::Ordinal) -or
    -not $workflow.Contains('id-token: write', [StringComparison]::Ordinal)) {
    throw 'The public NuGet job must use GitHub OIDC trusted publishing.'
}

$evidence = [ordered]@{
    schemaVersion = 1
    repository = $repository
    commit = $head
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    tagRulesetId = [long]$tagRuleset.id
    environments = $environmentEvidence
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
    if (-not $resolvedOutput.StartsWith(
            $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputPath must be inside the repository.'
    }
    $directory = [IO.Path]::GetDirectoryName($resolvedOutput)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText(
        $resolvedOutput,
        ($evidence | ConvertTo-Json -Depth 6) + "`n",
        [Text.UTF8Encoding]::new($false))
}

Write-Host "Validated release tags and $($environmentEvidence.Count) publishing environments for $head."
