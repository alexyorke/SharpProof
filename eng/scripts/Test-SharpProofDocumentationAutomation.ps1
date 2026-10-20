[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-docs-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$sync = Join-Path $PSScriptRoot 'Sync-SharpProofDocumentation.ps1'
$links = Join-Path $PSScriptRoot 'Test-SharpProofDocumentation.ps1'
function Assert-Failure([scriptblock]$Action) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    if (-not $failed) { throw 'Expected documentation validation to reject this fixture.' }
}
try {
    foreach ($directory in [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('src/SharpProof.Package/buildTransitive', 'src/SharpProof.Verifier/buildTransitive', 'src/SharpProof.Attributes', 'src/SharpProof.Gates', 'src/SharpProof.Analyzer.Core/ContractForValidation', 'src/SharpProof.Fuzz', 'eng/scripts', 'docs'))) {
        [IO.Directory]::CreateDirectory((Join-Path $fixture $directory)) | Out-Null
    }
    foreach ($path in [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('src/SharpProof.Package/buildTransitive', 'src/SharpProof.Verifier/buildTransitive', 'src/SharpProof.Attributes'))) {
        Copy-Item -Path (Join-Path $sourceRoot "$path/*") -Destination (Join-Path $fixture $path) -Recurse
    }
    foreach ($path in [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('eng/Versions.props', 'src/SharpProof.Gates/Program.cs', 'eng/scripts/Invoke-SharpProofContainer.ps1', 'src/SharpProof.Fuzz/FuzzOptions.cs', 'src/SharpProof.Analyzer.Core/GeneratedDiagnosticDescriptors.generated.cs', 'src/SharpProof.Analyzer.Core/ContractForValidation/ContractForDiagnosticDescriptors.generated.cs'))) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot $path) -Destination (Join-Path $fixture $path)
    }
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'), "# Fixture`n`n[Guide](docs/guide.md#guide)`n")
    $guide = Join-Path $fixture 'docs/guide.md'
    [IO.File]::WriteAllText($guide, "# Guide`n`nHuman prose stays intact.`n")
    & git -C $fixture init --quiet
    & git -C $fixture add -- .
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize documentation fixture index.' }
    & $sync -RepositoryRoot $fixture -Update
    & $sync -RepositoryRoot $fixture
    & $links -RepositoryRoot $fixture
    $generated = Join-Path $fixture 'docs/generated/source-reference.md'
    $first = [IO.File]::ReadAllText($generated)
    & $sync -RepositoryRoot $fixture -Update
    if ([IO.File]::ReadAllText($generated) -cne $first) { throw 'Generation is not idempotent.' }
    $mutations = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            ([ordered]@{ Path = 'src/SharpProof.Package/buildTransitive/SharpProof.props'; Before = '</Project>'; After = '<PropertyGroup><SharpProofDocumentationProbe>enabled</SharpProofDocumentationProbe></PropertyGroup></Project>'; Visible = 'SharpProofDocumentationProbe' }).AsReadOnly(),
            ([ordered]@{ Path = 'src/SharpProof.Attributes/SharpProofEffect.cs'; Before = 'None = 0,'; After = 'DocumentationProbe = 42, None = 0,'; Visible = 'DocumentationProbe' }).AsReadOnly(),
            ([ordered]@{ Path = 'eng/scripts/Invoke-SharpProofContainer.ps1'; Before = "'docs-update')"; After = "'docs-update', 'documentation-probe')"; Visible = 'documentation-probe' }).AsReadOnly(),
            ([ordered]@{ Path = 'src/SharpProof.Gates/Program.cs'; Before = 'Usage: SharpProof.Gates ['; After = 'Usage: SharpProof.Gates [documentation-probe|'; Visible = 'documentation-probe' }).AsReadOnly(),
            ([ordered]@{ Path = 'src/SharpProof.Fuzz/FuzzOptions.cs'; Before = 'case "--cases":'; After = 'case "--documentation-probe": case "--cases":'; Visible = '--documentation-probe' }).AsReadOnly(),
            ([ordered]@{ Path = 'src/SharpProof.Attributes/PublicAPI.Unshipped.txt'; Before = '#nullable enable'; After = '#nullable enable' + "`n" + 'SharpProof.Attributes.DocumentationProbe'; Visible = 'SharpProof.Attributes.DocumentationProbe' }).AsReadOnly(),
            ([ordered]@{ Path = 'src/SharpProof.Analyzer.Core/GeneratedDiagnosticDescriptors.generated.cs'; Before = 'id: "SP0002"'; After = 'id: "SP9999"'; Visible = 'SP9999' }).AsReadOnly()
        ))
    foreach ($mutation in $mutations) {
        $path = Join-Path $fixture $mutation.Path
        $original = [IO.File]::ReadAllText($path)
        if (-not $original.Contains($mutation.Before)) { throw "Fixture mutation no longer matches $path" }
        [IO.File]::WriteAllText($path, $original.Replace($mutation.Before, $mutation.After))
        Assert-Failure { & $sync -RepositoryRoot $fixture }
        & $sync -RepositoryRoot $fixture -Update
        & $sync -RepositoryRoot $fixture
        if (-not [IO.File]::ReadAllText($generated).Contains($mutation.Visible)) { throw 'Source addition was not rendered.' }
        [IO.File]::WriteAllText($path, $original)
        & $sync -RepositoryRoot $fixture -Update
    }
    if ([IO.File]::ReadAllText($guide) -cne "# Guide`n`nHuman prose stays intact.`n") { throw 'Authored prose changed.' }
    [IO.File]::WriteAllText($guide, "# Renamed`n")
    Assert-Failure { & $links -RepositoryRoot $fixture }
    [IO.File]::WriteAllText($guide, "# Guide`n`n[Missing](missing.md)`n")
    Assert-Failure { & $links -RepositoryRoot $fixture }
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'), '[Ghost](docs/guide.md#ghost)')
    [IO.File]::WriteAllText($guide, '# Guide' + "`n" + '```html' + "`n" + '<a id="ghost"></a>' + "`n" + '```' + "`n")
    Assert-Failure { & $links -RepositoryRoot $fixture }
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'), '# Fixture')
    $outsideDocs = Join-Path $fixture 'CONTRIBUTING.md'
    [IO.File]::WriteAllText($outsideDocs, '[Missing](missing-contributor-guide.md)')
    & git -C $fixture add -- CONTRIBUTING.md
    if ($LASTEXITCODE -ne 0) { throw 'Cannot track outside-docs fixture.' }
    Assert-Failure { & $links -RepositoryRoot $fixture }
    Write-Host 'Documentation automation regressions passed: drift, regeneration, idempotence, preserved prose, links and anchors.'
}
finally {
    # The fixture is a freshly created child of the OS temporary directory.
    if ((Split-Path $fixture -Parent) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('/', '\')) {
        throw 'Unexpected documentation fixture location.'
    }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
