[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1' -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne
        [Runtime.InteropServices.Architecture]::X64) {
    throw 'Run tutorials in the canonical Linux amd64 container.'
}
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$evidenceRoot = Join-Path $repositoryRoot "artifacts/tutorials/$runId"
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('SharpProof.Tutorials.' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidenceRoot)
[void][IO.Directory]::CreateDirectory($scratch)
$sourceRoot = Join-Path $scratch 'source'
$utf8 = [Text.UTF8Encoding]::new($false)

function Invoke-LoggedDotnet([string[]]$Arguments, [string]$LogPath) {
    $output = @(& dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText($LogPath, ($output -join "`n") + "`n", $utf8)
    return $exitCode
}

try {
    & git clone --quiet --shared --no-checkout $repositoryRoot $sourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the private source checkout.' }
    $commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    & git -C $sourceRoot checkout --quiet --detach $commit
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out the source commit.' }
    $patch = Join-Path $scratch 'working-tree.patch'
    & git -C $repositoryRoot diff HEAD --binary "--output=$patch"
    if ($LASTEXITCODE -ne 0) { throw 'Could not snapshot tracked working-tree changes.' }
    if ((Get-Item -LiteralPath $patch).Length -gt 0) {
        & git -C $sourceRoot apply --binary $patch
        if ($LASTEXITCODE -ne 0) { throw 'Could not apply the source snapshot.' }
    }
    $untrackedCode = @(& git -C $repositoryRoot ls-files --others --exclude-standard -- '*.cs' '*.csproj' '*.props' '*.targets')
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect untracked source.' }
    if (@($untrackedCode | Where-Object { $_ -notlike 'docs/tutorials/*' }).Count) {
        throw 'Commit non-tutorial untracked source before running the tutorial snapshot.'
    }
    [xml]$release = Get-Content (Join-Path $sourceRoot 'SharpProof.Release.props') -Raw
    $version = ([string]$release.Project.PropertyGroup.SharpProofPackageVersion).Replace(
        '$(SharpProofVersionPrefix)', [string]$release.Project.PropertyGroup.SharpProofVersionPrefix)
    $feed = Join-Path $scratch 'feed'
    [void][IO.Directory]::CreateDirectory($feed)
    $manifest = Get-Content (Join-Path $sourceRoot 'scripts/package-projects.json') -Raw | ConvertFrom-Json
    Write-Host "Building tutorial feed: $version"
    foreach ($project in $manifest.projects) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        $exitCode = Invoke-LoggedDotnet -Arguments @(
            'pack', (Join-Path $sourceRoot $project), '-c', 'Release', '-o', $feed, '--nologo') `
            -LogPath (Join-Path $evidenceRoot "pack-$name.log")
        if ($exitCode -ne 0) { throw "Packing $name failed; see the evidence directory." }
    }
    $nugetConfig = Join-Path $scratch 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    [IO.File]::WriteAllText($nugetConfig, @"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="SharpProof*" /></packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.*" /><package pattern="System.*" />
      <package pattern="NETStandard.*" /><package pattern="runtime.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@, $utf8)
    $cases = @(
        @{ Id = '01-first-contract'; Native = $false; Policy = 'advisory'; Exit = 0 },
        @{ Id = '02-diagnostics'; Native = $false; Policy = 'advisory'; Exit = 0 },
        @{ Id = '03-effects'; Native = $true; Policy = 'require-proven'; Exit = 0 },
        @{ Id = '04-branching'; Native = $true; Policy = 'require-proven'; Exit = 0 },
        @{ Id = '05-loops-and-state'; Native = $true; Policy = 'require-proven'; Exit = 0 },
        @{ Id = '06-outcomes'; Native = $true; Policy = 'advisory'; Exit = 1 },
        @{ Id = '07-companion'; Native = $false; Policy = 'advisory'; Exit = 0 },
        @{ Id = '08-trusted-boundary'; Native = $true; Policy = 'advisory'; Exit = 0 }
    )
    $transcript = [Collections.Generic.List[string]]::new()
    function Publish-Line([string]$Line) { $transcript.Add($Line); Write-Host $Line }
    foreach ($case in $cases) {
        $id = $case.Id
        $consumer = Join-Path $scratch $id
        [void][IO.Directory]::CreateDirectory($consumer)
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "code/$id.cs") -Destination (Join-Path $consumer 'Example.cs')
        $verifierReference = if ($case.Native) { "<PackageReference Include=`"SharpProof.Verifier`" Version=`"$version`" PrivateAssets=`"all`" />" } else { '' }
        $verify = if ($case.Native) { 'true' } else { 'false' }
        [IO.File]::WriteAllText((Join-Path $consumer 'Example.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable>
    <SharpProofProfile>advisory</SharpProofProfile><SharpProofFeatures>all</SharpProofFeatures>
    <SharpProofVerify>$verify</SharpProofVerify><SharpProofVerifyPolicy>$($case.Policy)</SharpProofVerifyPolicy>
    <SharpProofAssumptionPolicy>warn</SharpProofAssumptionPolicy>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpProof.Attributes" Version="$version" />
    <PackageReference Include="SharpProof" Version="$version" PrivateAssets="all" />
    $verifierReference
  </ItemGroup>
</Project>
"@, $utf8)
        if ($id -eq '02-diagnostics') {
            [IO.File]::WriteAllText((Join-Path $consumer '.editorconfig'), @'
root = true
[*.cs]
dotnet_diagnostic.SP0045.severity = warning
dotnet_diagnostic.SP0047.severity = warning
'@, $utf8)
        }
        $resultPath = Join-Path $consumer 'result.json'
        $sarifPath = Join-Path $consumer 'result.sarif'
        $project = Join-Path $consumer 'Example.csproj'
        $restoreExit = Invoke-LoggedDotnet -Arguments @('restore', $project, '--configfile', $nugetConfig,
            '--packages', (Join-Path $scratch 'packages'), '--nologo') -LogPath (Join-Path $evidenceRoot "$id-restore.log")
        if ($restoreExit) { throw "$id restore failed; see the evidence directory." }
        $buildExit = Invoke-LoggedDotnet -Arguments @('build', $project, '-c', 'Release', '--no-restore', '--nologo',
            "-p:SharpProofVerifyResultFile=$resultPath", "-p:SharpProofVerifySarifFile=$sarifPath") `
            -LogPath (Join-Path $evidenceRoot "$id-build.log")
        Publish-Line "[$id] build exit: $buildExit"
        if ($buildExit -ne $case.Exit) { throw "$id unexpected exit $buildExit; see the evidence directory." }
        if ($id -eq '02-diagnostics') {
            $log = [IO.File]::ReadAllText((Join-Path $evidenceRoot "$id-build.log"))
            foreach ($code in @('SP0027', 'SP0045', 'SP0047')) {
                if (-not $log.Contains($code)) { throw "$id did not report $code." }
            }
        }
        if ($case.Native) {
            Copy-Item -LiteralPath $resultPath -Destination (Join-Path $evidenceRoot "$id-result.json")
            Copy-Item -LiteralPath $sarifPath -Destination (Join-Path $evidenceRoot "$id-result.sarif")
            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            $expectedClaims = switch ($id) {
                '03-effects' { 4 }
                '04-branching' { 3 }
                '05-loops-and-state' { 3 }
                '06-outcomes' { 3 }
                '08-trusted-boundary' { 1 }
            }
            if ($result.runStatus -ne 'Complete' -or $result.failureReason -ne 'None' -or
                @($result.claimResults).Count -ne $expectedClaims) {
                throw "$id did not complete the expected claim set."
            }
            Publish-Line "  run: $($result.runStatus); failure: $($result.failureReason)"
            $claimLines = foreach ($claim in $result.claimResults) {
                $entry = @($result.manifest.claims | Where-Object claimId -eq $claim.claimId)[0]
                "  $($entry.callableId) | $($entry.kind)/$($entry.effectContractKind) | $($claim.outcome) | $($claim.reason) | $($claim.effectCertainty) | $($claim.vacuity)"
            }
            foreach ($line in @($claimLines | Sort-Object)) { Publish-Line $line }
            if ($id -in @('03-effects', '04-branching', '05-loops-and-state') -and
                @($result.claimResults | Where-Object outcome -ne 'Proven').Count) {
                throw "$id did not prove all selected claims."
            }
            if ($id -eq '06-outcomes' -and
                ((@($result.claimResults.outcome | Sort-Object) -join ',') -ne 'Proven,Refuted,Unknown')) {
                throw 'Mixed outcomes changed; review the tutorial evidence.'
            }
            if ($id -eq '08-trusted-boundary' -and
                ($result.claimResults[0].outcome -ne 'Proven' -or
                 $result.claimResults[0].effectCertainty -ne 'TrustedCompleteBoundary')) {
                throw 'Trusted boundary evidence changed; review its declaration.'
            }
        }
    }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'transcript.txt'), ($transcript -join "`n") + "`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'source-commit.txt'), $commit + "`n", $utf8)
    Write-Host "Tutorials passed. Evidence: $evidenceRoot"
}
finally {
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedScratch.StartsWith($parent, [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($resolvedScratch) -notlike 'SharpProof.Tutorials.*') {
        throw 'Refusing to remove a scratch directory outside the tutorial workspace.'
    }
    if (Test-Path -LiteralPath $resolvedScratch) { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force }
}
