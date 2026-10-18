[CmdletBinding()]
param(
    [string]$Target = '',
    [ValidateSet('Text', 'Json')][string]$Format = 'Text',
    [string]$TargetFramework = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$checks = [Collections.Generic.List[object]]::new()
function Add-Check([string]$Name, [string]$Status, [string]$Message, [string]$Action = '') {
    $checks.Add([ordered]@{ name = $Name; status = $Status; message = $Message; action = $Action })
}
$sdk = (& dotnet --version 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { Add-Check 'sdk' 'error' 'No compatible SDK was selected.' 'Install the SDK required by global.json.' }
else { Add-Check 'sdk' 'ok' $sdk }
if ($sdk -notmatch '^\d+\.\d+\.\d+' -or [version](($sdk -split '-')[0]) -lt [version]'9.0.300') {
    Add-Check 'portable-host' 'error' 'Portable analysis requires SDK 9.0.300 / Roslyn 4.14 or later.' 'Install a supported SDK and check global.json.'
}
$runtimes = @(& dotnet --list-runtimes 2>$null)
$configuration = $null
$packages = @()
$resolvedPackages = @()
$packageIds = @('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier')
if (-not [string]::IsNullOrWhiteSpace($Target)) {
    $project = (Resolve-Path -LiteralPath $Target).Path
    if ([IO.Path]::GetExtension($project) -ne '.csproj') { throw 'Doctor -Target must name one .csproj file.' }
    # Evaluation only: no restore, build, or target execution. Use trusted projects.
    $propertyNames = @('TargetFramework', 'TargetFrameworks', 'NETCoreSdkVersion', 'MSBuildVersion',
        'SharpProofProfile', 'SharpProofFeatures', 'SharpProofVerify', 'SharpProofVerifyPolicy',
        'SharpProofAssumptionPolicy', 'SharpProofWorkerPath', 'SharpProofVerifyResultFile',
        'SharpProofVerifySarifFile', 'IntermediateOutputPath', 'MSBuildProjectDirectory', 'ProjectAssetsFile')
    $arguments = @('msbuild', $project, '-nologo', "-getProperty:$($propertyNames -join ',')", '-getItem:PackageReference')
    if ($TargetFramework) { $arguments += "-p:TargetFramework=$TargetFramework" }
    $raw = (& dotnet @arguments 2>$null | Out-String)
    if ($LASTEXITCODE -ne 0) {
        Add-Check 'project' 'error' 'Project evaluation failed.' 'Check the project with dotnet msbuild; doctor does not restore dependencies.'
    }
    else {
        $evaluation = $raw | ConvertFrom-Json
        $configuration = $evaluation.Properties
        $packages = @($evaluation.Items.PackageReference | Where-Object { $_.Identity -in $packageIds } | ForEach-Object {
                $version = if ($_.PSObject.Properties['Version']) { $_.Version } else { '' }
                [ordered]@{ id = $_.Identity; version = $version }
            })
        if ($packages.Count -eq 0) { Add-Check 'packages' 'warning' 'No direct SharpProof package references were evaluated.' 'Add SharpProof and restore the project; transitive references require inspection of the restored assets file.' }
        else { Add-Check 'packages' 'ok' "$($packages.Count) direct SharpProof package references found." }
        $assetsPath = $configuration.ProjectAssetsFile
        if ($assetsPath -and -not [IO.Path]::IsPathRooted($assetsPath)) { $assetsPath = Join-Path $configuration.MSBuildProjectDirectory $assetsPath }
        if ($packages.Count -and (-not $assetsPath -or -not (Test-Path -LiteralPath $assetsPath -PathType Leaf))) {
            Add-Check 'restore' 'warning' 'Restored package assets are unavailable; package imports and resolved versions may be incomplete.' 'Restore the consumer in this environment, then repeat doctor. Versions listed here are evaluated direct references.'
        }
        if ($assetsPath -and (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
            try {
                $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
                if ($assets.Contains('libraries') -and $assets.libraries -is [Collections.IDictionary]) {
                    $resolvedPackages = @($assets.libraries.Keys | Sort-Object | ForEach-Object {
                            $parts = $_ -split '/', 2
                            if ($parts.Count -eq 2 -and $parts[0] -in $packageIds) {
                                [ordered]@{ id = $parts[0]; version = $parts[1] }
                            }
                        })
                }
                Add-Check 'resolved-packages' 'ok' "$($resolvedPackages.Count) SharpProof packages found in existing restored assets (including transitive references)." 'Restore again if package inputs have changed; doctor does not establish that existing assets are current.'
            }
            catch {
                Add-Check 'resolved-packages' 'warning' 'Existing package assets could not be inspected.' 'Restore the consumer and repeat doctor; evaluated direct references are still shown.'
            }
        }
        $versionPackages = if ($resolvedPackages.Count) { $resolvedPackages } else { $packages }
        $versions = @($versionPackages | ForEach-Object { $_.version } | Where-Object { $_ } | Select-Object -Unique)
        if ($versions.Count -gt 1) { Add-Check 'package-versions' 'warning' 'SharpProof package versions differ.' 'Align SharpProof package versions; existing resolved assets take precedence over evaluated direct version expressions.' }
        if ($configuration.TargetFrameworks -and -not $TargetFramework) {
            Add-Check 'framework' 'warning' 'This is an outer multi-target evaluation.' 'Repeat doctor with -TargetFramework for each target framework.'
        }
        $verifyEnabled = $configuration.SharpProofVerify.Trim().ToLowerInvariant() -eq 'true'
        $verificationProfile = $configuration.SharpProofProfile.Trim().ToLowerInvariant()
        $allowedValues = [ordered]@{
            SharpProofProfile          = @('advisory', 'strict', 'off')
            SharpProofFeatures         = @('effects', 'contracts', 'all')
            SharpProofVerify           = @('true', 'false')
            SharpProofVerifyPolicy     = @('advisory', 'warn-on-unknown', 'require-proven')
            SharpProofAssumptionPolicy = @('allow', 'warn', 'error')
        }
        foreach ($property in $allowedValues.Keys) {
            $value = $configuration.$property.Trim().ToLowerInvariant()
            if ($value -and $value -notin $allowedValues[$property]) {
                Add-Check $property 'error' "The evaluated $property value is invalid." "Use one of: $($allowedValues[$property] -join ', ')."
            }
        }
        if ($verificationProfile -eq 'strict' -and -not $verifyEnabled) {
            Add-Check 'strict-profile' 'error' 'SharpProofProfile=strict requires SharpProofVerify=true.' 'Enable verification or select advisory for optional verification.'
        }
        if (-not $verificationProfile) {
            Add-Check 'configuration' 'warning' 'SharpProof profile was not evaluated; package imports may be unavailable.' 'Restore the consumer, then repeat doctor. This check does not restore.'
        }
        if (-not $verifyEnabled -or $verificationProfile -eq 'off') {
            Add-Check 'verification' 'warning' 'Build verification is disabled.' 'Set SharpProofVerify=true and select a supported profile when proof execution is intended.'
        }
        else {
            Add-Check 'verification' 'ok' 'Build verification is enabled (ordinary builds; editor design-time builds do not run proofs).'
            if ($sdk -notmatch '^\d+\.' -or [int]($sdk.Split('.')[0]) -lt 10) {
                Add-Check 'compiler-host' 'error' 'Full verification requires .NET SDK 10.0.100 or later.' 'Install a supported .NET 10 SDK. Portable diagnostics retain their separate minimum host boundary.'
            }
            else { Add-Check 'compiler-host' 'ok' 'The SDK major version supports the full verifier host.' }
            $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            $processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            $runtimeIdentifier = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
            if (($IsLinux -and ($architecture -notin @('X64', 'Arm64') -or $runtimeIdentifier -notin @('linux-x64', 'linux-arm64'))) -or
                ($IsWindows -and $architecture -ne 'X64') -or
                ($IsMacOS -and $architecture -notin @('X64', 'Arm64')) -or
                $architecture -cne $processArchitecture -or
                (-not ($IsLinux -or $IsWindows -or $IsMacOS))) {
                Add-Check 'platform' 'error' 'This full-verifier host is outside the qualified platform boundary.' 'Use matching host/process architectures on glibc Linux x64/arm64, Windows x64, or macOS x64/arm64; consult preview-support.md.'
            }
            else { Add-Check 'platform' 'ok' 'OS and architecture match a documented verifier platform; proof execution is still required to qualify this installation.' }
            $worker = $configuration.SharpProofWorkerPath
            if ($worker -and -not [IO.Path]::IsPathRooted($worker)) { $worker = Join-Path $configuration.MSBuildProjectDirectory $worker }
            if (-not $worker -or -not (Test-Path -LiteralPath $worker -PathType Leaf)) {
                Add-Check 'worker' 'error' 'Configured worker payload is unavailable.' 'Restore SharpProof.Verifier for this host and inspect SharpProofWorkerPath.'
            }
            else {
                Add-Check 'worker' 'ok' 'Configured worker payload exists.'
                $runtimeConfig = [IO.Path]::ChangeExtension($worker, 'runtimeconfig.json')
                if (Test-Path -LiteralPath $runtimeConfig) {
                    $runtime = Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json
                    $framework = $runtime.runtimeOptions.framework
                    $major = ([version]$framework.version).Major
                    if (-not @($runtimes | Where-Object { $_ -match "^$([regex]::Escape($framework.name)) $major\." }).Count) {
                        Add-Check 'worker-runtime' 'error' 'The worker runtime major version is not installed.' "Install $($framework.name) $major or use the canonical container."
                    }
                    else { Add-Check 'worker-runtime' 'ok' 'The worker runtime major version is installed.' }
                }
                Add-Check 'native-payload' 'warning' 'Native loading is not exercised by this read-only check.' 'Run the first-proof sample on this host to verify solver loading and proof execution.'
            }
        }
        $intermediateDirectory = $configuration.IntermediateOutputPath
        if (-not [IO.Path]::IsPathRooted($intermediateDirectory)) { $intermediateDirectory = Join-Path $configuration.MSBuildProjectDirectory $intermediateDirectory }
        $reportDirectory = Join-Path $intermediateDirectory 'SharpProof'
        Add-Check 'reports' 'ok' "Default report directory: $reportDirectory. Custom report paths are shown in configuration."
    }
}
else { Add-Check 'project' 'warning' 'No consumer project selected.' 'Pass -Target path/to/consumer.csproj to inspect effective configuration.' }
$report = [ordered]@{
    schemaVersion    = 1
    host             = [ordered]@{ os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString(); sdk = $sdk }
    scope            = 'Read-only SDK discovery and trusted-project evaluation; no restore, build, write probe, solver execution, or secret/environment collection.'
    packages         = $packages
    resolvedPackages = $resolvedPackages
    configuration    = $configuration
    checks           = @($checks.ToArray())
}
if ($Format -eq 'Json') { $report | ConvertTo-Json -Depth 12 }
else {
    Write-Output "SharpProof doctor - $($report.host.os) / $($report.host.architecture)"
    foreach ($package in $packages) { Write-Output "Evaluated direct reference: $($package.id) $($package.version)" }
    foreach ($package in $resolvedPackages) { Write-Output "Existing restored package: $($package.id) $($package.version)" }
    foreach ($check in $checks) {
        Write-Output "[$($check.status)] $($check.name): $($check.message)"
        if ($check.action) { Write-Output "  Next: $($check.action)" }
    }
    if ($configuration) { $configuration | Format-List | Out-String | Write-Output }
}
if (@($checks | Where-Object { $_.status -eq 'error' }).Count) { exit 1 }
