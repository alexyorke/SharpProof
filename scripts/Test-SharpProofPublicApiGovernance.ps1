[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Public API governance controls require the canonical Linux container.'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$scratch = Join-Path $temporaryParent ('SharpProofPublicApiGovernance.' + [Guid]::NewGuid().ToString('N'))
$evidence = Join-Path $repository ('artifacts/public-api-governance/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$priorRepository = $env:SHARPPROOF_REPO_ROOT
$receipts = [Collections.Generic.List[object]]::new()

function Invoke-CheckedGit {
    param([string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'The isolated public API governance snapshot could not be prepared.' }
}

function Invoke-GovernanceBuild {
    param([string]$Name, [string]$ExpectedDiagnostic = '')
    $buildArguments = @('build', (Join-Path $scratch 'SharpProof.Attributes/SharpProof.Attributes.csproj'),
        '--configuration', 'Release', '--no-restore', '--nologo', '-t:Rebuild', '-p:UseSharedCompilation=false')
    $output = @(& dotnet @buildArguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = $output -join "`n"
    [IO.File]::WriteAllText((Join-Path $evidence ($Name + '.log')), $text + "`n")
    if ([string]::IsNullOrEmpty($ExpectedDiagnostic)) {
        if ($exitCode -ne 0) { throw "The '$Name' positive control failed; see $evidence." }
    }
    elseif ($exitCode -eq 0 -or $text -notmatch (': error ' + [regex]::Escape($ExpectedDiagnostic) + ':')) {
        throw "The '$Name' negative control did not fail with $ExpectedDiagnostic; see $evidence."
    }
    if ($text -match ': error (?!RS\d{4}:)[A-Z]+\d+:') {
        throw "The '$Name' control encountered a separate compiler, restore, or MSBuild error; see $evidence."
    }
    $receipts.Add([ordered]@{ name = $Name; exitCode = $exitCode; expectedDiagnostic = $ExpectedDiagnostic })
    Write-Host "Public API governance: $Name passed (expected diagnostic: '$ExpectedDiagnostic')."
}

try {
    Invoke-CheckedGit @('-C', $repository, 'clone', '--quiet', '--shared', '--no-checkout', $repository, $scratch)
    Invoke-CheckedGit @('-C', $scratch, 'checkout', '--quiet', '--detach', 'HEAD')
    Invoke-CheckedGit @('-C', $scratch, 'remote', 'set-url', 'origin', 'https://github.com/alexyorke/SharpProof')
    $patch = Join-Path $scratch 'working-tree.patch'
    Invoke-CheckedGit @('-C', $repository, 'diff', '--binary', '--full-index', '--no-ext-diff', ('--output=' + $patch), 'HEAD', '--', '.')
    if ((Get-Item -LiteralPath $patch).Length -gt 0) {
        Invoke-CheckedGit @('-C', $scratch, 'apply', '--binary', '--whitespace=nowarn', $patch)
    }
    $untracked = @(& git -C $repository ls-files --others --exclude-standard -z)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate untracked source inputs.' }
    foreach ($relative in (($untracked -join "`n") -split [char]0)) {
        if ([string]::IsNullOrEmpty($relative)) { continue }
        $source = [IO.Path]::GetFullPath((Join-Path $repository $relative))
        $destination = [IO.Path]::GetFullPath((Join-Path $scratch $relative))
        if (-not $source.StartsWith($repository + '/', [StringComparison]::Ordinal) -or
            -not $destination.StartsWith($scratch + '/', [StringComparison]::Ordinal)) {
            throw 'A source snapshot member escaped the isolated workspace.'
        }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy($source, $destination, $true)
    }
    $env:SHARPPROOF_REPO_ROOT = $scratch
    Push-Location $scratch
    try {
        $restore = @(& dotnet restore SharpProof.Attributes/SharpProof.Attributes.csproj --locked-mode --nologo 2>&1)
        if ($LASTEXITCODE -ne 0) {
            [IO.File]::WriteAllLines((Join-Path $evidence 'restore.log'), [string[]]$restore)
            throw "The isolated restore failed; see $evidence."
        }
        Invoke-GovernanceBuild 'baseline'
        $apiFiles = @('PublicAPI.Shipped.txt', 'PublicAPI.Unshipped.txt' | ForEach-Object {
                Join-Path $scratch ('SharpProof.Attributes/' + $_)
            })
        $apiContents = [Collections.Generic.List[byte[]]]::new()
        foreach ($file in $apiFiles) { $apiContents.Add([IO.File]::ReadAllBytes($file)) }
        $project = Join-Path $scratch 'SharpProof.Attributes/SharpProof.Attributes.csproj'
        $projectContents = [IO.File]::ReadAllBytes($project)
        try {
            foreach ($file in $apiFiles) { [IO.File]::Delete($file) }
            # The repository explicitly enrolls these files. Remove absent compiler inputs in
            # this private control so CS2001 cannot stand in for the analyzer's RS0016 check.
            $missingFileControl = @'
  <Target Name="RemoveAbsentApiControlInputs" BeforeTargets="CoreCompile">
    <ItemGroup>
      <AdditionalFiles Remove="$(MSBuildProjectDirectory)/PublicAPI.Shipped.txt;$(MSBuildProjectDirectory)/PublicAPI.Unshipped.txt" />
    </ItemGroup>
  </Target>
'@
            [IO.File]::WriteAllText($project, [Text.Encoding]::UTF8.GetString($projectContents).Replace(
                    '</Project>', $missingFileControl + "`n</Project>"))
            Invoke-GovernanceBuild 'missing-api-files' 'RS0016'
        }
        finally {
            [IO.File]::WriteAllBytes($project, $projectContents)
            for ($index = 0; $index -lt $apiFiles.Count; $index++) {
                [IO.File]::WriteAllBytes($apiFiles[$index], $apiContents[$index])
            }
        }
        $nullableSource = Join-Path $scratch 'SharpProof.Attributes/AllowedExceptionsAttribute.cs'
        $nullableContents = [IO.File]::ReadAllBytes($nullableSource)
        try {
            [IO.File]::WriteAllText($nullableSource, "#nullable disable`n" + [Text.Encoding]::UTF8.GetString($nullableContents))
            Invoke-GovernanceBuild 'oblivious-public-api' 'RS0041'
        }
        finally { [IO.File]::WriteAllBytes($nullableSource, $nullableContents) }
        Invoke-GovernanceBuild 'restored'
        [ordered]@{ schemaVersion = 1; controls = $receipts.ToArray() } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath (Join-Path $evidence 'controls.json') -Encoding utf8NoBOM
    }
    finally { Pop-Location }
}
finally {
    $env:SHARPPROOF_REPO_ROOT = $priorRepository
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if (-not $resolvedScratch.StartsWith($temporaryParent.TrimEnd('/') + '/SharpProofPublicApiGovernance.', [StringComparison]::Ordinal)) {
        throw 'Refusing cleanup outside the isolated public API governance workspace.'
    }
    if ([IO.Directory]::Exists($resolvedScratch)) { [IO.Directory]::Delete($resolvedScratch, $true) }
}
