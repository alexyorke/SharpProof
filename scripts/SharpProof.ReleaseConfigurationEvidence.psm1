Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofRuntimePlatform {
    $runtimeInformation = [Runtime.InteropServices.RuntimeInformation]
    if ($runtimeInformation::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::Linux)) {
        $osFamily = 'linux'
    }
    elseif ($runtimeInformation::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::Windows)) {
        $osFamily = 'windows'
    }
    elseif ($runtimeInformation::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::OSX)) {
        $osFamily = 'macos'
    }
    else {
        throw 'SharpProof cannot identify the current operating system.'
    }
    return [pscustomobject]@{
        OsFamily = $osFamily
        Architecture = $runtimeInformation::OSArchitecture.ToString().
            ToLowerInvariant()
    }
}

function Get-SharpProofReleaseAttemptId {
    $runId = [Environment]::GetEnvironmentVariable('GITHUB_RUN_ID')
    $attempt = [Environment]::GetEnvironmentVariable('GITHUB_RUN_ATTEMPT')
    if (-not [string]::IsNullOrWhiteSpace($runId) -and
        -not [string]::IsNullOrWhiteSpace($attempt)) {
        if ($runId -notmatch '^[0-9]+$' -or $attempt -notmatch '^[0-9]+$') {
            throw 'GitHub run identity must contain decimal values.'
        }
        return "$runId/$attempt"
    }

    return 'local-' + [Guid]::NewGuid().ToString('N')
}

function Assert-SharpProofReleaseConfigurationEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Evidence,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedCommit,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedRepository,

        [TimeSpan]$MaximumAge = ([TimeSpan]::FromHours(24))
    )

    $required = @(
        'schemaVersion', 'status', 'repository', 'commit', 'checkedAtUtc',
        'attemptId', 'tagRulesetId', 'tagRules', 'tagRulesetBypassActors',
        'workflowJobs', 'environments')
    $actual = @($Evidence.PSObject.Properties.Name)
    $missing = @($required | Where-Object { $actual -notcontains $_ })
    $extra = @($actual | Where-Object { $required -notcontains $_ })
    if ($missing.Count -ne 0 -or $extra.Count -ne 0) {
        throw 'Release-configuration evidence has an unexpected schema.'
    }
    if ([int]$Evidence.schemaVersion -ne 1 -or
        [string]$Evidence.status -cne 'passed' -or
        [string]$Evidence.repository -cne $ExpectedRepository -or
        [string]$Evidence.commit -cne $ExpectedCommit -or
        [string]$ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Release-configuration evidence identity is invalid.'
    }

    $checkedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            [string]$Evidence.checkedAtUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$checkedAt)) {
        throw 'Release-configuration evidence timestamp is invalid.'
    }
    $age = [DateTimeOffset]::UtcNow - $checkedAt.ToUniversalTime()
    if ($age -lt [TimeSpan]::Zero -or $age -gt $MaximumAge) {
        throw 'Release-configuration evidence timestamp is outside its freshness window.'
    }

    $attemptId = [string]$Evidence.attemptId
    if ($attemptId -notmatch '^(?:[0-9]+/[0-9]+|local-[0-9a-f]{32})$') {
        throw 'Release-configuration evidence attempt identity is invalid.'
    }
    $runId = [Environment]::GetEnvironmentVariable('GITHUB_RUN_ID')
    $attempt = [Environment]::GetEnvironmentVariable('GITHUB_RUN_ATTEMPT')
    if (-not [string]::IsNullOrWhiteSpace($runId) -and
        -not [string]::IsNullOrWhiteSpace($attempt) -and
        $attemptId -cne "$runId/$attempt") {
        throw 'Release-configuration evidence belongs to a different workflow attempt.'
    }

    if ([long]$Evidence.tagRulesetId -le 0 -or
        $null -eq $Evidence.tagRules -or
        $null -eq $Evidence.tagRulesetBypassActors) {
        throw 'Release-configuration ruleset evidence is incomplete.'
    }

    $workflowJobs = @($Evidence.workflowJobs)
    if ($workflowJobs.Count -ne 2 -or
        (($workflowJobs.id | Sort-Object) -join '|') -cne
            'publish|publish-private-preview' -or
        @($workflowJobs | Where-Object {
                [string]$_.canonicalSha256 -notmatch '^[0-9a-f]{64}$'
            }).Count -ne 0) {
        throw 'Release-configuration workflow evidence is incomplete.'
    }

    $environments = @($Evidence.environments)
    if ($environments.Count -lt 1 -or
        @($environments | Where-Object {
                [string]::IsNullOrWhiteSpace([string]$_.name) -or
                $null -eq $_.tags -or
                $null -eq $_.variables -or
                $null -eq $_.secrets
            }).Count -ne 0 -or
        @($environments.name | Group-Object | Where-Object Count -ne 1).Count -ne 0) {
        throw 'Release-configuration environment evidence is incomplete.'
    }
}

Export-ModuleMember -Function @(
    'Get-SharpProofRuntimePlatform',
    'Get-SharpProofReleaseAttemptId',
    'Assert-SharpProofReleaseConfigurationEvidence')
